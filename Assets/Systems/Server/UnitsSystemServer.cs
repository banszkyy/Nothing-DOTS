using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct UnitsSystemServer : ISystem
{
    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<PlaceUnitRequestRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);
            NetworkId networkId = request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO;

            (Entity Entity, Player Player) requestPlayer = default;

            foreach (var (player, _entity) in
                SystemAPI.Query<RefRO<Player>>()
                .WithEntityAccess())
            {
                if (player.ValueRO.ConnectionId != networkId.Value) continue;
                requestPlayer = (_entity, player.ValueRO);
                break;
            }

            if (requestPlayer.Entity == Entity.Null)
            {
                Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Failed to place unit: requested by `{{0}}` but doesn't have a team", networkId));
                continue;
            }

            DynamicBuffer<BufferedUnit> units = SystemAPI.GetBuffer<BufferedUnit>(SystemAPI.GetSingletonEntity<UnitDatabase>());

            BufferedUnit unit = default;

            for (int i = 0; i < units.Length; i++)
            {
                if (units[i].Name == command.ValueRO.UnitName)
                {
                    unit = units[i];
                    break;
                }
            }

            if (unit.Prefab == Entity.Null)
            {
                Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Unit \"{{0}}\" not found in the database", command.ValueRO.UnitName));
                continue;
            }

            Entity newEntity;
            if (requestPlayer.Player.InCreative)
            {
                newEntity = commandBuffer.Instantiate(unit.Prefab);
                commandBuffer.SetComponent<LocalTransform>(newEntity, LocalTransform.FromPosition(command.ValueRO.Position));
            }
            else
            {
                Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Can't place unit \"{{0}}\": not in creative", unit.Name));
                continue;
            }
            commandBuffer.SetComponent<UnitTeam>(newEntity, new()
            {
                Team = requestPlayer.Player.Team,
            });
            commandBuffer.SetComponent<GhostOwner>(newEntity, new()
            {
                NetworkId = networkId.Value,
            });
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<UnitsRequestRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);
            NetworkId networkId = request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO;

            Entity requestPlayer = default;

            foreach (var (player, _entity) in
                SystemAPI.Query<RefRO<Player>>()
                .WithEntityAccess())
            {
                if (player.ValueRO.ConnectionId != networkId.Value) continue;
                requestPlayer = _entity;
                break;
            }

            if (requestPlayer == Entity.Null)
            {
                Debug.LogError(string.Format($"{DebugEx.ServerPrefix} Player with network id {{0}} aint have a team", networkId));
                continue;
            }

            DynamicBuffer<BufferedAcquiredResearch> acquiredResearches = SystemAPI.GetBuffer<BufferedAcquiredResearch>(requestPlayer);
            DynamicBuffer<BufferedUnit> units = SystemAPI.GetBuffer<BufferedUnit>(SystemAPI.GetSingletonEntity<UnitDatabase>());

            foreach (BufferedUnit unit in units)
            {
                if (!unit.RequiredResearch.IsEmpty)
                {
                    bool can = false;
                    foreach (BufferedAcquiredResearch research in acquiredResearches)
                    {
                        if (research.Name != unit.RequiredResearch) continue;
                        can = true;
                        break;
                    }

                    if (!can) continue;
                }

                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new UnitsResponseRpc()
                {
                    Name = unit.Name,
                }, request.ValueRO.SourceConnection);
            }
        }
    }
}
