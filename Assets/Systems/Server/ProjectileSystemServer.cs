#if UNITY_EDITOR && EDITOR_DEBUG
#define _DEBUG_LINES
#endif

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateBefore(typeof(LocalToWorldSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct ProjectileSystemServer : ISystem
{
    BufferLookup<BufferedDamage> damageQ;
    public const float Gravity = -9.82f;
    public const float MinY = -5f;

    [BurstCompile]
    void ISystem.OnCreate(ref SystemState state)
    {
        damageQ = state.GetBufferLookup<BufferedDamage>();
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        TerrainSystemServer terrainSystem = TerrainSystemServer.GetInstance(state.WorldUnmanaged);

        damageQ.Update(ref state);
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
            transform.ValueRW.Position = newPosition;
            projectile.ValueRW.Velocity += new float3(0f, Gravity, 0f) * SystemAPI.Time.DeltaTime;

            Ray ray = new(lastPosition, direction, travelDistance, Layers.BuildingOrUnit);
            DynamicBuffer<BufferedDamage> damage = default;

#if DEBUG_LINES
            DebugEx.DrawLine(ray.Start, ray.End, Color.Lerp(Color.green, Color.white, 0.5f), 4f);
#endif

            bool didHitTerrain = terrainSystem.Raycast(ray.Start, ray.Direction, math.distance(lastPosition, newPosition), out float terrainHit, out _);
            bool didHitUnit = QuadrantRayCast.RayCast(map, ray, out Hit unitHit) && damageQ.TryGetBuffer(unitHit.Entity.Entity, out damage);

            if (didHitTerrain && (!didHitUnit || unitHit.Distance >= terrainHit))
            {
#if DEBUG_LINES
                DebugEx.DrawPoint(ray.GetPoint(terrainHit), 0.2f, Color.orange, 4f);
#endif
                commandBuffer.DestroyEntity(entity);
                continue;
            }

            if (didHitUnit)
            {
#if DEBUG_LINES
                DebugEx.DrawPoint(ray.GetPoint(unitHit.Distance), 0.2f, Color.green, 4f);
#endif

                damage.Add(new()
                {
                    Amount = projectile.ValueRO.Damage,
                    Direction = math.normalize(projectile.ValueRO.Velocity),
                });
                commandBuffer.DestroyEntity(entity);
                continue;
            }

            if (transform.ValueRO.Position.y < MinY)
            {
#if DEBUG_LINES
                DebugEx.DrawPoint(transform.ValueRO.Position, 0.2f, Color.red, 1f);
#endif
                commandBuffer.DestroyEntity(entity);
                continue;
            }
        }

        // EndSimulationEntityCommandBufferSystem.Singleton ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();

        // ProjectileJob projectileJob = new()
        // {
        //     EntityCommandBuffer = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged),
        //     DeltaTime = SystemAPI.Time.DeltaTime,
        //     CollisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
        //     DamageQ = damageQ,
        // };

        // projectileJob.Schedule();
    }
}

/*
[BurstCompile]
public partial struct ProjectileJob : IJobEntity
{
    public EntityCommandBuffer EntityCommandBuffer;
    public float DeltaTime;
    public CollisionWorld CollisionWorld;
    public BufferLookup<BufferedDamage> DamageQ;

    [BurstCompile]
    void Execute(Entity entity, ref Projectile projectile, ref LocalTransform transform)
    {
        float3 lastPosition = transform.Position;
        transform.Position += projectile.Velocity * DeltaTime;
        projectile.Velocity += new float3(0f, -9.82f, 0f) * DeltaTime;

        if (transform.Position.y < 0f)
        {
            EntityCommandBuffer.DestroyEntity(entity);
            return;
        }

        float3 lastPositionWorld = transform.TransformPoint(lastPosition);
        float3 positionWorld = transform.TransformPoint(transform.Position);

        RaycastInput input = new()
        {
            Start = lastPositionWorld,
            End = positionWorld,
            Filter = new CollisionFilter()
            {
                BelongsTo = Layers.All,
                CollidesWith = Layers.All,
                GroupIndex = 0,
            },
        };

        if (!CollisionWorld.CastRay(input, out Unity.Physics.RaycastHit hit))
        { return; }

        Debug.Log("Bruh");

        if (DamageQ.TryGetBuffer(hit.Entity, out var damage))
        {
            damage.Add(new BufferedDamage(1f, math.normalize(projectile.Velocity)));
            EntityCommandBuffer.DestroyEntity(entity);
            return;
        }
    }
}
*/
