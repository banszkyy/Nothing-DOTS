#if UNITY_EDITOR && EDITOR_DEBUG
#define DEBUG_LINES
#endif

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

public readonly struct EntityPortIdentifier : IEquatable<EntityPortIdentifier>
{
    public readonly Entity Entity;
    public readonly byte Port;

    public EntityPortIdentifier(Entity entity, byte port)
    {
        Entity = entity;
        Port = port;
    }

    public override bool Equals(object? obj) => obj is EntityPortIdentifier other && Equals(other);
    public bool Equals(EntityPortIdentifier other) => Entity.Equals(other.Entity) && Port == other.Port;
    public override int GetHashCode() => Entity.GetHashCode();

    public static bool operator ==(EntityPortIdentifier left, EntityPortIdentifier right) => left.Equals(right);
    public static bool operator !=(EntityPortIdentifier left, EntityPortIdentifier right) => !left.Equals(right);
}

[BurstCompile]
[UpdateBefore(typeof(ProcessorSystemServer))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct WiredTransmissionSystemServer : ISystem
{
    ComponentLookup<Processor> processorQ;
    ComponentLookup<Radio> radioQ;
    BufferLookup<BufferedLogPiece> logsBufferQ;

    [BurstCompile]
    void ISystem.OnCreate(ref SystemState state)
    {
        processorQ = state.GetComponentLookup<Processor>(false);
        radioQ = state.GetComponentLookup<Radio>(false);
        logsBufferQ = state.GetBufferLookup<BufferedLogPiece>(false);
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        processorQ.Update(ref state);
        radioQ.Update(ref state);
        logsBufferQ.Update(ref state);

        NativeQueue<EntityPortIdentifier> openSet = new(Allocator.Temp);
        NativeHashSet<EntityPortIdentifier> closedSet = new(16, Allocator.Temp);

        foreach (var (processor, radio, transform, logBuffer, log, originEntity) in
            SystemAPI.Query<RefRW<Processor>, RefRW<Radio>, RefRO<LocalTransform>, DynamicBuffer<BufferedLogPiece>, RefRW<LogPieces>>()
            .WithEntityAccess())
        {
            if (radio.ValueRW.OutgoingTransmissions.Length == 0) continue;
            BufferedUnitTransmissionOutgoing transmission = radio.ValueRW.OutgoingTransmissions[0];
            if (transmission.Metadata.IsWireless) continue;
            OutgoingWiredUnitTransmissionMetadata metadata = transmission.Metadata.Wired;

            //new UnitLog_Transmission_WiredOut(MonoTime.UnixSeconds, metadata, transmission.Data).Write(log);

            radio.ValueRW.OutgoingTransmissions.RemoveAt(0);

            if (!SystemAPI.TryGetComponent(originEntity, out Connector originConnector))
            {
                //Debug.LogWarning($"{DebugEx.ServerPrefix} Processor tried to send data on port {metadata.Port} but there are no ports");
                continue;
            }

            if (metadata.Port < 0 || metadata.Port >= originConnector.PortPositions.Length)
            {
                Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Processor tried to send data on port {{0}} but theres only {{1}} ports avaliable", metadata.Port, originConnector.PortPositions.Length));
                continue;
            }

            EntityPortIdentifier originIdentifier = new(originEntity, metadata.Port);

            openSet.Clear();
            closedSet.Clear();

            closedSet.Add(originIdentifier);
            openSet.Enqueue(originIdentifier);

            while (openSet.TryDequeue(out EntityPortIdentifier next))
            {
                if (SystemAPI.HasComponent<WirePlaceholder>(next.Entity)) continue;
                DynamicBuffer<BufferedWire> buffer = SystemAPI.GetBuffer<BufferedWire>(next.Entity);
                for (int i = 0; i < buffer.Length; i++)
                {
                    BufferedWire wire = buffer[i];

                    if (wire.EntityA == Entity.Null || wire.EntityB == Entity.Null)
                    {
                        Debug.LogWarning($"Invalid wire");
                        DebugEx.DrawSphere(SystemAPI.GetComponentRO<LocalTransform>(next.Entity).ValueRO.Position, 0.52f, Color.yellow, 1f, false);
                        continue;
                    }

                    if (wire.PortIdentifierA == next)
                    {
                        if (closedSet.Add(wire.PortIdentifierB))
                        {
#if DEBUG_LINES
                            float3 a = SystemAPI.GetComponentRO<LocalTransform>(next.Entity).ValueRO.TransformPoint(SystemAPI.GetComponentRO<Connector>(next.Entity).ValueRO.PortPositions[next.Port]);
                            float3 b = SystemAPI.GetComponentRO<LocalTransform>(wire.EntityB).ValueRO.TransformPoint(SystemAPI.GetComponentRO<Connector>(wire.EntityB).ValueRO.PortPositions[wire.PortB]);
                            WireRendererSystemClient.DrawWire(a, b, Color.white, 0.1f, false);
#endif
                            openSet.Enqueue(wire.PortIdentifierB);
                        }
                    }
                    else
                    {
                        if (closedSet.Add(wire.PortIdentifierA))
                        {
#if DEBUG_LINES
                            float3 a = SystemAPI.GetComponentRO<LocalTransform>(next.Entity).ValueRO.TransformPoint(SystemAPI.GetComponentRO<Connector>(next.Entity).ValueRO.PortPositions[next.Port]);
                            float3 b = SystemAPI.GetComponentRO<LocalTransform>(wire.EntityA).ValueRO.TransformPoint(SystemAPI.GetComponentRO<Connector>(wire.EntityA).ValueRO.PortPositions[wire.PortA]);
                            WireRendererSystemClient.DrawWire(a, b, Color.white, 0.1f, false);
#endif
                            openSet.Enqueue(wire.PortIdentifierA);
                        }
                    }
                }
            }

            processor.ValueRW.NetworkSendLED.Blink();

            closedSet.Remove(originIdentifier);

            foreach (EntityPortIdentifier connector in closedSet)
            {
                if (!processorQ.HasComponent(connector.Entity)) continue;
                RefRW<Processor> other = processorQ.GetRefRW(connector.Entity);
                if (!other.ValueRO.Source.Code.IsCreated || other.ValueRO.Signal != LanguageCore.Runtime.Signal.None) continue;
                RefRW<Radio> otherRadio = radioQ.GetRefRW(connector.Entity);

                other.ValueRW.NetworkReceiveLED.Blink(1);

#if DEBUG_LINES
                float3 p = SystemAPI.GetComponentRO<LocalTransform>(connector.Entity).ValueRO.TransformPoint(SystemAPI.GetComponentRO<Connector>(connector.Entity).ValueRO.PortPositions[connector.Port]);
                DebugEx.DrawSphere(p, 0.5f, Color.white, 0.1f, false);
#endif

                ref FixedList128Bytes<BufferedUnitTransmission> transmissions = ref otherRadio.ValueRW.IncomingTransmissions;

                IncomingWiredUnitTransmissionMetadata otherIncomingMetadata = new()
                {
                    Port = connector.Port,
                };

                if (logsBufferQ.TryGetBuffer(connector.Entity, out DynamicBuffer<BufferedLogPiece> otherLog))
                {
                    new UnitLog_Transmission_WiredIn(MonoTime.UnixSeconds, ++log.ValueRW.LastIndex, otherIncomingMetadata, transmission.Data, SystemAPI.GetComponent<GhostInstance>(originEntity)).Write(logBuffer);
                }

                if (transmissions.Length >= transmissions.Capacity) transmissions.RemoveAt(0);
                transmissions.Add(new()
                {
                    Data = transmission.Data,
                    Metadata = new IncomingUnitTransmissionMetadata()
                    {
                        IsWireless = false,
                        Wired = otherIncomingMetadata,
                    },
                });
            }
        }
    }
}
