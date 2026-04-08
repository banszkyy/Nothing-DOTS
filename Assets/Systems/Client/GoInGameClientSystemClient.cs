using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
public partial struct GoInGameClientSystemClient : ISystem
{
    [BurstCompile]
    void ISystem.OnCreate(ref SystemState state)
    {
        using EntityQueryBuilder builder = new(Allocator.Temp);
        builder.WithAny<NetworkId>();
        builder.WithNone<NetworkStreamInGame>();
        state.RequireForUpdate(state.GetEntityQuery(builder));
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;

        foreach (var (id, entity) in
            SystemAPI.Query<RefRO<NetworkId>>()
            .WithNone<NetworkStreamInGame>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.AddComponent<NetworkStreamInGame>(entity);
            NetcodeUtils.CreateRPC<GoInGameRpc>(commandBuffer, state.WorldUnmanaged);
        }
    }
}
