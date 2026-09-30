using System;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct VirtualPlayerSystemServer : ISystem
{
    void ISystem.OnUpdate(ref SystemState state)
    {
        foreach (var (player, playerV, playerE) in SystemAPI.Query<RefRO<Player>, RefRO<VirtualPlayer>>().WithEntityAccess())
        {
            if (player.ValueRO.Outcome != GameOutcome.None) continue;
            if (!player.ValueRO.IsCoreComputerSpawned) continue;

            int facilities = 0;
            int factories = 0;
            Entity nextFacility = Entity.Null;
            Entity nextFactory = Entity.Null;
            Research nextResearch = default;
            bool buildFacility = false;
            bool buildFactory = false;
            Entity coreComputer = Entity.Null;

            foreach (var (_coreComputer, team, entity) in SystemAPI.Query<RefRO<CoreComputer>, RefRO<UnitTeam>>().WithEntityAccess())
            {
                if (team.ValueRO.Team != player.ValueRO.Team) continue;
                coreComputer = entity;
            }

            // Research

            foreach (var (facility, team, queue, entity) in SystemAPI.Query<RefRO<Facility>, RefRO<UnitTeam>, DynamicBuffer<BufferedResearch>>().WithEntityAccess())
            {
                if (team.ValueRO.Team != player.ValueRO.Team) continue;
                facilities++;

                if (facility.ValueRO.Current.Name != default) continue;
                if (!queue.IsEmpty) continue;

                nextFacility = entity;
            }

            DynamicBuffer<BufferedAcquiredResearch> acquiredResearches = SystemAPI.GetBuffer<BufferedAcquiredResearch>(playerE);
            foreach (var (_research, requirements) in
                SystemAPI.Query<RefRO<Research>, DynamicBuffer<BufferedResearchRequirement>>())
            {
                bool hasAllRequirements = true;

                foreach (BufferedResearchRequirement requirement in requirements)
                {
                    bool hasThis = false;

                    foreach (BufferedAcquiredResearch acquired in acquiredResearches)
                    {
                        if (requirement.Name != acquired.Name) continue;
                        hasThis = true;
                        break;
                    }

                    if (!hasThis)
                    {
                        hasAllRequirements = false;
                        break;
                    }
                }
                if (!hasAllRequirements) continue;

                bool alreadyResearched = false;
                foreach (BufferedAcquiredResearch acquired in acquiredResearches)
                {
                    if (_research.ValueRO.Name != acquired.Name) continue;
                    alreadyResearched = true;
                    break;
                }
                if (alreadyResearched) continue;

                nextResearch = _research.ValueRO;
                break;
            }

            if (nextResearch.Name != default)
            {
                if (nextFacility == default)
                {
                    buildFacility = facilities == 0;
                }
                else
                {
                    Debug.Log($"{DebugEx.AIPrefix}#{playerV.ValueRO.Index} Researching {nextResearch.Name} at {nextFacility}");
                    SystemAPI.GetBuffer<BufferedResearch>(nextFacility).Add(new()
                    {
                        Hash = nextResearch.Hash,
                        Name = nextResearch.Name,
                        ResearchTime = nextResearch.ResearchTime,
                    });
                }
            }

            // Producing units

            int attackers = 0;
            int builders = 0;

            foreach (var (builder, processor, team, ghost, entity) in SystemAPI.Query<RefRO<Builder>, RefRO<Processor>, RefRO<UnitTeam>, RefRO<GhostInstance>>().WithEntityAccess())
            {
                if (team.ValueRO.Team != player.ValueRO.Team) continue;
                builders++;
            }

            foreach (var (unit, combatTurret, processor, team, ghost, entity) in SystemAPI.Query<RefRO<Unit>, RefRO<CombatTurret>, RefRO<Processor>, RefRO<UnitTeam>, RefRO<GhostInstance>>().WithEntityAccess())
            {
                if (team.ValueRO.Team != player.ValueRO.Team) continue;
                attackers++;
            }

            foreach (var (factory, team, queue, entity) in SystemAPI.Query<RefRO<Factory>, RefRO<UnitTeam>, DynamicBuffer<BufferedProducingUnit>>().WithEntityAccess())
            {
                if (team.ValueRO.Team != player.ValueRO.Team) continue;
                factories++;

                if (factory.ValueRO.Current.Name != default) continue;
                if (!queue.IsEmpty) continue;

                nextFactory = entity;
            }

            if (factories == 0)
            {
                buildFactory = true;
            }
            else if (nextFactory != default)
            {
                if (builders == 0)
                {
                    if (TryGetUnit("Builder", out var unit, ref state))
                    {
                        NetcodeUtils.CreateFakeRPC(ConnectionManager.ClientOrDefaultWorld.Unmanaged, new FactoryQueueUnitRequestRpc()
                        {
                            Unit = unit.Name,
                            Entity = SystemAPI.GetComponent<GhostInstance>(nextFactory),
                        }, playerV.ValueRO.Index);
                    }
                }
                else if (attackers == 0)
                {
                    if (TryGetUnit("Apc", out var unit, ref state))
                    {
                        NetcodeUtils.CreateFakeRPC(ConnectionManager.ClientOrDefaultWorld.Unmanaged, new FactoryQueueUnitRequestRpc()
                        {
                            Unit = unit.Name,
                            Entity = SystemAPI.GetComponent<GhostInstance>(nextFactory),
                        }, playerV.ValueRO.Index);
                    }
                }
            }

            // Build

            Entity nextBuilder = default;

            foreach (var (builder, processor, team, ghost, entity) in SystemAPI.Query<RefRO<Builder>, RefRO<Processor>, RefRO<UnitTeam>, RefRO<GhostInstance>>().WithEntityAccess())
            {
                if (team.ValueRO.Team != player.ValueRO.Team) continue;

                if (processor.ValueRO.SourceFile == default)
                {
                    Debug.Log($"{DebugEx.AIPrefix}#{playerV.ValueRO.Index} Setting processor source at {entity}");
                    NetcodeUtils.CreateFakeRPC<SetProcessorSourceRequestRpc>(state.WorldUnmanaged, new()
                    {
                        Entity = ghost.ValueRO,
                        IsHotReload = false,
                        Source = "ai/builder.bbc",
                    }, playerV.ValueRO.Index);
                    continue;
                }

                if (!processor.ValueRO.Source.Code.IsCreated || processor.ValueRO.Signal != LanguageCore.Runtime.Signal.None) continue;

                ReadOnlySpan<byte> stdoutRaw;
                unsafe { stdoutRaw = new ReadOnlySpan<byte>(processor.ValueRO.StdOutBuffer.GetUnsafePtr(), processor.ValueRO.StdOutBuffer.Length); }
                var stdout = Encoding.ASCII.GetString(stdoutRaw);

                if (!stdout.EndsWith("ready\n")) continue;
                nextBuilder = entity;
                break;
            }

            if (nextBuilder != default)
            {
                if (buildFacility)
                {
                    if (TryGetBuilding("Facility", out var building, ref state))
                    {
                        TryBuild(building, nextBuilder, player.ValueRO, playerV.ValueRO, SystemAPI.GetComponentRO<LocalTransform>(coreComputer).ValueRO.Position, ref state);
                    }
                }
                else if (buildFactory)
                {
                    if (TryGetBuilding("Factory", out var building, ref state))
                    {
                        TryBuild(building, nextBuilder, player.ValueRO, playerV.ValueRO, SystemAPI.GetComponentRO<LocalTransform>(coreComputer).ValueRO.Position, ref state);
                    }
                }
            }
        }
    }

    bool TryGetUnit(in FixedString32Bytes name, out BufferedUnit unit, ref SystemState state)
    {
        DynamicBuffer<BufferedUnit> units = SystemAPI.GetBuffer<BufferedUnit>(SystemAPI.GetSingletonEntity<UnitDatabase>());

        for (int i = 0; i < units.Length; i++)
        {
            if (units[i].Name == name)
            {
                unit = units[i];
                return true;
            }
        }

        unit = default;
        return false;
    }

    bool TryGetBuilding(in FixedString32Bytes name, out BufferedBuilding building, ref SystemState state)
    {
        DynamicBuffer<BufferedBuilding> buildings = SystemAPI.GetBuffer<BufferedBuilding>(SystemAPI.GetSingletonEntity<BuildingDatabase>());

        for (int i = 0; i < buildings.Length; i++)
        {
            if (buildings[i].Name == name)
            {
                building = buildings[i];
                return true;
            }
        }

        building = default;
        return false;
    }

    bool TryBuild(in BufferedBuilding building, Entity nextBuilder, in Player player, in VirtualPlayer playerV, float3 origin, ref SystemState state)
    {
        Entity placeholder = Entity.Null;

        foreach (var (_placeholder, team, entity) in SystemAPI.Query<RefRO<BuildingPlaceholder>, RefRO<UnitTeam>>().WithEntityAccess())
        {
            if (team.ValueRO.Team != player.Team) continue;
            if (_placeholder.ValueRO.BuildingName != building.Name) continue;

            placeholder = entity;
            break;
        }

        if (placeholder != Entity.Null)
        {
            Debug.Log($"{DebugEx.AIPrefix}#{playerV.Index} Commanding builder {nextBuilder} to build at {placeholder}");
            NetcodeUtils.CreateFakeRPC<UnitCommandRequestRpc>(state.WorldUnmanaged, new()
            {
                Entity = SystemAPI.GetComponentRO<GhostInstance>(nextBuilder).ValueRO,
                CommandId = 2,
                Arguments = new()
                {
                    WorldPosition = SystemAPI.GetComponentRO<LocalTransform>(placeholder).ValueRO.Position,
                },
            }, playerV.Index);
            return true;
        }

        var map = QuadrantSystem.GetMap(ConnectionManager.ServerOrDefaultWorld.Unmanaged);
        Collider placeholderCollider = new AABBCollider(true, new AABB() { Extents = new float3(1f, 1f, 1f) });
        var coreComputerP = origin;
        var validPosition = float3.zero;

        for (int retry = 0; retry < 20; retry++)
        {
            float2 rot = RandomManaged.Shared.Rotation();
            float3 position = coreComputerP + (new float3(rot.x, 0f, rot.y) * retry);
            if (!TerrainGenerator.Instance.TrySampleFast(new(position.x, position.z), out position.y)) continue;

            bool isValid = !Collision.Intersect(
                map,
                placeholderCollider,
                position,
                out _,
                out _);

            if (!isValid) continue;

            validPosition = position;
            break;
        }

        if (!validPosition.Equals(default))
        {
            Debug.Log($"{DebugEx.AIPrefix}#{playerV.Index} Placing building at {validPosition}");
            NetcodeUtils.CreateFakeRPC<PlaceBuildingRequestRpc>(state.WorldUnmanaged, new()
            {
                BuildingName = building.Name,
                Position = validPosition,
            }, playerV.Index);
            return true;
        }

        return false;
    }
}
