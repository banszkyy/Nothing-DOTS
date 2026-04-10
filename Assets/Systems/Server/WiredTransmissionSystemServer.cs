#if UNITY_EDITOR && EDITOR_DEBUG
#define DEBUG_LINES
#endif

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
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
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct WiredTransmissionSystemServer : ISystem
{
    ComponentLookup<Processor> processorComponentQ;

    [BurstCompile]
    void ISystem.OnCreate(ref SystemState state)
    {
        processorComponentQ = state.GetComponentLookup<Processor>(false);
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        processorComponentQ.Update(ref state);

        NativeQueue<EntityPortIdentifier> openSet = new(Allocator.Temp);
        NativeHashSet<EntityPortIdentifier> closedSet = new(16, Allocator.Temp);

        foreach (var (processor, transform, originEntity) in
            SystemAPI.Query<RefRW<Processor>, RefRO<LocalTransform>>()
            .WithEntityAccess())
        {
            if (processor.ValueRW.OutgoingTransmissions.Length == 0) continue;
            BufferedUnitTransmissionOutgoing transmission = processor.ValueRW.OutgoingTransmissions[0];
            if (transmission.Metadata.IsWireless) continue;
            OutgoingWiredUnitTransmissionMetadata metadata = transmission.Metadata.Wired;

            processor.ValueRW.OutgoingTransmissions.RemoveAt(0);

            if (!SystemAPI.TryGetComponent(originEntity, out Connector originConnector))
            {
                //Debug.LogWarning($"{DebugEx.ServerPrefix} Processor tried to send data on port {metadata.Port} but there are no ports");
                continue;
            }

            if (metadata.Port < 0 || metadata.Port >= originConnector.PortPositions.Length)
            {
                Debug.LogWarning($"{DebugEx.ServerPrefix} Processor tried to send data on port {metadata.Port} but theres only {originConnector.PortPositions.Length} ports avaliable");
                continue;
            }

            EntityPortIdentifier originIdentifier = new(originEntity, metadata.Port);

            openSet.Clear();
            closedSet.Clear();

            closedSet.Add(originIdentifier);
            openSet.Enqueue(originIdentifier);

            while (openSet.TryDequeue(out EntityPortIdentifier next))
            {
                DynamicBuffer<BufferedWire> buffer = SystemAPI.GetBuffer<BufferedWire>(next.Entity);
                for (int i = 0; i < buffer.Length; i++)
                {
                    BufferedWire wire = buffer[i];
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
                if (!processorComponentQ.HasComponent(connector.Entity)) continue;
                RefRW<Processor> other = processorComponentQ.GetRefRW(connector.Entity);
                if (!other.ValueRO.Source.Code.IsCreated || other.ValueRO.Signal != LanguageCore.Runtime.Signal.None) continue;

                other.ValueRW.NetworkReceiveLED.Blink();

#if DEBUG_LINES
                float3 p = SystemAPI.GetComponentRO<LocalTransform>(connector.Entity).ValueRO.TransformPoint(SystemAPI.GetComponentRO<Connector>(connector.Entity).ValueRO.PortPositions[connector.Port]);
                DebugEx.DrawSphere(p, 0.5f, Color.white, 0.1f, false);
#endif

                ref FixedList128Bytes<BufferedUnitTransmission> transmissions = ref other.ValueRW.IncomingTransmissions;

                if (transmissions.Length >= transmissions.Capacity) transmissions.RemoveAt(0);
                transmissions.Add(new()
                {
                    Data = transmission.Data,
                    Metadata = new IncomingUnitTransmissionMetadata()
                    {
                        IsWireless = false,
                        Wired = new()
                        {
                            Port = connector.Port,
                        },
                    },
                });
            }
        }
    }
}
