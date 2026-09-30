using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct BuildingSystemServer : ISystem
{
    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<PlaceBuildingRequestRpc>>()
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
                Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Failed to place building: requested by `{{0}}` but doesn't have a team", networkId));
                continue;
            }

            TryPlaceBuilding(command.ValueRO.BuildingName, command.ValueRO.Position, commandBuffer, networkId, requestPlayer, ref state);
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<DestroyBuildingRpc>>()
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
                Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Failed to destroy building: requested by `{{0}}` but doesn't have a team", networkId));
                continue;
            }

            foreach (var (buildingGhost, buildingEntity) in SystemAPI.Query<RefRO<GhostInstance>>().WithAll<Building>().WithEntityAccess())
            {
                if (command.ValueRO.Entity.Equals(buildingGhost.ValueRO)) // FIXME: team check
                {
                    if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
                    commandBuffer.DestroyEntity(buildingEntity);
                }
            }
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<PlaceWireRequestRpc>>()
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
                Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Failed to place wire: requested by `{{0}}` but doesn't have a team", networkId));
                continue;
            }

            EntityPortIdentifier connectorA = default;
            EntityPortIdentifier connectorB = default;

            foreach (var (connectorGhost, connectorEntity) in SystemAPI.Query<RefRO<GhostInstance>>().WithAll<Connector>().WithEntityAccess()) // FIXME: team check
            {
                if (command.ValueRO.EntityA.Equals(connectorGhost.ValueRO))
                {
                    connectorA = new EntityPortIdentifier(connectorEntity, command.ValueRO.PortA);
                    if (!connectorB.Equals(default)) break;
                }

                if (command.ValueRO.EntityB.Equals(connectorGhost.ValueRO))
                {
                    connectorB = new EntityPortIdentifier(connectorEntity, command.ValueRO.PortB);
                    if (!connectorA.Equals(default)) break;
                }
            }

            if (connectorA == default || connectorB == default)
            {
                Debug.Log($"{DebugEx.ServerPrefix} Failed to place wire: connectors not found");
                continue;
            }

            if (connectorA == connectorB)
            {
                Debug.Log($"{DebugEx.ServerPrefix} Failed to place wire: the ports are the same");
                continue;
            }

            DynamicBuffer<BufferedWire> wiresA = SystemAPI.GetBuffer<BufferedWire>(connectorA.Entity);
            DynamicBuffer<BufferedWire> wiresB = SystemAPI.GetBuffer<BufferedWire>(connectorB.Entity);

            bool isRemove = false;
            for (int i = 0; i < wiresA.Length; i++)
            {
                BufferedWire item = wiresA[i];
                if ((item.PortIdentifierA == connectorA && item.PortIdentifierB == connectorB) ||
                    (item.PortIdentifierA == connectorB && item.PortIdentifierB == connectorA))
                {
                    wiresA.RemoveAtSwapBack(i);
                    isRemove = true;
                }
            }

            for (int i = 0; i < wiresB.Length; i++)
            {
                BufferedWire item = wiresB[i];
                if ((item.PortIdentifierA == connectorA && item.PortIdentifierB == connectorB) ||
                    (item.PortIdentifierA == connectorB && item.PortIdentifierB == connectorA))
                {
                    wiresB.RemoveAtSwapBack(i);
                    isRemove = true;
                }
            }

            if (!isRemove)
            {
                BufferedWire wire = new()
                {
                    EntityA = connectorA.Entity,
                    EntityB = connectorB.Entity,
                    PortA = connectorA.Port,
                    PortB = connectorB.Port,
                    GhostA = command.ValueRO.EntityA,
                    GhostB = command.ValueRO.EntityB,
                };

                wiresA.Add(wire);
                wiresB.Add(wire);
            }
        }

        foreach (var (placeholder, transform, owner, unitTeam, entity) in
            SystemAPI.Query<RefRW<BuildingPlaceholder>, RefRO<LocalToWorld>, RefRO<GhostOwner>, RefRO<UnitTeam>>()
            .WithEntityAccess())
        {
            if (placeholder.ValueRO.CurrentProgress < placeholder.ValueRO.TotalProgress) continue;

            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

            Entity newEntity = commandBuffer.Instantiate(placeholder.ValueRO.BuildingPrefab);
            commandBuffer.SetComponent<LocalTransform>(newEntity, LocalTransform.FromPositionRotation(transform.ValueRO.Position, transform.ValueRO.Rotation));
            commandBuffer.SetComponent<GhostOwner>(newEntity, new()
            {
                NetworkId = owner.ValueRO.NetworkId,
            });
            commandBuffer.SetComponent<UnitTeam>(newEntity, new()
            {
                Team = unitTeam.ValueRO.Team,
            });

            commandBuffer.DestroyEntity(entity);
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<BuildingsRequestRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);
            NetworkId networkId = request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO;

            Debug.Log(string.Format($"{DebugEx.ServerPrefix} Compiling buildings for client {{0}} ...", networkId));

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
                Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Player with network id `{{0}}` doesn't have a team", networkId));
                continue;
            }

            DynamicBuffer<BufferedAcquiredResearch> acquiredResearches = SystemAPI.GetBuffer<BufferedAcquiredResearch>(requestPlayer);
            DynamicBuffer<BufferedBuilding> buildings = SystemAPI.GetBuffer<BufferedBuilding>(SystemAPI.GetSingletonEntity<BuildingDatabase>());

            foreach (BufferedBuilding building in buildings)
            {
                if (!building.RequiredResearch.IsEmpty)
                {
                    foreach (BufferedAcquiredResearch research in acquiredResearches)
                    {
                        if (research.Name != building.RequiredResearch) continue;
                        goto _;
                    }
                }

                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new BuildingsResponseRpc()
                {
                    Name = building.Name,
                }, request.ValueRO.SourceConnection);

            _:;
            }
        }
    }

    bool TryPlaceBuilding(FixedString32Bytes buildingName, float3 position, EntityCommandBuffer commandBuffer, NetworkId networkId, Entity player, ref SystemState state)
    {
        Player _player = SystemAPI.GetComponent<Player>(player);

        DynamicBuffer<BufferedBuilding> buildings = SystemAPI.GetBuffer<BufferedBuilding>(SystemAPI.GetSingletonEntity<BuildingDatabase>());

        BufferedBuilding building = default;

        for (int i = 0; i < buildings.Length; i++)
        {
            if (buildings[i].Name == buildingName)
            {
                building = buildings[i];
                break;
            }
        }

        if (building.Prefab == Entity.Null)
        {
            Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Building \"{{0}}\" not found in the database", buildingName));
            return false;
        }

        if (SystemAPI.TryGetComponent(building.PlaceholderPrefab, out Collider collider))
        {
            var map = QuadrantSystem.GetMap(ConnectionManager.ServerOrDefaultWorld.Unmanaged);

            if (!TerrainGenerator.Instance.TrySampleFast(new(position.x, position.z), out position.y))
            {
                Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Invalid position (terrain isn't loaded yet)"));
                return false;
            }

            bool isValid = !Collision.Intersect(
                map,
                collider,
                position,
                out _,
                out _);

            if (!isValid)
            {
                Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Invalid position (something is in the way)"));
                return false;
            }
        }

        Entity newEntity;
        if (_player.InCreative)
        {
            newEntity = commandBuffer.Instantiate(building.Prefab);
            commandBuffer.SetComponent<LocalTransform>(newEntity, LocalTransform.FromPosition(position));
        }
        else
        {
            if (!building.RequiredResearch.IsEmpty)
            {
                DynamicBuffer<BufferedAcquiredResearch> acquiredResearches = SystemAPI.GetBuffer<BufferedAcquiredResearch>(player);
                bool can = false;
                foreach (var research in acquiredResearches)
                {
                    if (research.Name != building.RequiredResearch) continue;
                    can = true;
                    break;
                }

                if (!can)
                {
                    Debug.Log(string.Format($"{DebugEx.ServerPrefix} Can't place building \"{{0}}\": not researched", building.Name));
                    return false;
                }
            }

            if (_player.Resources < building.RequiredResources)
            {
                Debug.Log(string.Format($"{DebugEx.ServerPrefix} Can't place building \"{{0}}\": not enought resources ({{1}} < {{2}})", building.Name, _player.Resources, building.RequiredResources));
                return false;
            }

            if (SystemAPI.HasComponent<Extractor>(building.Prefab))
            {
                foreach (var resource in
                    SystemAPI.Query<RefRO<LocalTransform>>()
                    .WithAll<ResourceNode>())
                {
                    if (math.distance(resource.ValueRO.Position, position) < 5f)
                    {
                        goto ok;
                    }
                }

                Debug.Log(string.Format($"{DebugEx.ServerPrefix} Can't place building \"{{0}}\": needs a resource node in 5 radius", building.Name));
                return false;
            ok:;
            }

            SystemAPI.GetComponentRW<Player>(player).ValueRW.Resources -= building.RequiredResources;

            newEntity = commandBuffer.Instantiate(building.PlaceholderPrefab);
            commandBuffer.SetComponent<LocalTransform>(newEntity, LocalTransform.FromPosition(position));
            commandBuffer.SetComponent<BuildingPlaceholder>(newEntity, new()
            {
                BuildingName = building.Name,
                BuildingPrefab = building.Prefab,
                CurrentProgress = 0f,
                TotalProgress = building.ConstructionTime,
            });
        }
        commandBuffer.SetComponent<UnitTeam>(newEntity, new()
        {
            Team = _player.Team,
        });
        commandBuffer.SetComponent<GhostOwner>(newEntity, new()
        {
            NetworkId = networkId.Value,
        });

        return true;
    }
}
