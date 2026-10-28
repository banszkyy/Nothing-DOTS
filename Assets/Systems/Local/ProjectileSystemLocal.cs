using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateBefore(typeof(LocalToWorldSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
partial struct ProjectileSystemLocal : ISystem
{
    BufferLookup<BufferedDamage> DamageQ;
    EntityArchetype VisualEffectSpawnArchetype;

    [BurstCompile]
    void ISystem.OnCreate(ref SystemState state)
    {
        DamageQ = state.GetBufferLookup<BufferedDamage>();
        VisualEffectSpawnArchetype = state.EntityManager.CreateArchetype(stackalloc ComponentType[]
        {
            ComponentType.ReadWrite<VisualEffectSpawn>(),
        });
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        TerrainSystemServer terrainSystem = TerrainSystemServer.GetInstance(state.WorldUnmanaged);

        DamageQ.Update(ref state);
        var map = QuadrantSystem.GetMap(ref state);

        foreach (var (transform, projectile, entity) in
            SystemAPI.Query<RefRW<LocalTransform>, RefRW<Projectile>>()
            .WithEntityAccess())
        {
            float t = SystemAPI.Time.DeltaTime;
            float travelDistance = math.length(projectile.ValueRO.Velocity * t);
            float3 lastPosition = transform.ValueRO.Position;
            float3 newPosition = lastPosition + (projectile.ValueRO.Velocity * t);
            float3 direction = projectile.ValueRO.Velocity / travelDistance * t;

            projectile.ValueRW.Velocity += new float3(0f, ProjectileSystemServer.Gravity, 0f) * t;
            transform.ValueRW.Position = newPosition;
            transform.ValueRW.Rotation = quaternion.LookRotation(direction, new float3(0f, 1f, 0f));

            if (transform.ValueRO.Position.y < ProjectileSystemServer.MinY)
            {
                commandBuffer.DestroyEntity(entity);
                continue;
            }

            Ray ray = new(lastPosition, direction, travelDistance, Layers.BuildingOrUnit);
            DynamicBuffer<BufferedDamage> damage = default;

            bool didHitTerrain = terrainSystem.Raycast(ray.Start, ray.Direction, math.distance(lastPosition, newPosition), out float terrainHit, out float3 normal);
            bool didHitUnit = QuadrantRayCast.Raycast(map, ray, out Hit unitHit) && DamageQ.TryGetBuffer(unitHit.Entity.Entity, out damage);

            float distance;
            bool metalHit;

            if (didHitTerrain && (!didHitUnit || unitHit.Distance >= terrainHit))
            {
                distance = terrainHit;
                metalHit = false;
                goto hit;
            }

            if (didHitUnit)
            {
                damage.Add(new()
                {
                    Amount = projectile.ValueRO.Damage,
                    Direction = math.normalize(projectile.ValueRO.Velocity),
                });
                distance = unitHit.Distance;
                normal = math.normalizesafe(-projectile.ValueRW.Velocity);
                metalHit = true;
                goto hit;
            }

            continue;
        hit:
            int effect = metalHit ? projectile.ValueRO.MetalImpactEffect : projectile.ValueRO.DustImpactEffect;

            if (effect != -1)
            {
                Entity visualEffectSpawn = commandBuffer.CreateEntity(VisualEffectSpawnArchetype);
                commandBuffer.SetComponent<VisualEffectSpawn>(visualEffectSpawn, new()
                {
                    Position = ray.GetPoint(distance),
                    Rotation = normal.ToEuler(),
                    Index = effect,
                });
            }

            commandBuffer.DestroyEntity(entity);
        }
    }
}
