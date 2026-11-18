using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateBefore(typeof(LocalToWorldSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct RigidbodySystemServer : ISystem
{
    void ISystem.OnUpdate(ref SystemState state)
    {
        PausableTime t = SystemAPI.GetSingleton<PausableTime>();
        if (t.IsPaused) return;

        TerrainSystemServer terrainSystem = TerrainSystemServer.GetInstance(state.WorldUnmanaged);

        foreach (var (rigidbody, collider, transform) in
            SystemAPI.Query<RefRW<Rigidbody>, RefRO<Collider>, RefRW<LocalTransform>>())
        {
            if (!rigidbody.ValueRO.IsEnabled) continue;

            //bool moved = math.abs(transform.ValueRO.Position.x - rigidbody.ValueRO.LastPosition.x) > 0.01f || math.abs(transform.ValueRO.Position.z - rigidbody.ValueRO.LastPosition.y) > 0.01f;

            switch (collider.ValueRO.Type)
            {
                case ColliderType.Sphere:
                {
                    ref readonly SphereCollider sphere = ref collider.ValueRO.Sphere;

                    if (!terrainSystem.TrySample(new float2(transform.ValueRO.Position.x, transform.ValueRO.Position.z), out float h, out float3 normal))
                    {
                        h = 0f;
                        normal = new float3(0f, 1f, 0f);
                    }

                    if (transform.ValueRW.Position.y <= h + sphere.Radius + 0.05f &&
                        math.lengthsq(rigidbody.ValueRW.Velocity) < 0.2f * 0.2f)
                    {
                        transform.ValueRW.Position.y = h + sphere.Radius;
                        break;
                    }

                    transform.ValueRW.Position += rigidbody.ValueRO.Velocity * SystemAPI.Time.DeltaTime;

                    if (transform.ValueRW.Position.y <= h + sphere.Radius)
                    {
                        transform.ValueRW.Position.y = h + sphere.Radius;
                        rigidbody.ValueRW.Velocity.y = math.abs(rigidbody.ValueRW.Velocity.y);
                        rigidbody.ValueRW.Velocity *= rigidbody.ValueRO.Bounciness;
                    }
                    else
                    {
                        rigidbody.ValueRW.Velocity += new float3(0f, ProjectileSystemServer.Gravity, 0f) * SystemAPI.Time.DeltaTime;
                        rigidbody.ValueRW.Velocity -= rigidbody.ValueRW.Velocity * (0.01f * SystemAPI.Time.DeltaTime);
                    }

                    break;
                }
                case ColliderType.AABB:
                {
                    ref readonly AABBCollider aabb = ref collider.ValueRO.AABB;
                    float height = aabb.AABB.Extents.y;

                    if (!terrainSystem.TrySample(new float2(transform.ValueRO.Position.x, transform.ValueRO.Position.z), out float h, out float3 normal))
                    {
                        h = 0f;
                        normal = new float3(0f, 1f, 0f);
                    }

                    if (transform.ValueRW.Position.y <= h + height + 0.05f &&
                        math.lengthsq(rigidbody.ValueRW.Velocity) < 0.2f * 0.2f)
                    {
                        transform.ValueRW.Position.y = h + height;
                        TerrainCollisionSystemServer.AlignPreserveYawExact(ref transform.ValueRW.Rotation, normal);
                        break;
                    }

                    transform.ValueRW.Position += rigidbody.ValueRO.Velocity * SystemAPI.Time.DeltaTime;

                    if (transform.ValueRW.Position.y <= h + height)
                    {
                        transform.ValueRW.Position.y = h + height;
                        rigidbody.ValueRW.Velocity.y = math.abs(rigidbody.ValueRW.Velocity.y);
                        rigidbody.ValueRW.Velocity *= rigidbody.ValueRO.Bounciness;
                    }
                    else
                    {
                        rigidbody.ValueRW.Velocity += new float3(0f, ProjectileSystemServer.Gravity, 0f) * SystemAPI.Time.DeltaTime;
                        rigidbody.ValueRW.Velocity -= rigidbody.ValueRW.Velocity * (0.01f * SystemAPI.Time.DeltaTime);
                    }

                    break;
                }
                default: throw new UnreachableException();
            }

            rigidbody.ValueRW.LastPosition = new float2(transform.ValueRO.Position.x, transform.ValueRO.Position.z);
        }
    }
}
