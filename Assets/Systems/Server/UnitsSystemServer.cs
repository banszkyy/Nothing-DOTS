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

            Entity requestPlayerE = default;
            Player requestPlayer = default;

            if (SystemAPI.TryGetComponent(entity, out VirtualRpc virtualRpc))
            {
                foreach (var (player, _player, _entity) in
                    SystemAPI.Query<RefRO<Player>, RefRO<VirtualPlayer>>()
                    .WithEntityAccess())
                {
                    if (_player.ValueRO.Index != virtualRpc.PlayerIndex) continue;
                    requestPlayerE = _entity;
                    requestPlayer = player.ValueRO;
                    break;
                }
            }
            else
            {
                foreach (var (player, _player, _entity) in
                    SystemAPI.Query<RefRO<Player>, RefRO<RealPlayer>>()
                    .WithEntityAccess())
                {
                    if (_player.ValueRO.ConnectionId != networkId.Value) continue;
                    requestPlayerE = _entity;
                    requestPlayer = player.ValueRO;
                    break;
                }
            }

            if (requestPlayerE == Entity.Null)
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
            if (requestPlayer.InCreative)
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
                Team = requestPlayer.Team,
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

            if (SystemAPI.TryGetComponent(entity, out VirtualRpc virtualRpc))
            {
                foreach (var (player, _entity) in
                    SystemAPI.Query<RefRO<VirtualPlayer>>()
                    .WithEntityAccess())
                {
                    if (player.ValueRO.Index != virtualRpc.PlayerIndex) continue;
                    requestPlayer = _entity;
                    break;
                }
            }
            else
            {
                foreach (var (player, _entity) in
                    SystemAPI.Query<RefRO<RealPlayer>>()
                    .WithEntityAccess())
                {
                    if (player.ValueRO.ConnectionId != networkId.Value) continue;
                    requestPlayer = _entity;
                    break;
                }
            }

            if (requestPlayer == Entity.Null)
            {
                Debug.LogError(string.Format($"{DebugEx.ServerPrefix} Player with network id {{0}} doesn't have a team", networkId));
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
