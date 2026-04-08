using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct GoInServerClientSystemServer : ISystem
{
    [BurstCompile]
    void ISystem.OnCreate(ref SystemState state)
    {
        EntityQueryBuilder builder = new(Allocator.Temp);
        builder.WithAll<ReceiveRpcCommandRequest, GoInGameRpc>();
        state.RequireForUpdate(state.GetEntityQuery(builder));
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;

        foreach (var (request, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
            .WithAll<GoInGameRpc>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);
            commandBuffer.AddComponent<NetworkStreamInGame>(request.ValueRO.SourceConnection);
        }
    }
}
