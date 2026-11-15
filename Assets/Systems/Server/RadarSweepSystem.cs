using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(RadarControlSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct RadarSweepSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        double now = SystemAPI.Time.ElapsedTime;
        ComponentLookup<Velocity> velocities = SystemAPI.GetComponentLookup<Velocity>(true);
        ComponentLookup<Processor> processors = SystemAPI.GetComponentLookup<Processor>(true);

        foreach (var (radarRef, contacts, radarTransform, radarEntity) in
                 SystemAPI.Query<RefRW<Radar>, DynamicBuffer<RadarContact>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            ref var radar = ref radarRef.ValueRW;
            if (!radar.PulseReady) continue;
            radar.PulseReady = false;

            radar.RequestLED.Blink();

            float3 radarPos = radarTransform.ValueRO.Position;
            quaternion toLocal = math.inverse(radarTransform.ValueRO.Rotation);

            float3 radarVel = default;
            if (velocities.TryGetComponent(radar.Owner, out Velocity ownVel)) radarVel = ownVel.Value;

            float3 fwd = radar.Beam;
            RadarMath.BeamFrame(fwd, out float3 right, out float3 up);
            float2 tan = radar.BeamTan;
            float gain = RadarMath.Gain(tan);
            bool omni = gain <= 1.0001f;

            DebugEx.DrawLine(radarTransform.ValueRO.Position, radarTransform.ValueRO.Position + fwd * 5f, Color.white, radar.DwellTime);

            foreach (var (signature, targetTransform, target) in
                     SystemAPI.Query<RefRO<RadarSignature>, RefRO<LocalTransform>>().WithEntityAccess())
            {
                if (target == radar.Owner || target == radarEntity) continue;

                float3 rel = math.rotate(toLocal, targetTransform.ValueRO.Position - radarPos);
                float r2 = math.lengthsq(rel);
                if (r2 < 1e-4f)
                {
                    DebugEx.DrawLine(radarTransform.ValueRO.Position, targetTransform.ValueRO.Position, Color.magenta, radar.DwellTime);
                    DebugEx.DrawSphere(targetTransform.ValueRO.Position, 0.5f, Color.magenta, radar.DwellTime);
                    continue;
                }
                float range = math.sqrt(r2);
                float3 dir = rel / range;

                float pattern = 1f;
                if (!omni)
                {
                    float z = math.dot(dir, fwd);
                    if (z <= 1e-3f)
                    {
                        DebugEx.DrawLine(radarTransform.ValueRO.Position, targetTransform.ValueRO.Position, Color.red, radar.DwellTime);
                        DebugEx.DrawSphere(targetTransform.ValueRO.Position, 0.5f, Color.red, radar.DwellTime);
                        continue;
                    }
                    float bu = math.dot(dir, right) / (tan.x * z);
                    float bv = math.dot(dir, up) / (tan.y * z);
                    float q = (bu * bu) + (bv * bv);
                    if (q > 1f)
                    {
                        DebugEx.DrawLine(radarTransform.ValueRO.Position, targetTransform.ValueRO.Position, Color.red, radar.DwellTime);
                        DebugEx.DrawSphere(targetTransform.ValueRO.Position, 0.5f, Color.red, radar.DwellTime);
                        continue;
                    }
                    pattern = math.exp2(-q);
                }

                float g = gain * pattern;
                float echo = radar.PeakPower * g * g * signature.ValueRO.RadarCrossSection / (r2 * r2);
                float snr = echo / radar.Sensitivity;

                if (snr /* * RadarMath.Fluctuation(ref radar.Rng) */ < 1f)
                {
                    DebugEx.DrawLine(radarTransform.ValueRO.Position, targetTransform.ValueRO.Position, Color.orange, radar.DwellTime);
                    DebugEx.DrawSphere(targetTransform.ValueRO.Position, 0.5f, Color.orange, radar.DwellTime);
                    continue;
                }

                float k = math.rsqrt(2f * snr);
                float gx = 0f; // RadarMath.Gaussian(ref radar.Rng)
                float gy = 0f; // RadarMath.Gaussian(ref radar.Rng)
                float gr = 0f; // RadarMath.Gaussian(ref radar.Rng)
                float3 noisyDir = math.normalize(dir + (right * (gx * tan.x * k / RadarMath.Overlap))
                                                     + (up * (gy * tan.y * k / RadarMath.Overlap)));
                float noisyRange = math.max(0f, range + (gr * radar.RangeResolution * k));

                float3 targetVel = default;
                if (velocities.TryGetComponent(target, out Velocity tv)) targetVel = tv.Value;
                float radial = math.dot(math.rotate(toLocal, targetVel - radarVel), dir);

                float emissionLevel = 0f;
                if (processors.TryGetComponent(target, out Processor processor) && processor.Source.Code.IsCreated)
                {
                    float powerMultiplier;
                    if (processor.Signal != LanguageCore.Runtime.Signal.None || processor.IsKeyRequested)
                    {
                        powerMultiplier = 0.05f;
                    }
                    else if (processor.DebugContext.IsBeingDebugged && processor.DebugContext.Stopped
                        is ProcessorDebugSystemServer.StopReason.Breakpoint
                        or ProcessorDebugSystemServer.StopReason.Pause
                        or ProcessorDebugSystemServer.StopReason.RuntimeException
                        or ProcessorDebugSystemServer.StopReason.Signal
                        or ProcessorDebugSystemServer.StopReason.StepForward
                        or ProcessorDebugSystemServer.StopReason.StepIn
                        or ProcessorDebugSystemServer.StopReason.StepOut
                        or ProcessorDebugSystemServer.StopReason.StepInstruction)
                    {
                        powerMultiplier = 0.4f;
                    }
                    else
                    {
                        powerMultiplier = 1f;
                    }

                    float rx = processor.EmissionPower * powerMultiplier * g / r2;
                    emissionLevel = rx / radar.Sensitivity /* * RadarMath.Fluctuation(ref radar.Rng) */;
                }

                DebugEx.DrawLine(radarTransform.ValueRO.Position, targetTransform.ValueRO.Position, Color.green, radar.DwellTime);
                DebugEx.DrawSphere(targetTransform.ValueRO.Position, 0.5f, Color.green, radar.DwellTime);
                DebugEx.Label(targetTransform.ValueRO.Position + new float3(0f, 2f, 0f), $"{radial} | {snr} | {emissionLevel}", radar.DwellTime);
                RadarMath.InsertContact(contacts, new RadarContact
                {
                    Pos = noisyDir * noisyRange,
                    RadialVel = radial,
                    Time = now,
                    Strength = RadarMath.ToLevel(snr),
                    Emission = RadarMath.ToLevel(emissionLevel),
                    Source = target,
                }, radar.MaxContacts);
                radar.ResponseLED.Blink();
            }
        }
    }
}
