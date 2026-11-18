#if UNITY_EDITOR && EDITOR_DEBUG
#define DEBUG_LINES
#endif

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
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
[UpdateBefore(typeof(ProcessorSystemServer))]
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
        radioComponentQ = state.GetComponentLookup<Radio>(false);
        transformComponentQ = state.GetComponentLookup<LocalTransform>(true);
        logsBufferQ = state.GetBufferLookup<BufferedLogPiece>(false);
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        PausableTime t = SystemAPI.GetSingleton<PausableTime>();
        if (t.IsPaused) return;

        processorComponentQ.Update(ref state);
        radioComponentQ.Update(ref state);
        transformComponentQ.Update(ref state);
        logsBufferQ.Update(ref state);

        var radioEnvironment = SystemAPI.GetSingleton<RadioEnvironment>();

        foreach (var (srcProcessor, srcRadio, srcTransform, logBuffer, log, srcEntity) in
            SystemAPI.Query<RefRW<Processor>, RefRW<Radio>, RefRO<LocalTransform>, DynamicBuffer<BufferedLogPiece>, RefRW<LogPieces>>()
            .WithEntityAccess())
        {
            if (srcRadio.ValueRW.OutgoingTransmissions.Length == 0) continue;
            BufferedUnitTransmissionOutgoing transmission = srcRadio.ValueRW.OutgoingTransmissions[0];
            if (!transmission.Metadata.IsWireless) continue;

            srcRadio.ValueRW.OutgoingTransmissions.RemoveAt(0);

            new UnitLog_Transmission_WirelessOut(MonoTime.UnixSeconds, ++log.ValueRW.LastIndex, transmission.Metadata.Wireless, transmission.Data).Write(logBuffer);

            float3 srcPoint = srcTransform.ValueRO.TransformPoint(srcRadio.ValueRO.WirelessTransmissionPoint);

            NativeParallelHashMap<uint, NativeList<QuadrantEntity>>.ReadOnly map = QuadrantSystem.GetMap(state.WorldUnmanaged);
            Cell grid = Cell.ToGrid(srcPoint);

            srcProcessor.ValueRW.NetworkSendLED.Blink();

            SendRequest req = new()
            {
                Aim = transmission.Metadata.Wireless.Aim,
                BeamHalvings = transmission.Metadata.Wireless.BeamHalvings,
            };

            float maxRadius = MaxRange(srcRadio.ValueRO, req.BeamHalvings, radioEnvironment.BestRadioReceiver, radioEnvironment);

#if DEBUG_LINES
            float beamDeg = BeamDeg(ResolveBeamIndex(srcRadio.ValueRO, transmission.Metadata.Wireless.BeamHalvings));
            if (!transmission.Metadata.Wireless.Aim.Equals(float3.zero) && beamDeg < 180f)
            {
                float3 globalDirection = srcTransform.ValueRO.TransformDirection(transmission.Metadata.Wireless.Aim);
                //DebugEx.DrawLine(
                //    sourcePoint,
                //    globalDirection * maxRadius,
                //    Color.cyan,
                //    1f,
                //    true
                //);
                DebugEx.DrawCone(
                    srcPoint,
                    globalDirection,
                    maxRadius,
                    beamDeg,
                    Color.cyan,
                    1f,
                    true);
            }
            else
            {
                DebugEx.DrawPoint(
                    srcPoint,
                    maxRadius,
                    Color.cyan,
                    1f,
                    true);
                DebugEx.DrawSphere(
                    srcPoint,
                    maxRadius,
                    Color.cyan,
                    1f,
                    true);
            }
#endif

            float rangeMain = MaxRange(srcRadio.ValueRO, req.BeamHalvings, radioEnvironment.BestRadioReceiver, radioEnvironment, offAxisDeg: 0f);
            float rangeFloor = MaxRange(srcRadio.ValueRO, req.BeamHalvings, radioEnvironment.BestRadioReceiver, radioEnvironment, offAxisDeg: 180f);

            int diff = (int)((maxRadius / Cell.Size * 0.5f) + 2f);
            for (int x = -diff; x <= diff; x++)
            {
                for (int z = -diff; z <= diff; z++)
                {
                    Cell p = grid + new Cell(x, z);

                    if (!map.TryGetValue(p.key, out NativeList<QuadrantEntity> cell)) continue;
                    if (cell.Length == 0) continue;
                    if (!IsCellCovered(srcPoint, req, p, radioEnvironment.BestRadioReceiver, radioEnvironment, rangeMain, rangeFloor)) continue;

                    Cell.Draw(p, Color.gray, 0.3f);

                    using (var marker = _CellOperation.Auto())
                    {
                        for (int i = 0; i < cell.Length; i++)
                        {
                            if (cell[i].Entity == srcEntity) continue;
                            if (!processorComponentQ.TryGetRefRW(cell[i].Entity, out RefRW<Processor> dstProcessor)) continue;
                            if (!radioComponentQ.TryGetRefRW(cell[i].Entity, out RefRW<Radio> dstRadio)) continue;
                            RefRO<LocalTransform> dstTranform = transformComponentQ.GetRefRO(cell[i].Entity);

                            float3 dstPoint = dstTranform.ValueRO.TransformPoint(dstRadio.ValueRO.WirelessTransmissionPoint);

                            int obstructions = 0;

                            if (!TryDeliver(srcRadio.ValueRO, srcRadio.ValueRO.WirelessTransmissionPoint, req, dstRadio.ValueRO, srcTransform.ValueRO.InverseTransformPoint(dstPoint), radioEnvironment, obstructions, 0f, out var pkt))
                            {
                                DebugEx.DrawLine(srcPoint, dstPoint, Color.red, 0.3f);
                                DebugEx.DrawSphere(dstPoint, 1f, Color.red, 0.3f);
                                continue;
                            }

                            float jamMw = 0f;

                            foreach (var (jammer, jammerTransform) in
                                SystemAPI.Query<RefRO<RadioJammer>, RefRO<LocalTransform>>())
                            {
                                if (!jammer.ValueRO.IsActive) continue;

                                float dist = math.distance(dstPoint, jammerTransform.ValueRO.Position);
                                float pathLoss = radioEnvironment.RefLossDb
                                               + (10f * radioEnvironment.PathLossExp * math.log10(dist))
                                               + (obstructions * radioEnvironment.ObstructionDb);
                                jamMw += math.pow(10f, (jammer.ValueRO.EirpDbm - pathLoss + dstRadio.ValueRO.RxGainDbi) / 10f);

                                DebugEx.DrawLine(srcPoint, jammerTransform.ValueRO.Position, Color.magenta, 0.3f);
                                DebugEx.DrawSphere(jammerTransform.ValueRO.Position, 1f, Color.magenta, 0.3f);
                            }

                            if (jamMw != 0f && !TryDeliver(srcRadio.ValueRO, srcRadio.ValueRO.WirelessTransmissionPoint, req, dstRadio.ValueRO, srcTransform.ValueRO.InverseTransformPoint(dstPoint), radioEnvironment, obstructions, 10f * math.log10(1f + (jamMw / math.pow(10f, (radioEnvironment.SensitivityDbm(dstRadio.ValueRO) - radioEnvironment.RequiredSnrDb) / 10f))), out pkt))
                            {
                                dstProcessor.ValueRW.NetworkReceiveLED.Blink(2);
                                DebugEx.DrawLine(srcPoint, dstPoint, Color.orange, 0.3f);
                                DebugEx.DrawSphere(dstPoint, 1f, Color.orange, 0.3f);
                                continue;
                            }

                            DebugEx.DrawLine(srcPoint, dstPoint, Color.green, 0.3f);
                            DebugEx.DrawSphere(dstPoint, 1f, Color.green, 0.3f);

                            dstProcessor.ValueRW.NetworkReceiveLED.Blink(1);

                            ref FixedList128Bytes<BufferedUnitTransmission> transmissions = ref dstRadio.ValueRW.IncomingTransmissions;

                            IncomingWirelessUnitTransmissionMetadata otherIncomingMetadata = new()
                            {
                                Direction = pkt.Direction,
                                MarginDb = pkt.MarginDb,
                            };

                            if (logsBufferQ.TryGetBuffer(cell[i].Entity, out DynamicBuffer<BufferedLogPiece> otherLog))
                            {
                                new UnitLog_Transmission_WirelessIn(MonoTime.UnixSeconds, ++log.ValueRW.LastIndex, otherIncomingMetadata, transmission.Data, SystemAPI.GetComponent<GhostInstance>(srcEntity)).Write(logBuffer);
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

                            //foreach (var player in SystemAPI.Query<RefRO<RealPlayer>>())
                            //{
                            //    if (math.distance(player.ValueRO.Position, sourcePoint) < 50f ||
                            //        math.distance(player.ValueRO.Position, receiverPoint) < 50f)
                            //    {
                            //        NetcodeUtils.CreateRPC(state.WorldUnmanaged, new WirelessTransmissionEventRpc()
                            //        {
                            //            Origin = sourcePoint,
                            //            Destination = receiverPoint,
                            //            Data = transmission.Data,
                            //        }, player.ValueRO.Connection);
                            //    }
                            //}
                        }
                    }
                }
            }
        }
    }

    public static bool IsCellCovered(
        float3 transmitterPosition, in SendRequest req,
        Cell cell,
        in Radio tx, in RadioEnvironment world,
        float rangeMain, float rangeFloor)
    {
        float3 cellCenter = Cell.ToWorld(cell) + new float3(Cell.Size / 2, 0f, Cell.Size / 2);
        float cellRadius = math.sqrt(Cell.Size * Cell.Size) / 2f;

        float3 d = cellCenter - transmitterPosition;
        float D = math.length(d);
        if (D <= cellRadius) return true;

        float dMin = D - cellRadius;
        if (dMin > rangeMain) return false;
        if (dMin <= rangeFloor) return true;

        float alpha = math.degrees(math.acos(math.clamp(math.dot(math.normalizesafe(req.Aim * new float3(1f, 0f, 1f), req.Aim), d / D), -1f, 1f)));
        float spread = math.degrees(math.asin(cellRadius / D));
        float thetaMin = math.max(0f, alpha - spread);

        float r = MaxRange(tx, req.BeamHalvings, world.BestRadioReceiver, world, thetaMin);
        return dMin <= r * 1.001f + 0.01f;
    }

    [BurstCompile]
    static float PointSquareDistance(in float2 point, in float2 a, in float2 b)
    {
        float dx = math.max(math.max(a.x - point.x, point.x - b.x), 0f);
        float dy = math.max(math.max(a.y - point.y, point.y - b.y), 0f);
        return math.sqrt((dx * dx) + (dy * dy));
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
            leftDir = new float2((viewDir.x * cos) - (viewDir.y * sin), (viewDir.x * sin) + (viewDir.y * cos));
            sin = math.sin(halfFov);
            rightDir = new float2((viewDir.x * cos) - (viewDir.y * sin), (viewDir.x * sin) + (viewDir.y * cos));
        }

        float2 leftEnd = viewPos + (leftDir * viewRange);
        float2 rightEnd = viewPos + (rightDir * viewRange);

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

        float denom = (d00 * d11) - (d01 * d01);
        if (math.abs(denom) <= 0.0001f) return false;

        float2 v2 = point - a;
        float d02 = math.dot(v0, v2);
        float d12 = math.dot(v1, v2);

        float u = ((d11 * d02) - (d01 * d12)) / denom;
        float v = ((d00 * d12) - (d01 * d02)) / denom;

        return (u >= 0) && (v >= 0) && (u + v <= 1);
    }

    [BurstCompile]
    static bool PointInRectangle(in float2 point, in float2 a, in float2 b) =>
        point.x >= a.x && point.x <= b.x &&
        point.y >= a.y && point.y <= b.y;

    public static float BeamDeg(int beamHalvings) => 360f / (1 << beamHalvings);

    public static float PatternDb(float offAxisDeg, float beamDeg) => beamDeg >= 360f ? 0f : math.max(-12f * math.square(offAxisDeg / beamDeg), -60f);

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

    static int ResolveBeamIndex(in Radio transmitter, int reqBeamHalvings)
        => math.clamp(reqBeamHalvings, transmitter.MinBeamHalvings, math.min(transmitter.MaxBeamHalvings, GainDb.Length - 1));

    static float PreLossDbm(in Radio transmitter, in float3 reqAim, in Radio receiver, int k, float3 toTarget)
        => PreLossDbm(transmitter, receiver, k, OffAxisDeg(reqAim, toTarget));

    static float PreLossDbm(in Radio transmitter, in Radio receiver, int k, float offAxisDeg)
        => transmitter.TxPowerDbm + GainDb[k] + PatternDb(offAxisDeg, BeamDeg(k)) + receiver.RxGainDbi;

    static float DistanceForDistanceLossDb(float distanceLossBudgetDb, in RadioEnvironment world)
    {
        // distanceLossBudgetDb = allowed loss minus RefLossDb minus obstruction loss
        if (world.PathLossExp <= 0f)  // degenerate: loss independent of distance
            return distanceLossBudgetDb >= 0f ? float.PositiveInfinity : 0f;

        return math.pow(10f, distanceLossBudgetDb / (10f * world.PathLossExp));
    }

    public static float MaxRange(
        in Radio transmitter, float3 txPos, in SendRequest req,
        in Radio receiver, float3 rxPos, in RadioEnvironment world)
        => MaxRange(transmitter, txPos, req, receiver, rxPos, world, obstructions: 0, jamRiseDb: 0f);

    public static float MaxRange(
        in Radio transmitter, int reqBeamHalvings,
        in Radio receiver, in RadioEnvironment world,
        float offAxisDeg,
        int obstructions = 0, float jamRiseDb = 0f)
    {
        int k = ResolveBeamIndex(transmitter, reqBeamHalvings);

        float allowedLossDb = PreLossDbm(transmitter, receiver, k, offAxisDeg) - (world.SensitivityDbm(receiver) + jamRiseDb);

        float distanceLossBudgetDb = allowedLossDb - world.RefLossDb - (obstructions * world.ObstructionDb);
        return DistanceForDistanceLossDb(distanceLossBudgetDb, world);
    }

    public static float MaxRange(
        in Radio transmitter, float3 transmitterPosition, in SendRequest req,
        in Radio receiver, float3 receiverPosition, in RadioEnvironment world,
        int obstructions = 0, float jamRiseDb = 0f)
    {
        int k = ResolveBeamIndex(transmitter, req.BeamHalvings);
        float3 toTarget = math.normalizesafe(receiverPosition - transmitterPosition, req.Aim);

        float allowedLossDb = PreLossDbm(transmitter, req.Aim, receiver, k, toTarget) - (world.SensitivityDbm(receiver) + jamRiseDb);

        float distanceLossBudgetDb = allowedLossDb - world.RefLossDb - (obstructions * world.ObstructionDb);
        return DistanceForDistanceLossDb(distanceLossBudgetDb, world);
    }

    public static float MaxRange(
        in Radio transmitter, int reqBeamHalvings,
        in Radio receiver, in RadioEnvironment world,
        int obstructions = 0, float jamRiseDb = 0f)
    {
        int k = ResolveBeamIndex(transmitter, reqBeamHalvings);

        float allowedLossDb = PreLossDbm(transmitter, receiver, k, 0f) - (world.SensitivityDbm(receiver) + jamRiseDb);

        float distanceLossBudgetDb = allowedLossDb - world.RefLossDb - (obstructions * world.ObstructionDb);
        return DistanceForDistanceLossDb(distanceLossBudgetDb, world);
    }

    public static bool TryDeliver(
        in Radio transmitter, float3 transmitterPosition,
        in SendRequest req,
        in Radio receiver, float3 receiverPosition,
        in RadioEnvironment world, int obstructions, float jamRiseDb,
        out RxPacket packet)
    {
        packet = default;

        int k = ResolveBeamIndex(transmitter, req.BeamHalvings);

        float dist = math.distance(transmitterPosition, receiverPosition);
        float pathLoss = world.RefLossDb + (10f * world.PathLossExp * math.log10(dist)) + (obstructions * world.ObstructionDb);

        float3 toTarget = math.normalize(receiverPosition - transmitterPosition);

        float rxDbm = PreLossDbm(transmitter, req.Aim, receiver, k, toTarget) - pathLoss;

        float margin = rxDbm - (world.SensitivityDbm(receiver) + jamRiseDb);
        if (margin < 0f) return false;

        packet.Direction = -toTarget;
        packet.MarginDb = (sbyte)math.min((int)(margin / 3f), 127);
        return true;
    }
}
