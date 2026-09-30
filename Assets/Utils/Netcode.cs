using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
public static class NetcodeUtils
{
    [BurstCompile]
    public static bool IsLocal(this in WorldUnmanaged world) => !world.IsServer() && !world.IsClient();

    public static bool IsLocal(this World world) => !world.IsServer() && !world.IsClient();

    [BurstCompile]
    public static void CreateRPC<T>(in WorldUnmanaged world)
        where T : unmanaged, IComponentData
    {
        CreateRPCImpl(in world, ComponentType.ReadOnly<T>(), Entity.Null, out _);
    }

    [BurstCompile]
    public static void CreateRPC<T>(in WorldUnmanaged world, in Entity connectionEntity)
        where T : unmanaged, IComponentData
    {
        CreateRPCImpl(in world, ComponentType.ReadOnly<T>(), in connectionEntity, out _);
    }

    [BurstCompile]
    public static void CreateRPC<T>(in WorldUnmanaged world, in T componentData)
        where T : unmanaged, IComponentData
    {
        CreateRPCImpl(in world, ComponentType.ReadWrite<T>(), Entity.Null, out Entity entity);
        world.EntityManager.SetComponentData(entity, componentData);
    }

    [BurstCompile]
    public static void CreateRPC<T>(in WorldUnmanaged world, in T componentData, in Entity connectionEntity)
        where T : unmanaged, IComponentData
    {
        CreateRPCImpl(in world, ComponentType.ReadWrite<T>(), in connectionEntity, out Entity entity);
        world.EntityManager.SetComponentData(entity, componentData);
    }

    [BurstCompile]
    public static void CreateFakeRPC<T>(in WorldUnmanaged world, in T componentData, int playerIndex)
        where T : unmanaged, IComponentData
    {
        CreateFakeRPCImpl(in world, ComponentType.ReadWrite<T>(), playerIndex, out Entity entity);
        world.EntityManager.SetComponentData(entity, componentData);
    }

    [BurstCompile]
    static void CreateRPCImpl(in WorldUnmanaged world, in ComponentType componentType, in Entity connectionEntity, out Entity result)
    {
        if (world.IsClient() || world.IsServer())
        {
            result = world.EntityManager.CreateEntity(stackalloc ComponentType[]
            {
                componentType,
                ComponentType.ReadWrite<SendRpcCommandRequest>(),
            });

            world.EntityManager.SetComponentData(result, new SendRpcCommandRequest() { TargetConnection = connectionEntity });
        }
        else
        {
            result = world.EntityManager.CreateEntity(stackalloc ComponentType[]
            {
                componentType,
                ComponentType.ReadWrite<ReceiveRpcCommandRequest>(),
            });

            world.EntityManager.SetComponentData(result, new ReceiveRpcCommandRequest() { SourceConnection = connectionEntity });
        }
    }

    [BurstCompile]
    static void CreateFakeRPCImpl(in WorldUnmanaged world, in ComponentType componentType, int playerIndex, out Entity result)
    {
        result = world.EntityManager.CreateEntity(stackalloc ComponentType[]
        {
            componentType,
            ComponentType.ReadWrite<ReceiveRpcCommandRequest>(),
            ComponentType.ReadWrite<VirtualRpc>(),
        });

        world.EntityManager.SetComponentData(result, new ReceiveRpcCommandRequest());
        world.EntityManager.SetComponentData(result, new VirtualRpc() { PlayerIndex = playerIndex });
    }

    [BurstCompile]
    public static void CreateRPC<T>(in EntityCommandBuffer commandBuffer, in WorldUnmanaged world)
        where T : unmanaged, IComponentData
    {
        CreateRPCImpl(in commandBuffer, in world, ComponentType.ReadOnly<T>(), Entity.Null, out _);
    }

    [BurstCompile]
    public static void CreateRPC<T>(in EntityCommandBuffer commandBuffer, in WorldUnmanaged world, in Entity connectionEntity)
        where T : unmanaged, IComponentData
    {
        CreateRPCImpl(in commandBuffer, in world, ComponentType.ReadOnly<T>(), in connectionEntity, out _);
    }

    [BurstCompile]
    public static void CreateRPC<T>(in EntityCommandBuffer commandBuffer, in WorldUnmanaged world, in T componentData)
        where T : unmanaged, IComponentData
    {
        CreateRPCImpl(in commandBuffer, in world, ComponentType.ReadWrite<T>(), Entity.Null, out Entity entity);
        commandBuffer.SetComponent(entity, componentData);
    }

    [BurstCompile]
    public static void CreateRPC<T>(in EntityCommandBuffer commandBuffer, in WorldUnmanaged world, in T componentData, in Entity connectionEntity)
        where T : unmanaged, IComponentData
    {
        CreateRPCImpl(in commandBuffer, in world, ComponentType.ReadWrite<T>(), in connectionEntity, out Entity entity);
        commandBuffer.SetComponent(entity, componentData);
    }

    [BurstCompile]
    static void CreateRPCImpl(in EntityCommandBuffer commandBuffer, in WorldUnmanaged world, in ComponentType componentType, in Entity connectionEntity, out Entity result)
    {
        if (world.Time.ElapsedTime < 1d)
        {
            if (world.IsClient() || world.IsServer())
            {
                result = commandBuffer.CreateEntity();
                commandBuffer.AddComponent(result, componentType);
                commandBuffer.AddComponent<SendRpcCommandRequest>(result, new() { TargetConnection = connectionEntity });
            }
            else
            {
                result = commandBuffer.CreateEntity();
                commandBuffer.AddComponent(result, componentType);
                commandBuffer.AddComponent<SendRpcCommandRequest>(result, new() { TargetConnection = connectionEntity });
                commandBuffer.AddComponent<ReceiveRpcCommandRequest>(result, new() { SourceConnection = connectionEntity });
            }
        }
        else if (world.IsClient() || world.IsServer())
        {
            result = commandBuffer.CreateEntity(world.EntityManager.CreateArchetype(stackalloc ComponentType[]
            {
                componentType,
                ComponentType.ReadWrite<SendRpcCommandRequest>(),
            }));

            commandBuffer.SetComponent(result, new SendRpcCommandRequest() { TargetConnection = connectionEntity });
        }
        else
        {
            result = commandBuffer.CreateEntity(world.EntityManager.CreateArchetype(stackalloc ComponentType[]
            {
                componentType,
                ComponentType.ReadOnly<SendRpcCommandRequest>(),
                ComponentType.ReadWrite<ReceiveRpcCommandRequest>(),
            }));

            commandBuffer.SetComponent(result, new ReceiveRpcCommandRequest() { SourceConnection = connectionEntity });
        }
    }
}
