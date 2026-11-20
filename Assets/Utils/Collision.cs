using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public static class Collision
{
    #region With normals

    static bool CircleAABBIntersect(
        in float3 sphereOrigin, float sphereRadius,
        in float3 aabbOrigin, in AABB aabb,
        out float3 normal, out float depth
    )
    {
        depth = default;
        float3 closestPoint = math.clamp(sphereOrigin, aabb.Min + aabbOrigin, aabb.Max + aabbOrigin);
        if (closestPoint.Equals(sphereOrigin)) closestPoint = aabb.Center.x + aabbOrigin.x;
        normal = sphereOrigin - closestPoint;

        float distanceSquared = math.lengthsq(normal);
        if (distanceSquared >= sphereRadius * sphereRadius)
        { return false; }

        if (distanceSquared == 0f)
        { return false; }

        float distance = math.sqrt(distanceSquared);

        normal /= distance;
        depth = math.abs(sphereRadius - distance);
        return true;
    }

    static bool CircleCircleIntersect(
        in float3 originA, float radiusA,
        in float3 originB, float radiusB,
        out float3 normal, out float depth
    )
    {
        depth = default;
        normal = originA - originB;

        float distance = math.length(normal);
        if (distance == 0f) return false;

        float radii = radiusA + radiusB;

        if (distance > radii) return false;

        normal /= distance;
        depth = math.abs(radii - distance);
        return true;
    }

    static bool AABBAABBIntersect(
        in float3 originA, in AABB a,
        in float3 originB, in AABB b,
        out float3 normal, out float depth
    )
    {
        AABB _a = a;
        AABB _b = b;

        _a.Center += originA;
        _b.Center += originB;

        normal = default;
        depth = default;

        if (
            _a.Min.x <= _b.Max.x &&
            _a.Max.x >= _b.Min.x &&

            _a.Min.y <= _b.Max.y &&
            _a.Max.y >= _b.Min.y &&

            _a.Min.z <= _b.Max.z &&
            _a.Max.z >= _b.Min.z
        )
        {
            // float dx = Math.Min(a.Max.x - b.Min.x, b.Max.x - a.Min.x);
            // float dy = Math.Min(a.Max.y - b.Min.y, b.Max.y - a.Min.y);
            // float dz = Math.Min(a.Max.z - b.Min.z, b.Max.z - a.Min.z);
            return true;
        }

        return false;
    }

    public static bool Intersect(
        in Collider a, in float3 positionA,
        in Collider b, in float3 positionB,
        out float3 normal, out float depth
    )
    {
        if (a.Type == ColliderType.Sphere &&
            b.Type == ColliderType.Sphere)
        {
            return CircleCircleIntersect(
                positionA + a.Sphere.Offset + a.ResolvedOffset, a.Sphere.Radius,
                positionB + b.Sphere.Offset + b.ResolvedOffset, b.Sphere.Radius,
                out normal, out depth
            );
        }
        else if (a.Type == ColliderType.Sphere &&
                 b.Type == ColliderType.AABB)
        {
            return CircleAABBIntersect(
                positionA + a.Sphere.Offset + a.ResolvedOffset, a.Sphere.Radius,
                positionB + b.ResolvedOffset, b.AABB.AABB,
                out normal, out depth
            );
        }
        else if (a.Type == ColliderType.AABB &&
                 b.Type == ColliderType.Sphere)
        {
            return CircleAABBIntersect(
                positionB + b.Sphere.Offset + b.ResolvedOffset, b.Sphere.Radius,
                positionA + a.ResolvedOffset, a.AABB.AABB,
                out normal, out depth
            );
        }
        else if (a.Type == ColliderType.AABB &&
                 b.Type == ColliderType.AABB)
        {
            return AABBAABBIntersect(
                positionA + a.ResolvedOffset, a.AABB.AABB,
                positionB + b.ResolvedOffset, b.AABB.AABB,
                out normal, out depth
            );
        }
        else
        {
            Debug.LogError(string.Format($"{DebugEx.AnyPrefix} Unsupported collision combination: `{{0}}` - `{{1}}`", a.Type, b.Type));
            normal = default;
            depth = default;
            return false;
        }
    }

    public static bool Intersect(
        in NativeParallelHashMap<uint, NativeList<QuadrantEntity>>.ReadOnly quadrantMap,
        in Collider collider, in float3 colliderPosition,
        in ComponentLookup<Collider> colliderLookup,
        in ComponentLookup<LocalTransform> transformLookup,
        out float3 normal, out float depth
    )
    {
        normal = default;
        depth = default;

        if (!quadrantMap.TryGetValue(Cell.ToGrid(colliderPosition).key, out var quadrant)) return false;

        for (int i = 0; i < quadrant.Length; i++)
        {
            if (Intersect(colliderLookup.GetRefRO(quadrant[i].Entity).ValueRO, transformLookup.GetRefRO(quadrant[i].Entity).ValueRO.Position, collider, colliderPosition, out normal, out depth))
            { return true; }
        }

        return false;
    }

    public static bool Intersect(
        in NativeParallelHashMap<uint, NativeList<QuadrantEntity>>.ReadOnly quadrantMap,
        in Collider collider, in float3 colliderPosition,
        in EntityManager entityManager,
        out float3 normal, out float depth
    )
    {
        normal = default;
        depth = default;

        if (!quadrantMap.TryGetValue(Cell.ToGrid(colliderPosition).key, out var quadrant)) return false;

        for (int i = 0; i < quadrant.Length; i++)
        {
            if (Intersect(entityManager.GetComponentData<Collider>(quadrant[i].Entity), entityManager.GetComponentData<LocalTransform>(quadrant[i].Entity).Position, collider, colliderPosition, out normal, out depth))
            { return true; }
        }

        return false;
    }

    #endregion

    #region  Without normals

    static bool CircleAABBIntersect(
        in float3 sphereOrigin, float sphereRadius,
        in float3 aabbOrigin, in AABB aabb
    ) => math.distancesq(sphereOrigin, math.clamp(sphereOrigin, aabb.Min + aabbOrigin, aabb.Max + aabbOrigin)) < sphereRadius * sphereRadius;

    static bool CircleCircleIntersect(
        in float3 originA, float radiusA,
        in float3 originB, float radiusB
    ) => math.distancesq(originA, originB) <= radiusA + radiusB;

    static bool AABBAABBIntersect(
        in float3 originA, in AABB a,
        in float3 originB, in AABB b
    ) => a.Min.x + originA.x <= b.Max.x + originB.x &&
        a.Max.x + originA.x >= b.Min.x + originB.x &&

        a.Min.y + originA.y <= b.Max.y + originB.y &&
        a.Max.y + originA.y >= b.Min.y + originB.y &&

        a.Min.z + originA.z <= b.Max.z + originB.z &&
        a.Max.z + originA.z >= b.Min.z + originB.z;

    public static bool Intersect(
        in Collider a, in float3 positionA,
        in Cell b
    )
    {
        if (a.Type == ColliderType.Sphere)
        {
            return CircleAABBIntersect(
                positionA + a.Sphere.Offset, a.Sphere.Radius,
                Cell.ToWorld(b), Cell.AABB
            );
        }
        else if (a.Type == ColliderType.AABB)
        {
            return AABBAABBIntersect(
                positionA, a.AABB.AABB,
                Cell.ToWorld(b), Cell.AABB
            );
        }
        else
        {
            Debug.LogError(string.Format($"{DebugEx.AnyPrefix} Unsupported collision combination: `{{0}}` - Cell", a.Type));
            return false;
        }
    }

    public static bool Intersect(
        in Collider a, in float3 positionA,
        in Collider b, in float3 positionB
    )
    {
        if (a.Type == ColliderType.Sphere &&
            b.Type == ColliderType.Sphere)
        {
            return CircleCircleIntersect(
                positionA + a.Sphere.Offset, a.Sphere.Radius,
                positionB + b.Sphere.Offset, b.Sphere.Radius
            );
        }
        else if (a.Type == ColliderType.Sphere &&
                 b.Type == ColliderType.AABB)
        {
            return CircleAABBIntersect(
                positionA + a.Sphere.Offset, a.Sphere.Radius,
                positionB, b.AABB.AABB
            );
        }
        else if (a.Type == ColliderType.AABB &&
                 b.Type == ColliderType.Sphere)
        {
            return CircleAABBIntersect(
                positionB + b.Sphere.Offset, b.Sphere.Radius,
                positionA, a.AABB.AABB
            );
        }
        else if (a.Type == ColliderType.AABB &&
                 b.Type == ColliderType.AABB)
        {
            return AABBAABBIntersect(
                positionA, a.AABB.AABB,
                positionB, b.AABB.AABB
            );
        }
        else
        {
            Debug.LogError(string.Format($"{DebugEx.AnyPrefix} Unsupported collision combination: `{{0}}` - `{{1}}`", a.Type, b.Type));
            return false;
        }
    }

    #endregion

}
