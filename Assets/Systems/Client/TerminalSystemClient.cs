using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

class TerminalSubscriptionClient : IDisposable
{
    public const int MaxLength = 1024;
    public readonly SpawnedGhost Entity;
    public readonly NativeList<byte> Data;
    public ulong Offset;
    public int References;

    public TerminalSubscriptionClient(SpawnedGhost entity, Allocator allocator)
    {
        Entity = entity;
        Data = new(allocator);
        Offset = 0;
        References = 0;
    }

    public void Dispose()
    {
        Data.Dispose();
    }
}

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial class TerminalSystemClient : SystemBase
{
    readonly Dictionary<SpawnedGhost, TerminalSubscriptionClient> Subscriptions = new();

    public TerminalSubscriptionClient Subscribe(SpawnedGhost entity, in EntityCommandBuffer commandBuffer)
    {
        if (!Subscriptions.TryGetValue(entity, out TerminalSubscriptionClient subscription))
        {
            subscription = new(entity, Allocator.Persistent);
            Subscriptions.Add(entity, subscription);
        }
        Debug.Log($"{DebugEx.ClientPrefix} Subscribing to terminal of entity {entity}");
        NetcodeUtils.CreateRPC(in commandBuffer, World.Unmanaged, new SubscribeTerminalRpc()
        {
            Entity = entity,
            Offset = 0,
        });
        subscription.References++;
        return subscription;
    }

    public TerminalSubscriptionClient Subscribe(SpawnedGhost entity)
    {
        if (!Subscriptions.TryGetValue(entity, out TerminalSubscriptionClient subscription))
        {
            subscription = new(entity, Allocator.Persistent);
            Subscriptions.Add(entity, subscription);
        }
        Debug.Log($"{DebugEx.ClientPrefix} Subscribing to terminal of entity {entity}");
        NetcodeUtils.CreateRPC(World.Unmanaged, new SubscribeTerminalRpc()
        {
            Entity = entity,
            Offset = 0,
        });
        subscription.References++;
        return subscription;
    }

    public void Unsubscribe(SpawnedGhost entity, in EntityCommandBuffer commandBuffer)
    {
        if (Subscriptions.TryGetValue(entity, out TerminalSubscriptionClient subscription))
        {
            if (--subscription.References > 0) return;
        }
        Debug.Log($"{DebugEx.ClientPrefix} Unsubscribing from terminal of entity {entity}");
        NetcodeUtils.CreateRPC(in commandBuffer, World.Unmanaged, new UnsubscribeTerminalRpc()
        {
            Entity = entity,
        });
    }

    public void Unsubscribe(SpawnedGhost entity)
    {
        if (Subscriptions.TryGetValue(entity, out TerminalSubscriptionClient subscription))
        {
            if (--subscription.References > 0) return;
        }
        Debug.Log($"{DebugEx.ClientPrefix} Unsubscribing from terminal of entity {entity}");
        NetcodeUtils.CreateRPC(World.Unmanaged, new UnsubscribeTerminalRpc()
        {
            Entity = entity,
        });
    }

    protected override unsafe void OnUpdate()
    {
        EntityCommandBuffer commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

        foreach (var (command, entity) in
            SystemAPI.Query<RefRO<TerminalDataRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            commandBuffer.DestroyEntity(entity);

            if (Subscriptions.TryGetValue(command.ValueRO.Entity, out TerminalSubscriptionClient subscription))
            {
                if (subscription.Offset > command.ValueRO.Offset)
                {
                    subscription.Data.Clear();
                }
                int dispose = subscription.Data.Length + command.ValueRO.Data.Length - TerminalSubscriptionClient.MaxLength;
                if (dispose > 0) subscription.Data.RemoveRange(0, dispose);
                subscription.Data.AddRange(command.ValueRO.Data.GetUnsafePtr(), command.ValueRO.Data.Length);
                subscription.Offset = command.ValueRO.Offset + (ulong)command.ValueRO.Data.Length;
            }
        }
    }
}
