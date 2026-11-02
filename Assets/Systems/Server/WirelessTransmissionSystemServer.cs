#if UNITY_EDITOR && EDITOR_DEBUG
#define DEBUG_LINES
#endif

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;

[InternalBufferCapacity(4)]
public struct RxPacket : IBufferElementData
{
    public float3 Direction;
    public sbyte MarginDb;
}

public struct SendRequest
{
    public float3 Aim;
    public byte BeamHalvings;
}

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct WirelessTransmissionSystemServer : ISystem
{
    public const float TransmissionRadius = 100f;

    ComponentLookup<Processor> processorComponentQ;
    ComponentLookup<Radio> radioComponentQ;
    ComponentLookup<LocalTransform> transformComponentQ;
    BufferLookup<BufferedLogPiece> logsBufferQ;

    static readonly ProfilerMarker _CellVisibilityCheck = new(ProfilerCategory.Scripts, "TransmissionSystem.CellVisibilityCheck");
    static readonly ProfilerMarker _CellOperation = new(ProfilerCategory.Scripts, "TransmissionSystem.CellOperation");

    [BurstCompile]
    void ISystem.OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<RadioEnvironment>();
        processorComponentQ = state.GetComponentLookup<Processor>(false);
        radioComponentQ = state.GetComponentLookup<Radio>(true);
        transformComponentQ = state.GetComponentLookup<LocalTransform>(true);
        logsBufferQ = state.GetBufferLookup<BufferedLogPiece>(false);
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        processorComponentQ.Update(ref state);
        radioComponentQ.Update(ref state);
        transformComponentQ.Update(ref state);
        logsBufferQ.Update(ref state);

        var radioEnvironment = SystemAPI.GetSingleton<RadioEnvironment>();

        foreach (var (processor, radio, transform, logBuffer, log, entity) in
            SystemAPI.Query<RefRW<Processor>, RefRO<Radio>, RefRO<LocalTransform>, DynamicBuffer<BufferedLogPiece>, RefRW<LogPieces>>()
            .WithEntityAccess())
        {
            if (processor.ValueRW.OutgoingTransmissions.Length == 0) continue;
            BufferedUnitTransmissionOutgoing transmission = processor.ValueRW.OutgoingTransmissions[0];
            if (!transmission.Metadata.IsWireless) continue;

            processor.ValueRW.OutgoingTransmissions.RemoveAt(0);

            new UnitLog_Transmission_WirelessOut(MonoTime.UnixSeconds, ++log.ValueRW.LastIndex, transmission.Metadata.Wireless, transmission.Data).Write(logBuffer);

            float3 sourcePoint = transform.ValueRO.TransformPoint(processor.ValueRO.WirelessTransmissionPoint);

            NativeParallelHashMap<uint, NativeList<QuadrantEntity>>.ReadOnly map = QuadrantSystem.GetMap(state.WorldUnmanaged);
            Cell grid = Cell.ToGrid(sourcePoint);

            processor.ValueRW.NetworkSendLED.Blink();

#if DEBUG_LINES
            float beamDeg = math.clamp(transmission.Metadata.Wireless.BeamHalvings, radio.ValueRO.MinBeamHalvings, math.min(radio.ValueRO.MaxBeamHalvings, GainDb.Length - 1));
            if (!transmission.Metadata.Wireless.Aim.Equals(float3.zero) && beamDeg < 180f)
            {
                float3 globalDirection = transform.ValueRO.TransformDirection(transmission.Metadata.Wireless.Aim);
                //DebugEx.DrawLine(
                //    sourcePoint,
                //    globalDirection * TransmissionRadius,
                //    Color.cyan,
                //    1f,
                //    true
                //);
                DebugEx.DrawCone(
                    sourcePoint,
                    globalDirection,
                    TransmissionRadius,
                    beamDeg,
                    Color.cyan,
                    1f,
                    true);
            }
            else
            {
                DebugEx.DrawPoint(
                    sourcePoint,
                    TransmissionRadius,
                    Color.cyan,
                    1f,
                    true);
                DebugEx.DrawSphere(
                    sourcePoint,
                    TransmissionRadius,
                    Color.cyan,
                    1f,
                    true);
            }
#endif

            //float cosAngle = math.abs(math.cos(transmission.Metadata.Wireless.Angle));

            int diff = (int)(TransmissionRadius / Cell.Size * 0.5f + 2f);

            for (int x = -diff; x <= diff; x++)
            {
                for (int z = -diff; z <= diff; z++)
                {
                    Cell p = grid + new Cell(x, z);

                    if (!map.TryGetValue(p.key, out NativeList<QuadrantEntity> cell)) continue;
                    if (cell.Length == 0) continue;

#if _DEBUG_LINES
                    //Cell.Draw(p, 1f);
#endif

                    using (var marker = _CellOperation.Auto())
                    {
                        for (int i = 0; i < cell.Length; i++)
                        {
                            if (cell[i].Entity == entity) continue;
                            if (!processorComponentQ.HasComponent(cell[i].Entity)) continue;
                            if (!radioComponentQ.HasComponent(cell[i].Entity)) continue;
                            RefRW<Processor> other = processorComponentQ.GetRefRW(cell[i].Entity);
                            RefRO<Radio> otherRadio = radioComponentQ.GetRefRO(cell[i].Entity);
                            RefRO<LocalTransform> otherTransform = transformComponentQ.GetRefRO(cell[i].Entity);

                            float3 receiverPoint = otherTransform.ValueRO.TransformPoint(other.ValueRO.WirelessTransmissionPoint);
                            float3 receiveVector = sourcePoint - receiverPoint;
                            float3 receiveDirection = math.normalize(receiveVector);

                            int obstructions = 0;

                            SendRequest req = new()
                            {
                                Aim = transmission.Metadata.Wireless.Aim,
                                BeamHalvings = transmission.Metadata.Wireless.BeamHalvings,
                            };

                            if (!TryDeliver(radio.ValueRO, transform.ValueRO.InverseTransformPoint(sourcePoint), req, radio.ValueRO, transform.ValueRO.InverseTransformPoint(receiverPoint), radioEnvironment, obstructions, 0f, out var pkt))
                            {
                                continue;
                            }

                            float noiseMw = math.pow(10f, (otherRadio.ValueRO.SensitivityDbm - radioEnvironment.RequiredSnrDb) / 10f);
                            float jamMw = 0f;

                            foreach (var (jammer, jammerTransform) in
                                SystemAPI.Query<RefRO<RadioJammer>, RefRO<LocalTransform>>())
                            {
                                if (!jammer.ValueRO.IsActive) continue;

                                float dist = math.distance(receiverPoint, jammerTransform.ValueRO.Position);
                                float pathLoss = radioEnvironment.RefLossDb
                                               + 10f * radioEnvironment.PathLossExp * math.log10(dist)
                                               + obstructions * radioEnvironment.ObstructionDb;
                                jamMw += math.pow(10f, (jammer.ValueRO.EirpDbm - pathLoss + otherRadio.ValueRO.RxGainDbi) / 10f);
                            }
                            float jamRiseDb = 10f * math.log10(1f + jamMw / noiseMw);

                            if (jamMw != 0f && !TryDeliver(radio.ValueRO, transform.ValueRO.InverseTransformPoint(sourcePoint), req, radio.ValueRO, transform.ValueRO.InverseTransformPoint(receiverPoint), radioEnvironment, obstructions, jamRiseDb, out pkt))
                            {
                                other.ValueRW.NetworkReceiveLED.Blink(2);
                                continue;
                            }

#if DEBUG_LINES
                            DebugEx.DrawLine(sourcePoint, receiverPoint, Color.green, 1f, true);
                            DebugEx.DrawSphere(receiverPoint, 1f, Color.green, 1f, true);
#endif

                            other.ValueRW.NetworkReceiveLED.Blink(1);

                            ref FixedList128Bytes<BufferedUnitTransmission> transmissions = ref other.ValueRW.IncomingTransmissions;

                            IncomingWirelessUnitTransmissionMetadata otherIncomingMetadata = new()
                            {
                                Direction = pkt.Direction,
                                MarginDb = pkt.MarginDb,
                            };

                            if (logsBufferQ.TryGetBuffer(cell[i].Entity, out DynamicBuffer<BufferedLogPiece> otherLog))
                            {
                                new UnitLog_Transmission_WirelessIn(MonoTime.UnixSeconds, ++log.ValueRW.LastIndex, otherIncomingMetadata, transmission.Data).Write(logBuffer);
                            }

                            if (transmissions.Length >= transmissions.Capacity) transmissions.RemoveAt(0);
                            transmissions.Add(new()
                            {
                                Data = transmission.Data,
                                Metadata = new IncomingUnitTransmissionMetadata()
                                {
                                    IsWireless = true,
                                    Wireless = otherIncomingMetadata,
                                },
                            });

                            foreach (var player in SystemAPI.Query<RefRO<RealPlayer>>())
                            {
                                if (math.distance(player.ValueRO.Position, sourcePoint) < 50f ||
                                    math.distance(player.ValueRO.Position, receiverPoint) < 50f)
                                {
                                    NetcodeUtils.CreateRPC(state.WorldUnmanaged, new WirelessTransmissionEventRpc()
                                    {
                                        Origin = sourcePoint,
                                        Destination = receiverPoint,
                                        Data = transmission.Data,
                                    }, player.ValueRO.Connection);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    [BurstCompile]
    static float PointSquareDistance(in float2 point, in float2 a, in float2 b)
    {
        float dx = math.max(math.max(a.x - point.x, point.x - b.x), 0f);
        float dy = math.max(math.max(a.y - point.y, point.y - b.y), 0f);
        return math.sqrt(dx * dx + dy * dy);
    }

    [BurstCompile]
    static bool IsSquareVisible(in float2 viewPos, in float2 viewDir, float fovAngle, in float2 a, in float2 b, float viewRange = float.MaxValue)
    {
        if (PointInRectangle(viewPos, a, b)) return true;

        ReadOnlySpan<float2> square = stackalloc float2[]
        {
            a,
            new float2(a.x, b.y),
            new float2(b.x, a.y),
            b,
        };

        float halfFov = fovAngle / 2f;
        float2 leftDir;
        float2 rightDir;
        {
            float cos = math.cos(halfFov);
            float sin = math.sin(-halfFov);
            leftDir = new float2(viewDir.x * cos - viewDir.y * sin, viewDir.x * sin + viewDir.y * cos);
            sin = math.sin(halfFov);
            rightDir = new float2(viewDir.x * cos - viewDir.y * sin, viewDir.x * sin + viewDir.y * cos);
        }

        float2 leftEnd = viewPos + leftDir * viewRange;
        float2 rightEnd = viewPos + rightDir * viewRange;

        float2x3 fovTriangle = new(
            viewPos,
            leftEnd,
            rightEnd
        );

        for (int i = 0; i < 4; i++)
        {
            if (SegmentIntersectsTriangle(square[i], square[(i + 1) % 4], fovTriangle)) return true;
            if (PointInTriangle(square[i], viewPos, leftEnd, rightEnd)) return true;
        }

        return false;
    }

    [BurstCompile]
    static bool SegmentIntersectsTriangle(in float2 p1, in float2 p2, in float2x3 tri)
    {
        for (int i = 0; i < 3; i++)
        {
            float2 q1 = tri[i];
            float2 q2 = tri[(i + 1) % 3];
            if (SegmentsIntersect(p1, p2, q1, q2))
            {
                return true;
            }
        }
        return false;
    }

    [BurstCompile]
    static bool CCW(in float2 a, in float2 b, in float2 c) => (c.y - a.y) * (b.x - a.x) > (b.y - a.y) * (c.x - a.x);

    [BurstCompile]
    static bool SegmentsIntersect(in float2 p1, in float2 p2, in float2 q1, in float2 q2) => (CCW(p1, q1, q2) != CCW(p2, q1, q2)) && (CCW(p1, p2, q1) != CCW(p1, p2, q2));

    [BurstCompile]
    static bool PointInTriangle(in float2 point, in float2 a, in float2 b, in float2 c)
    {
        float2 v0 = c - a;
        float2 v1 = b - a;

        float d00 = math.dot(v0, v0);
        float d01 = math.dot(v0, v1);
        float d11 = math.dot(v1, v1);

        float denom = d00 * d11 - d01 * d01;
        if (math.abs(denom) <= 0.0001f) return false;

        float2 v2 = point - a;
        float d02 = math.dot(v0, v2);
        float d12 = math.dot(v1, v2);

        float u = (d11 * d02 - d01 * d12) / denom;
        float v = (d00 * d12 - d01 * d02) / denom;

        return (u >= 0) && (v >= 0) && (u + v <= 1);
    }

    [BurstCompile]
    static bool PointInRectangle(in float2 point, in float2 a, in float2 b) =>
        point.x >= a.x && point.x <= b.x &&
        point.y >= a.y && point.y <= b.y;

    public static float BeamDeg(int beamHalvings) => 360f / (1 << beamHalvings);

    public static float PatternDb(float offAxisDeg, float beamDeg) => beamDeg >= 360f ? 0f : math.max(-12f * math.square(offAxisDeg / beamDeg), -25f);

    static float OffAxisDeg(float3 aim, float3 toTarget) => math.degrees(math.acos(math.clamp(math.dot(math.normalize(aim), toTarget), -1f, 1f)));

    static readonly float[] GainDb = { 0f, 3.0f, 8.3f, 14.2f, 20.2f, 26.2f, 32.2f, 38.2f };

    // TODO
    static float3 QuantizeDirection(float3 direction, int sectors)
    {
        float step = math.PI * 2f / sectors;
        float az = math.round(math.atan2(direction.x, direction.z) / step) * step;
        float el = math.round(math.asin(direction.y) / step) * step;
        return new float3(math.sin(az) * math.cos(el), math.sin(el), math.cos(az) * math.cos(el));
    }

    public static bool TryDeliver(
        in Radio transmitterHardware, float3 transmitterPosition,
        in SendRequest req,
        in Radio receiverHardware, float3 receiverPosition,
        in RadioEnvironment world, int obstructions, float jamRiseDb,
        out RxPacket packet)
    {
        packet = default;

        int k = math.clamp(req.BeamHalvings, transmitterHardware.MinBeamHalvings, math.min(transmitterHardware.MaxBeamHalvings, GainDb.Length - 1));

        float dist = math.distance(transmitterPosition, receiverPosition);
        float pathLoss = world.RefLossDb + 10f * world.PathLossExp * math.log10(dist) + obstructions * world.ObstructionDb;

        float3 toTarget = math.normalize(receiverPosition - transmitterPosition);

        float offAxisDeg = OffAxisDeg(req.Aim, toTarget);

        float rxDbm = transmitterHardware.TxPowerDbm + GainDb[k] + PatternDb(offAxisDeg, BeamDeg(k)) + receiverHardware.RxGainDbi - pathLoss;

        float margin = rxDbm - (receiverHardware.SensitivityDbm + jamRiseDb);
        if (margin < 0f) return false;

        packet.Direction = -toTarget;
        packet.MarginDb = (sbyte)math.min((int)(margin / 3f), 127);
        return true;
    }
}
