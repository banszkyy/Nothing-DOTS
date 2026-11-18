using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateBefore(typeof(LocalToWorldSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct TerrainCollisionSystemServer : ISystem
{
    [BurstCompile]
    public static void AlignPreserveYawExact(ref quaternion rotation, in float3 terrainNormal)
    {
        AlignPreserveYawExact(rotation.ToEuler().y, terrainNormal, out rotation);
    }

    [BurstCompile]
    public static void AlignPreserveYawExact(in quaternion rotation, in float3 terrainNormal, out quaternion result)
    {
        AlignPreserveYawExact(rotation.ToEuler().y, terrainNormal, out result);
    }

    [BurstCompile]
    public static void AlignPreserveYawExact(float yawRadians, in float3 terrainNormal, out quaternion result)
    {
        float3 N = math.normalizesafe(terrainNormal, new float3(0f, 1f, 0f));

        float3 fXZ = new(math.sin(yawRadians), 0f, math.cos(yawRadians));

        float3 forward;
        if (math.abs(N.y) > 1e-4f)
        {
            float y = -math.dot(fXZ, N) / N.y;
            forward = math.normalize(new float3(fXZ.x, y, fXZ.z));
        }
        else
        {
            forward = math.normalize(fXZ - math.dot(fXZ, N) * N);
        }

        float3 right = math.normalize(math.cross(N, forward));

        result = new quaternion(new float3x3(right, N, forward));
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        PausableTime t = SystemAPI.GetSingleton<PausableTime>();
        if (t.IsPaused) return;

        TerrainSystemServer terrainSystem = TerrainSystemServer.GetInstance(state.WorldUnmanaged);

        foreach (var (transform, terrainUnit) in
            SystemAPI.Query<RefRW<LocalTransform>, RefRW<TerrainUnit>>())
        {
            if (math.abs(transform.ValueRO.Position.x - terrainUnit.ValueRO.LastPosition.x) <= 0.01f &&
                math.abs(transform.ValueRO.Position.z - terrainUnit.ValueRO.LastPosition.y) <= 0.01f)
            { continue; }
            if (!terrainSystem.TrySample(new float2(transform.ValueRO.Position.x, transform.ValueRO.Position.z), out float h, out float3 normal, true))
            { continue; }

            transform.ValueRW.Position.y = h;
            AlignPreserveYawExact(ref transform.ValueRW.Rotation, normal);

            terrainUnit.ValueRW.LastPosition = new float2(transform.ValueRO.Position.x, transform.ValueRO.Position.z);
        }

        foreach (var (transform, terrainBuilding, collider, e) in
            SystemAPI.Query<RefRW<LocalTransform>, RefRW<TerrainBuilding>, RefRO<Collider>>().WithEntityAccess())
        {
            if (terrainBuilding.ValueRO.IsInitialized)
            { continue; }

            float h;
            float3 n;
            float o;

            if (collider.ValueRO.Type == ColliderType.AABB)
            {
                o = -collider.ValueRO.AABB.AABB.Min.y;
                float3 min = transform.ValueRO.Position + collider.ValueRO.AABB.AABB.Min;
                float3 max = transform.ValueRO.Position + collider.ValueRO.AABB.AABB.Max;
                if (!terrainSystem.TrySample(new float2(min.x, min.z), new float2(max.x, max.z), out h, out n))
                { continue; }
            }
            else
            {
                o = 0f;
                if (!terrainSystem.TrySample(new float2(transform.ValueRO.Position.x, transform.ValueRO.Position.z), out h, out n, false))
                { continue; }
            }

            transform.ValueRW.Position.y = h + o;
            AlignPreserveYawExact(ref transform.ValueRW.Rotation, n);

            terrainBuilding.ValueRW.IsInitialized = true;
        }
    }
}
