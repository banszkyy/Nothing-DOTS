using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
[RequireMatchingQueriesForUpdate]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct PlayerPositionSystemServer : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<PlayerPositionSyncRpc>>()
            .WithEntityAccess())
        {
            commandBuffer.DestroyEntity(entity);
            RefRO<NetworkId> networkId = SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection);

            foreach (var player in SystemAPI.Query<RefRW<Player>>())
            {
                if (player.ValueRO.ConnectionId != networkId.ValueRO.Value) continue;
                player.ValueRW.Position = command.ValueRO.Position;
                break;
            }
        }
    }
}
