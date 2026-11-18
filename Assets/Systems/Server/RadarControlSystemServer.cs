using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(RadarSweepSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct RadarControlSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        PausableTime t = SystemAPI.GetSingleton<PausableTime>();
        if (t.IsPaused) return;

        float dt = t.DeltaTime;
        double now = t.ElapsedTime;

        foreach ((var radar, var transform) in SystemAPI.Query<RefRW<Radar>, RefRO<LocalTransform>>())
        {
            if (radar.ValueRW.ReqDirty)
            {
                ApplyRequest(ref radar.ValueRW, now);
            }
            else if (!radar.ValueRW.PreparingPulse)
            {
                continue;
            }

            float3 want0 = RadarMath.IsOmni(radar.ValueRW.BeamTan) ? radar.ValueRW.Aim : ScanPoint(in radar.ValueRW);

            float3 dishTarget = RadarMath.ClampToTravel(in radar.ValueRW, want0);
            float3 want = RadarMath.ClampToCone(want0, dishTarget, radar.ValueRW.CosElec);

            if (math.dot(want, radar.ValueRW.Aim) < radar.ValueRW.CosElec)
            {
                radar.ValueRW.Aim = RadarMath.MoveDish(in radar.ValueRW, dishTarget, dt);
                RefRW<LocalTransform> radarTransform = SystemAPI.GetComponentRW<LocalTransform>(radar.ValueRO.Transform);
                radarTransform.ValueRW.Rotation = quaternion.LookRotation(radar.ValueRW.Aim, transform.ValueRO.Up());
            }

            radar.ValueRW.Beam = RadarMath.ClampToCone(want, radar.ValueRW.Aim, radar.ValueRW.CosElec);

            if (math.dot(radar.ValueRW.Beam, want) >= RadarMath.AlignCos)
            {
                radar.ValueRW.DwellLeft -= dt;
                if (radar.ValueRW.DwellLeft <= 0f)
                {
                    radar.ValueRW.PreparingPulse = false;
                    radar.ValueRW.PulseReady = true;
                    AdvanceScan(ref radar.ValueRW, now);
                    radar.ValueRW.DwellLeft = radar.ValueRW.DwellTime;
                }
            }
        }
    }

    static void ApplyRequest(ref Radar radar, double now)
    {
        float l = math.length(radar.ReqCenter);
        radar.Center = l > 1e-4f ? radar.ReqCenter / l : new float3(0, 0, 1);
        radar.Sector = math.clamp(radar.ReqSector, 0f, math.PI);
        int h = math.clamp(radar.ReqHalvings, 0, 16);
        radar.BeamTan = math.max(new float2(RadarMath.OmniTan * math.exp2(-h)), radar.MinBeamTan);   // hardware floor

        radar.ScanTheta = 0f;
        radar.ScanPhi = 0f;
        radar.ScanSign = 1f;

        // Only a yaw rotor and no electronic steering: the beam can only move along the horizon.
        radar.RingScan = radar.SlewRate.x > 0f && radar.SlewRate.y <= 0f && radar.CosElec >= 1f;

        if (radar.RingScan)
        {
            // Project the requested cone onto the yaw axis: the range of horizontal directions it spans.
            float3 hc = new(radar.Center.x, 0f, radar.Center.z);
            float hl = math.length(hc);
            float sinS = math.sin(radar.Sector);
            if (radar.Sector >= math.PI * 0.5f) radar.YawHalf = math.PI;
            else if (hl > 1e-4f) radar.YawHalf = sinS >= hl ? math.PI : math.asin(sinS / hl);   // cone contains the vertical axis => all yaws
            else radar.YawHalf = 0f;                                                              // centre straight up, tiny cone: nothing to sweep
            radar.RingCenter = hl > 1e-4f
                ? hc / hl
                : math.normalizesafe(new float3(radar.Aim.x, 0f, radar.Aim.z), new float3(0, 0, 1));

            // One beam position covers the whole yaw range => stare.
            radar.SinglePosition = radar.YawHalf <= 1e-3f || math.atan(radar.BeamTan.x) >= radar.YawHalf;
            bool full = radar.YawHalf >= math.PI - 1e-3f;
            radar.ScanPhi = (!radar.SinglePosition && !full) ? -radar.YawHalf : 0f;   // limited sweeps start at one end
        }
        else
        {
            // A cone's half-angle theta is a circle of radius tan(theta) in the beam's tangent plane, so a centred
            // beam covers the whole sector when that radius fits inside the narrower beam half-width.
            radar.SinglePosition = RadarMath.IsOmni(radar.BeamTan)
                || (radar.Sector < math.PI * 0.5f && math.tan(radar.Sector) <= math.cmin(radar.BeamTan));
        }

        radar.DwellLeft = radar.DwellTime;
        radar.PulseReady = false;
        radar.SweepStart = now;
        radar.Revisit = RadarMath.EstimateRevisit(in radar);
        radar.ReqDirty = false;
        radar.PreparingPulse = true;
    }

    // Ring: rotate the centre direction about mount Y by ScanPhi (stays exactly on the horizon).
    // Spiral: theta = angle from the centre, phi = rotation around it.
    static float3 ScanPoint(in Radar radar)
    {
        if (radar.RingScan)
        {
            float3 side = math.cross(new float3(0, 1, 0), radar.RingCenter);
            math.sincos(radar.ScanPhi, out float s, out float c);
            return (radar.RingCenter * c) + (side * s);
        }

        RadarMath.PerpBasis(radar.Center, out float3 u, out float3 v);
        math.sincos(radar.ScanTheta, out float sT, out float cT);
        math.sincos(radar.ScanPhi, out float sP, out float cP);
        return (radar.Center * cT) + (((u * cP) + (v * sP)) * sT);
    }

    static void AdvanceScan(ref Radar radar, double now)
    {
        if (radar.SinglePosition)       // staring: every dwell is a complete "sweep"
        {
            radar.Revisit = (float)(now - radar.SweepStart);
            radar.SweepStart = now;
            radar.SweepCount++;
            return;
        }

        if (radar.RingScan)
        {
            // Step = the beam's horizontal extent. The beam's right axis is horizontal, so that is BeamTan.x.
            float step = math.atan(radar.BeamTan.x) * RadarMath.Overlap;
            bool full = radar.YawHalf >= math.PI - 1e-3f;
            bool passDone = false;

            radar.ScanPhi += radar.ScanSign * step;
            if (radar.ScanPhi > radar.YawHalf)
            {
                if (full) radar.ScanPhi -= 2f * math.PI;                       // continuous rotation
                else { radar.ScanPhi = radar.YawHalf; radar.ScanSign = -1f; }         // limited sector: sweep back and forth
                passDone = true;
            }
            else if (radar.ScanPhi < -radar.YawHalf)
            {
                radar.ScanPhi = -radar.YawHalf;
                radar.ScanSign = 1f;
                passDone = true;
            }

            if (passDone)
            {
                radar.Revisit = (float)(now - radar.SweepStart);
                radar.SweepStart = now;
                radar.SweepCount++;
            }
            return;
        }

        // Spiral with spacing from the real (elliptical) beam footprint:
        // step along the ring uses the beam's extent along the ring direction, ring pitch uses its extent radially.
        RadarMath.PerpBasis(radar.Center, out float3 u, out float3 v);
        math.sincos(radar.ScanTheta, out float sT, out float cT);
        math.sincos(radar.ScanPhi, out float sP, out float cP);
        float3 e = (u * cP) + (v * sP);
        float3 p = (radar.Center * cT) + (e * sT);      // current point
        float3 rho = (e * cT) - (radar.Center * sT);    // direction of increasing theta (radial)
        float3 tau = (v * cP) - (u * sP);           // direction of increasing phi (along the ring)

        RadarMath.BeamFrame(p, out float3 right, out float3 up);
        float stepRadial = math.atan(RadarMath.EllipseRadius(rho, right, up, radar.BeamTan)) * RadarMath.Overlap;
        float stepRing = math.atan(RadarMath.EllipseRadius(tau, right, up, radar.BeamTan)) * RadarMath.Overlap;

        float dPhi = math.min(math.PI, stepRing / math.max(sT, 1e-3f));   // arc length stepRing on a ring of radius sin(theta)
        radar.ScanPhi += dPhi;
        radar.ScanTheta += stepRadial * dPhi / (2f * math.PI);                // one full turn advances theta by stepRadial

        if (radar.ScanTheta > radar.Sector + (0.25f * stepRadial))                   // sweep finished: measure revisit time, restart
        {
            radar.ScanTheta = 0f;
            radar.ScanPhi = 0f;
            radar.Revisit = (float)(now - radar.SweepStart);
            radar.SweepStart = now;
            radar.SweepCount++;
        }
    }
}
