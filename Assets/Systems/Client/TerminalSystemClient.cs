using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

class TerminalSubscriptionClient : IDisposable
{
    public const int MaxLength = 1024;
    public readonly SpawnedGhost Ghost;
    public readonly Entity Entity;
    public readonly NativeList<byte> Data;
    public ulong Offset;
    public ulong Version;
    public int References;

    public TerminalSubscriptionClient(SpawnedGhost ghost, Entity entity, Allocator allocator)
    {
        Ghost = ghost;
        Entity = entity;
        Data = new(allocator);
        Offset = 0;
        References = 0;
        Version = 0;
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

    public TerminalSubscriptionClient Subscribe(Entity entity, in EntityCommandBuffer commandBuffer)
    {
        var ghost = SystemAPI.GetComponentRO<GhostInstance>(entity).ValueRO;
        if (!Subscriptions.TryGetValue(ghost, out TerminalSubscriptionClient subscription))
        {
            subscription = new(ghost, entity, Allocator.Persistent);
            Subscriptions.Add(ghost, subscription);
        }
        Debug.Log($"{DebugEx.ClientPrefix} Subscribing to terminal of entity {ghost}");
        NetcodeUtils.CreateRPC(in commandBuffer, World.Unmanaged, new SubscribeTerminalRpc()
        {
            Entity = ghost,
            Offset = 0,
        });
        subscription.References++;
        return subscription;
    }

    public TerminalSubscriptionClient Subscribe(Entity entity)
    {
        var ghost = SystemAPI.GetComponentRO<GhostInstance>(entity).ValueRO;
        if (!Subscriptions.TryGetValue(ghost, out TerminalSubscriptionClient subscription))
        {
            subscription = new(ghost, entity, Allocator.Persistent);
            Subscriptions.Add(ghost, subscription);
        }
        Debug.Log($"{DebugEx.ClientPrefix} Subscribing to terminal of entity {ghost}");
        NetcodeUtils.CreateRPC(World.Unmanaged, new SubscribeTerminalRpc()
        {
            Entity = ghost,
            Offset = 0,
        });
        subscription.References++;
        return subscription;
    }

    public void Unsubscribe(SpawnedGhost ghost, in EntityCommandBuffer commandBuffer)
    {
        if (Subscriptions.TryGetValue(ghost, out TerminalSubscriptionClient subscription))
        {
            if (--subscription.References > 0) return;
            Subscriptions.Remove(ghost);
        }
        Debug.Log($"{DebugEx.ClientPrefix} Unsubscribing from terminal of entity {ghost}");
        NetcodeUtils.CreateRPC(in commandBuffer, World.Unmanaged, new UnsubscribeTerminalRpc()
        {
            Entity = ghost,
        });
    }

    public void Unsubscribe(SpawnedGhost ghost)
    {
        if (Subscriptions.TryGetValue(ghost, out TerminalSubscriptionClient subscription))
        {
            if (--subscription.References > 0) return;
            Subscriptions.Remove(ghost);
        }
        Debug.Log($"{DebugEx.ClientPrefix} Unsubscribing from terminal of entity {ghost}");
        NetcodeUtils.CreateRPC(World.Unmanaged, new UnsubscribeTerminalRpc()
        {
            Entity = ghost,
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

            if (!Subscriptions.TryGetValue(command.ValueRO.Entity, out TerminalSubscriptionClient subscription)) continue;

            if (subscription.Offset > command.ValueRO.Offset)
            {
                subscription.Data.Clear();
            }
            int dispose = subscription.Data.Length + command.ValueRO.Data.Length - TerminalSubscriptionClient.MaxLength;
            if (dispose > 0) subscription.Data.RemoveRange(0, dispose);
            subscription.Data.AddRange(command.ValueRO.Data.GetUnsafePtr(), command.ValueRO.Data.Length);
            subscription.Offset = command.ValueRO.Offset + (ulong)command.ValueRO.Data.Length;
            subscription.Version++;
        }

        foreach (var item in Subscriptions)
        {
            if (SystemAPI.Exists(item.Value.Entity)) continue;
            Unsubscribe(item.Key, commandBuffer);
            break;
        }
    }
}
