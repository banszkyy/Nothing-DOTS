using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct FactorySystemServer : ISystem
{
    void ISystem.OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<UnitDatabase>();
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;

        Entity unitDatabase = SystemAPI.GetSingletonEntity<UnitDatabase>();
        DynamicBuffer<BufferedUnit> units = SystemAPI.GetBuffer<BufferedUnit>(unitDatabase);

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<FactoryQueueUnitRequestRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);
            NetworkId networkId = request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO;

            Entity playerE = default;
            Player player = default;

            foreach (var (_player, _entity) in
                SystemAPI.Query<RefRO<Player>>()
                .WithEntityAccess())
            {
                if (_player.ValueRO.ConnectionId != networkId.Value) continue;
                playerE = _entity;
                player = _player.ValueRO;
                break;
            }

            if (playerE == Entity.Null)
            {
                Debug.LogError(string.Format($"{DebugEx.ServerPrefix} Failed to queue unit: requested by {{0}} but aint have a team", networkId));
                continue;
            }

            DynamicBuffer<BufferedAcquiredResearch> acquiredResearches = SystemAPI.GetBuffer<BufferedAcquiredResearch>(playerE);

            foreach (var (ghostInstance, ghostEntity) in
                SystemAPI.Query<RefRO<GhostInstance>>()
                .WithAll<Factory>()
                .WithEntityAccess())
            {
                if (!command.ValueRO.Entity.Equals(ghostInstance.ValueRO)) continue;

                BufferedUnit unit = default;
                for (int i = 0; i < units.Length; i++)
                {
                    if (units[i].Name != command.ValueRO.Unit) continue;
                    unit = units[i];
                    break;
                }

                if (unit.Prefab == Entity.Null)
                {
                    Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Unit \"{{0}}\" not found in the database", command.ValueRO.Unit));
                    break;
                }

                if (!unit.RequiredResearch.IsEmpty)
                {
                    bool can = false;
                    foreach (var research in acquiredResearches)
                    {
                        if (research.Name != unit.RequiredResearch) continue;
                        can = true;
                        break;
                    }

                    if (!can)
                    {
                        Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Can't queue unit \"{{0}}\": not researched", unit.Name));
                        break;
                    }
                }

                if (player.Resources < unit.RequiredResources)
                {
                    Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Can't queue unit \"{{0}}\": not enought resources ({{1}} < {{2}})", unit.Name, player.Resources, unit.RequiredResources));
                    break;
                }

                foreach (var _player in
                    SystemAPI.Query<RefRW<Player>>())
                {
                    if (_player.ValueRO.ConnectionId != networkId.Value) continue;
                    _player.ValueRW.Resources -= unit.RequiredResources;
                    break;
                }

                SystemAPI.GetBuffer<BufferedProducingUnit>(ghostEntity).Add(new BufferedProducingUnit()
                {
                    Name = unit.Name,
                    Prefab = unit.Prefab,
                    ProductionTime = unit.ProductionTime,
                });

                break;
            }
        }

        foreach (var (factory, localToWorld, unitTeam, unitQueue) in
                SystemAPI.Query<RefRW<Factory>, RefRO<LocalToWorld>, RefRO<UnitTeam>, DynamicBuffer<BufferedProducingUnit>>())
        {
            if (factory.ValueRO.TotalProgress == default)
            {
                if (unitQueue.Length > 0)
                {
                    BufferedProducingUnit unit = unitQueue[0];
                    unitQueue.RemoveAt(0);
                    factory.ValueRW.Current = unit;
                    factory.ValueRW.CurrentProgress = 0f;
                    factory.ValueRW.TotalProgress = unit.ProductionTime;
                }

                continue;
            }

            factory.ValueRW.CurrentProgress += SystemAPI.Time.DeltaTime * Factory.ProductionSpeed;

            if (factory.ValueRO.CurrentProgress < factory.ValueRO.TotalProgress)
            { continue; }

            BufferedProducingUnit finishedUnit = factory.ValueRO.Current;

            factory.ValueRW.Current = default;
            factory.ValueRW.CurrentProgress = default;
            factory.ValueRW.TotalProgress = default;

            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            Entity newUnit = commandBuffer.Instantiate(finishedUnit.Prefab);
            commandBuffer.SetComponent(newUnit, LocalTransform.FromPosition(localToWorld.ValueRO.Position + new float3(0f, 0f, 1.5f)));
            commandBuffer.SetComponent<UnitTeam>(newUnit, new()
            {
                Team = unitTeam.ValueRO.Team
            });
        }
    }
}
