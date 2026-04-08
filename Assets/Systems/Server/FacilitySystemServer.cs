using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct FacilitySystemServer : ISystem
{
    void ISystem.OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<UnitDatabase>();
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<FacilityQueueResearchRequestRpc>>()
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
                Debug.LogError(string.Format($"{DebugEx.ServerPrefix} Failed to start research: requested by {{0}} but aint have a team", networkId));
                return;
            }

            var acquiredResearches = SystemAPI.GetBuffer<BufferedAcquiredResearch>(requestPlayer);

            foreach (var (ghostInstance, ghostEntity) in
                SystemAPI.Query<RefRO<GhostInstance>>()
                .WithAll<Facility>()
                .WithEntityAccess())
            {
                if (!command.ValueRO.Entity.Equals(ghostInstance.ValueRO)) continue;

                Research research = default;
                bool canResearch = false;

                foreach (var (_research, requirements) in
                    SystemAPI.Query<RefRO<Research>, DynamicBuffer<BufferedResearchRequirement>>())
                {
                    if (_research.ValueRO.Name != command.ValueRO.ResearchName) continue;
                    research = _research.ValueRO;
                    canResearch = true;

                    foreach (var requirement in requirements)
                    {
                        bool has = false;
                        foreach (var acquired in acquiredResearches)
                        {
                            if (requirement.Name == acquired.Name)
                            {
                                has = true;
                                break;
                            }
                        }
                        if (!has)
                        {
                            canResearch = false;
                        }
                    }

                    break;
                }

                if (research.Name.IsEmpty)
                {
                    Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Research \"{{0}}\" not found in the database", command.ValueRO.ResearchName));
                    break;
                }

                if (!canResearch)
                {
                    Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Research \"{{0}}\" cannot be started", command.ValueRO.ResearchName));
                    break;
                }

                bool alreadyHas = false;
                foreach (var acquired in acquiredResearches)
                {
                    if (research.Name == acquired.Name)
                    {
                        alreadyHas = true;
                        break;
                    }
                }

                if (alreadyHas)
                {
                    Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Research \"{{0}}\" already acquired", research.Name));
                    break;
                }

                SystemAPI.GetBuffer<BufferedResearch>(ghostEntity).Add(new BufferedResearch()
                {
                    Name = research.Name,
                    Hash = research.Hash,
                    ResearchTime = research.ResearchTime,
                });

                break;
            }
        }

        foreach (var (facility, unitTeam, queue, hashIn, hashOut) in
            SystemAPI.Query<RefRW<Facility>, RefRO<UnitTeam>, DynamicBuffer<BufferedResearch>, DynamicBuffer<BufferedTechnologyHashIn>, DynamicBuffer<BufferedTechnologyHashOut>>())
        {
            Research finishedResearch;

            if (!hashIn.IsEmpty)
            {
                BufferedTechnologyHashIn hash = hashIn[0];
                hashIn.RemoveAt(0);

                foreach (var research in
                    SystemAPI.Query<RefRO<Research>>())
                {
                    if (FixedBytes.AreEqual(research.ValueRO.Hash, hash.Hash))
                    {
                        finishedResearch = research.ValueRO;
                        goto good;
                    }
                }

                Debug.LogError(string.Format($"{DebugEx.ServerPrefix} Research with hash \"{{0}}\" not found", hash.Hash));
                continue;
            good:;
            }
            else
            {
                if (facility.ValueRO.Current.Name.IsEmpty)
                {
                    if (queue.Length > 0)
                    {
                        BufferedResearch research = queue[0];
                        queue.RemoveAt(0);
                        facility.ValueRW.Current = research;
                        facility.ValueRW.CurrentProgress = 0f;
                    }

                    continue;
                }

                facility.ValueRW.CurrentProgress += SystemAPI.Time.DeltaTime * Facility.ResearchSpeed;

                if (facility.ValueRO.CurrentProgress < facility.ValueRO.Current.ResearchTime)
                { continue; }

                finishedResearch = new Research()
                {
                    Hash = facility.ValueRO.Current.Hash,
                    Name = facility.ValueRO.Current.Name,
                    ResearchTime = facility.ValueRO.Current.ResearchTime,
                };

                facility.ValueRW.Current = default;
                facility.ValueRW.CurrentProgress = default;

                hashOut.Add(new BufferedTechnologyHashOut()
                {
                    Hash = finishedResearch.Hash,
                });
            }

            Entity playerEntity = default;
            Player player = default;

            foreach (var (_player, _entity) in
                SystemAPI.Query<RefRO<Player>>()
                .WithEntityAccess())
            {
                if (_player.ValueRO.Team != unitTeam.ValueRO.Team) continue;
                playerEntity = _entity;
                player = _player.ValueRO;
                break;
            }

            if (playerEntity == Entity.Null)
            {
                Debug.LogError(string.Format($"{DebugEx.ServerPrefix} Failed to finish research: No player found im team {{0}}", unitTeam.ValueRO.Team));
                return;
            }

            Entity playerConnection = default;
            foreach (var (networkId, _entity) in
                SystemAPI.Query<RefRO<NetworkId>>()
                .WithEntityAccess())
            {
                if (networkId.ValueRO.Value != player.ConnectionId) continue;
                playerConnection = _entity;
                break;
            }

            if (playerConnection != Entity.Null)
            {
                if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new ResearchDoneRpc()
                {
                    Name = finishedResearch.Name,
                }, playerConnection);
            }

            SystemAPI.GetBuffer<BufferedAcquiredResearch>(playerEntity)
                .Add(new BufferedAcquiredResearch()
                {
                    Name = finishedResearch.Name,
                });
        }
    }
}
