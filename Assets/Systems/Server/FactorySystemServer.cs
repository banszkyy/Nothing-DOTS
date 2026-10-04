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
            NetcodeEndPoint ep = new(networkId, request.ValueRO.SourceConnection);

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
                Debug.LogError(string.Format($"{DebugEx.ServerPrefix} Failed to queue unit: requested by {{0}} but doesn't have a team", networkId));
                continue;
            }

            DynamicBuffer<BufferedAcquiredResearch> acquiredResearches = SystemAPI.GetBuffer<BufferedAcquiredResearch>(requestPlayerE);

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

                if (requestPlayer.Resources < unit.RequiredResources)
                {
                    Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Can't queue unit \"{{0}}\": not enought resources ({{1}} < {{2}})", unit.Name, requestPlayer.Resources, unit.RequiredResources));
                    break;
                }

                SystemAPI.GetComponentRW<Player>(requestPlayerE).ValueRW.Resources -= unit.RequiredResources;

                SystemAPI.GetBuffer<BufferedProducingUnit>(ghostEntity).Add(new BufferedProducingUnit()
                {
                    Name = unit.Name,
                    Prefab = unit.Prefab,
                    ProductionTime = unit.ProductionTime,
                    Source = new(command.ValueRO.Source, ep),
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
            if (finishedUnit.Source != default)
            {
                commandBuffer.AddComponent<ProcessorInitialization>(newUnit, new()
                {
                    SourceFile = finishedUnit.Source,
                });
            }
        }
    }
}
