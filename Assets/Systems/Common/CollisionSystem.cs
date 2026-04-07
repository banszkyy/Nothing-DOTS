#if UNITY_EDITOR && EDITOR_DEBUG
#define _DEBUG_COLLISIONS
#define _DEBUG_COLLIDERS
#endif

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateBefore(typeof(LocalToWorldSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
unsafe partial struct CollisionSystem : ISystem
{
    [BurstCompile]
    public static void Debug(
        in Collider collider, in float3 offset,
        in Color color, float duration = 0f, bool depthTest = true)
    {
#if DEBUG_COLLIDERS
        switch (collider.Type)
        {
            case ColliderType.Sphere:
                if (collider.Sphere.Radius == 0f)
                {
                    DebugEx.DrawPoint(offset + collider.Sphere.Offset, 1f, color, duration, depthTest);
                }
                else
                {
                    DebugEx.DrawSphere(offset + collider.Sphere.Offset, collider.Sphere.Radius, color, duration, depthTest);
                }
                break;
            case ColliderType.AABB:
                AABB aabb = collider.AABB.AABB;
                aabb.Center += offset;
                DebugEx.DrawBox(aabb, color, duration, depthTest);
                break;
            default: throw new UnreachableException();
        }
#endif
    }

    [BurstCompile]
    public static bool Contains(
        in Collider collider, in float3 offset,
        in float3 point)
    {
        switch (collider.Type)
        {
            case ColliderType.Sphere:
                return math.distancesq(point, offset + collider.Sphere.Offset) <= collider.Sphere.Radius * collider.Sphere.Radius;
            case ColliderType.AABB:
                AABB aabb = collider.AABB.AABB;
                aabb.Center += offset;
                return aabb.Contains(point);
            default: throw new UnreachableException();
        }
    }

    /// <summary>
    /// <seealso href="https://github.com/xhacker/raycast/blob/master/raycast/sphere.cpp">Source</seealso> 
    /// </summary>
    [BurstCompile]
    static bool RaycastSphere(
        in float sphereRadius, in float3 sphereOffset,
        in Ray ray,
        out float distance)
    {
        float3 rayStartLocal = ray.Start - sphereOffset;

        float a = math.dot(ray.Direction, ray.Direction);
        float b = 2f * math.dot(ray.Direction, rayStartLocal);
        float c = math.dot(rayStartLocal, rayStartLocal);
        c -= sphereRadius * sphereRadius;

        float dt = b * b - 4f * a * c;

        if (dt < 0)
        {
            distance = default;
            return false;
        }

        distance = (-b - math.sqrt(dt)) / (a * 2f);
        return distance >= 0;
    }

    /// <summary>
    /// <seealso href="https://gamedev.stackexchange.com/a/18459">Source</seealso>
    /// </summary>
    [BurstCompile]
    static bool RaycastAABB(
        in AABB aabb,
        in Ray ray,
        out float distance)
    {
        // r.dir is unit direction vector of ray
        float3 dirfrac = 1f / ray.Direction;

        // lb is the corner of AABB with minimal coordinates - left bottom, rt is maximal corner
        // r.org is origin of ray
        float t1 = (aabb.Min.x - ray.Start.x) * dirfrac.x;
        float t2 = (aabb.Max.x - ray.Start.x) * dirfrac.x;
        float t3 = (aabb.Min.y - ray.Start.y) * dirfrac.y;
        float t4 = (aabb.Max.y - ray.Start.y) * dirfrac.y;
        float t5 = (aabb.Min.z - ray.Start.z) * dirfrac.z;
        float t6 = (aabb.Max.z - ray.Start.z) * dirfrac.z;

        float tmin = math.max(math.max(math.min(t1, t2), math.min(t3, t4)), math.min(t5, t6));
        float tmax = math.min(math.min(math.max(t1, t2), math.max(t3, t4)), math.max(t5, t6));

        // if tmax < 0, ray (line) is intersecting AABB, but the whole AABB is behind us
        if (tmax < 0)
        {
            distance = tmax;
            return false;
        }

        // if tmin > tmax, ray doesn't intersect AABB
        if (tmin > tmax)
        {
            distance = tmax;
            return false;
        }

        distance = tmin;
        return true;
    }

    [BurstCompile]
    public static bool Raycast(
        in Collider collider, in float3 offset,
        in Ray ray,
        out float distance)
    {
        switch (collider.Type)
        {
            case ColliderType.Sphere:
                return RaycastSphere(
                    collider.Sphere.Radius, offset,
                    ray,
                    out distance);
            case ColliderType.AABB:
                AABB aabb = collider.AABB.AABB;
                aabb.Center += offset;
                return RaycastAABB(
                    aabb,
                    ray,
                    out distance);
            default: throw new UnreachableException();
        }
    }

    ComponentLookup<LocalTransform> localTransformQ;

    void ISystem.OnCreate(ref SystemState state)
    {
        localTransformQ = state.GetComponentLookup<LocalTransform>(false);
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        localTransformQ.Update(ref state);

        var map = QuadrantSystem.GetMap(ref state);

        var enumerator = map.GetEnumerator();
        while (enumerator.MoveNext())
        {
            Cell p = new(enumerator.Current.Key);
            NativeList<QuadrantEntity> quadrant = enumerator.Current.Value;
            for (int i = 0; i < quadrant.Length; i++)
            {
                QuadrantEntity* a = &quadrant.GetUnsafePtr()[i];

                for (int j = i + 1; j < quadrant.Length; j++)
                {
                    QuadrantEntity* b = &quadrant.GetUnsafePtr()[j];
                    HandleCollision(a, b, false);
                }

                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        Cell otherP = new(p.x + x, p.y + y);
                        if (p == otherP) continue;
                        if (!map.TryGetValue(otherP.key, out NativeList<QuadrantEntity> otherQuadrant)) continue;
                        if (!Collision.Intersect(
                            a->Collider, a->Position,
                            Cell.Collider, Cell.ToWorld(otherP)))
                        { continue; }

                        for (int j = 0; j < otherQuadrant.Length; j++)
                        {
                            QuadrantEntity* b = &otherQuadrant.GetUnsafePtr()[j];
                            HandleCollision(a, b, true);
                        }
                    }
                }

                if (!a->ResolvedOffset.Equals(default))
                {
                    if (localTransformQ.HasComponent(a->Entity))
                    {
                        RefRW<LocalTransform> transform = localTransformQ.GetRefRW(a->Entity);
#if DEBUG_COLLISIONS
                        DebugEx.Label(transform.ValueRO.Position, a->ResolvedOffset.ToString());
                        DebugEx.DrawLine(transform.ValueRO.Position, transform.ValueRO.Position + a->ResolvedOffset, Color.green, 0.1f, false);
#endif
                        transform.ValueRW.Position += a->ResolvedOffset;
                    }
                    a->ResolvedOffset = default;
                }
            }
        }
        enumerator.Dispose();
    }

    static readonly ProfilerMarker _handleCollision = new("CollisionSystem.HandleCollision");
    [BurstCompile]
    static void HandleCollision(QuadrantEntity* a, QuadrantEntity* b, bool skipB)
    {
        if (skipB && a->Collider.IsStatic) return;
        if (a->Collider.IsStatic && b->Collider.IsStatic) return;
        if (!a->Collider.IsEnabled || !b->Collider.IsEnabled) return;

        // if (a->LastPosition.Equals(a->Position) &&
        //     b->LastPosition.Equals(b->Position))
        // { continue; }

        // a->LastPosition = a->Position;

        using ProfilerMarker.AutoScope _ = _handleCollision.Auto();

#if DEBUG_COLLISIONS
        DebugEx.DrawLine(a->Position, b->Position, Color.Lerp(Color.green, Color.white, 0.5f), 0.1f, false);
        DebugEx.DrawPoint(a->Position, 0.5f, Color.Lerp(Color.green, Color.white, 0.5f), 0.1f, false);
        if (!skipB)
        {
            DebugEx.DrawPoint(b->Position, 0.5f, Color.Lerp(Color.green, Color.white, 0.5f), 0.1f, false);
        }
#endif

        if (!Collision.Intersect(
            a->Collider, a->Position,
            b->Collider, b->Position,
            out float3 normal, out float depth
            ))
        {
#if DEBUG_COLLISIONS
            Debug(a->Collider, a->Position, Color.Lerp(Color.green, Color.white, 0.5f), 0.1f, false);
            if (!skipB)
            {
                Debug(b->Collider, b->Position, Color.Lerp(Color.green, Color.white, 0.5f), 0.1f, false);
            }
#endif

            return;
        }


#if DEBUG_COLLISIONS
        Debug(a->Collider, a->Position, Color.green, 0.1f, false);
        if (!skipB)
        {
            Debug(b->Collider, b->Position, Color.green, 0.1f, false);
        }
#endif


        normal.y = 0f;
        depth = math.clamp(depth, 0f, 0.1f);

        if (a->Collider.IsStatic)
        {
            if (!skipB)
            {
                float3 displaceB = normal * -depth;

                b->ResolvedOffset += displaceB;
                b->Position += displaceB;
            }
        }
        else if (b->Collider.IsStatic)
        {
            float3 displaceA = normal * depth;

            a->ResolvedOffset += displaceA;
            a->Position += displaceA;
        }
        else
        {
            float3 displaceA = normal * (depth * 0.5f);
            float3 displaceB = normal * (depth * -0.5f);

            a->ResolvedOffset += displaceA;
            a->Position += displaceA;

            if (!skipB)
            {
                b->ResolvedOffset += displaceB;
                b->Position += displaceB;
            }
        }
    }
}
