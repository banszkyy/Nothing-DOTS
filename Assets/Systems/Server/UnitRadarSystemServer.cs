#if UNITY_EDITOR && EDITOR_DEBUG
#define _DEBUG_LINES
#endif

using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct UnitRadarSystemServer : ISystem
{
#if DEBUG_LINES
    const float DebugDuration = 3f;
#endif

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        var map = QuadrantSystem.GetMap(ref state);

        foreach (var (processor, localTransform, transform) in
            SystemAPI.Query<RefRW<Processor>, RefRO<LocalTransform>, RefRO<LocalToWorld>>()
            .WithAll<Radar>())
        {
            float2 direction = processor.ValueRO.RadarRequest;
            if (direction.Equals(default)) continue;

            float3 direction3 = localTransform.ValueRO.TransformDirection(new float3(direction.x, 0f, direction.y));
            float h = math.sqrt(1f - (direction3.y * direction3.y));
            direction = new float2(direction3.x, direction3.z) / h;
            float2 position = new(transform.ValueRO.Position.x, transform.ValueRO.Position.z);

            processor.ValueRW.RadarRequest = default;

            processor.ValueRW.RadarLED.Blink();

            const float offset = 0f;

            RadarRay ray = new(transform.ValueRO.Position + (direction3 * offset), direction, math.max((Radar.RadarRadius * h) - offset, 0f), Layers.BuildingOrUnit);

#if DEBUG_LINES
            DebugEx.DrawLine(ray.Start, new float3(ray.End.x, ray.Start.y, ray.End.y), Color.white, DebugDuration, false);
#endif

            if (!RadarCast(map, ray, out RadarHit hit))
            {
                processor.ValueRW.RadarResponse = new RadarResponse(float.NaN, 0, 0, 0, 0);
                return;
            }

            float distance = math.distance(hit.Point, ray.Start) + offset;

            if (distance >= Radar.RadarRadius)
            {
                processor.ValueRW.RadarResponse = new RadarResponse(float.NaN, 0, 0, 0, 0);
                return;
            }

#if DEBUG_LINES
            DebugEx.DrawPoint(hit.Point, 1f, Color.white, DebugDuration, false);
#endif

            byte speedSignal = 0;
            byte clutter = 67;
            byte fingerprint = 0;
            byte meta = 0;

            if (SystemAPI.TryGetComponent(hit.Entity.Entity, out Vehicle hitVehicle))
            {
                speedSignal = (byte)Math.Clamp(Math.Abs(hitVehicle.Speed) * 13, byte.MinValue, byte.MaxValue);
                clutter = 0;
            }

            if (SystemAPI.TryGetComponent(hit.Entity.Entity, out Processor hitProcessor))
            {
                clutter = 0;
                fingerprint |= 0b_00000001;
                if (hitProcessor.SourceFile != default && hitProcessor.Signal == LanguageCore.Runtime.Signal.None)
                {
                    meta |= 0b_00000001;
                }
            }

            if (SystemAPI.HasComponent<CombatTurret>(hit.Entity.Entity))
            {
                clutter = 0;
                meta |= 0b_00000010;
            }

            processor.ValueRW.RadarResponse = new RadarResponse(localTransform.ValueRO.InverseTransformPoint(hit.Point), speedSignal, clutter, fingerprint, meta);
        }
    }

    [BurstCompile]
    public readonly struct RadarRay
    {
        public readonly float3 Start;
        public readonly float2 End;
        public readonly float MaxDistance;
        public readonly float2 Direction;
        public readonly uint Layer;
        [MarshalAs(UnmanagedType.U1)]
        public readonly bool ExcludeContainingBodies;

        public RadarRay(float3 start, float2 direction, float maxDistance, uint layer, bool excludeContainingBodies = true)
        {
            Start = start;
            End = new float2(start.x, start.z) + (direction * maxDistance);
            MaxDistance = maxDistance;
            Direction = direction;
            Layer = layer;
            ExcludeContainingBodies = excludeContainingBodies;
        }
    }

    [BurstCompile]
    public readonly struct RadarHit
    {
        public readonly QuadrantEntity Entity;
        public readonly float3 Point;

        public RadarHit(QuadrantEntity entity, float3 point)
        {
            Entity = entity;
            Point = point;
        }
    }

    [BurstCompile]
    static bool RadarCast(
        in NativeList<QuadrantEntity> entities,
        in RadarRay ray,
        out RadarHit hit)
    {
        for (int i = 0; i < entities.Length; i++)
        {
            if ((entities[i].Layer & ray.Layer) == 0u) continue;

            if (ray.ExcludeContainingBodies && CollisionSystem.Contains(
                    entities[i].Collider,
                    entities[i].Position,
                    ray.Start))
            { continue; }

            float targetSize = entities[i].Collider.Type switch
            {
                ColliderType.Sphere => entities[i].Collider.Sphere.Radius * 2f,
                ColliderType.AABB => math.length(entities[i].Collider.AABB.AABB.Size),
                _ => default,
            };
            if (targetSize < .7) continue;

            float3 target = entities[i].Position + entities[i].Collider.Type switch
            {
                ColliderType.Sphere => entities[i].Collider.Sphere.Offset,
                ColliderType.AABB => entities[i].Collider.AABB.AABB.Center,
                _ => default,
            };

            float pitch = math.asin(math.normalize(target - ray.Start).y);
            float3 direction = new(
                ray.Direction.x * math.cos(pitch),
                math.sin(pitch),
                ray.Direction.y * math.cos(pitch)
            );

            Ray ray3 = new(
                ray.Start,
                direction,
                ray.MaxDistance,
                ray.Layer,
                ray.ExcludeContainingBodies
            );

#if DEBUG_LINES
            DebugEx.DrawLine(ray3.Start, ray3.End, Color.magenta, DebugDuration);
#endif

            if (!CollisionSystem.Raycast(
                entities[i].Collider,
                entities[i].Position,
                ray3,
                out float distance))
            { continue; }

            float3 p = ray3.GetPoint(distance);

            if (distance > ray.MaxDistance)
            {
#if DEBUG_LINES
                //DebugEx.DrawPoint(p, 2f, Color.orange, DebugDuration, false);
#endif
                continue;
            }

            hit = new(entities[i], p);
            return true;
        }

        hit = default;
        return false;
    }

    /// <remarks>
    /// Source: javidx9
    /// </remarks>
    [BurstCompile]
    static bool RadarCast(
        in NativeParallelHashMap<uint, NativeList<QuadrantEntity>>.ReadOnly map,
        in RadarRay ray,
        out RadarHit hit)
    {
        if (ray.MaxDistance <= 0f) { hit = default; return false; }

        Cell.ToGridF(ray.Start, out float2 _start);
        Cell.ToGridF(ray.End, out float2 _end);
        float2 _dir = math.normalize(_end - _start);

        float2 rayUnitStepSize = new(
            math.sqrt(1f + (_dir.y / _dir.x) * (_dir.y / _dir.x)),
            math.sqrt(1f + (_dir.x / _dir.y) * (_dir.x / _dir.y))
        );

        Cell.ToGrid(ray.Start, out Cell mapCheck);
        float2 rayLength1D;
        Cell step;

        if (_dir.x < 0f)
        {
            step.x = -1;
            rayLength1D.x = (_start.x - mapCheck.x) * rayUnitStepSize.x;
        }
        else
        {
            step.x = 1;
            rayLength1D.x = (mapCheck.x + 1 - _start.x) * rayUnitStepSize.x;
        }

        if (_dir.y < 0f)
        {
            step.y = -1;
            rayLength1D.y = (_start.y - mapCheck.y) * rayUnitStepSize.y;
        }
        else
        {
            step.y = 1;
            rayLength1D.y = (mapCheck.y + 1 - _start.y) * rayUnitStepSize.y;
        }

        float maxDistance = math.distance(_start, _end);
        float distance = 0f;

        while (distance < maxDistance)
        {
            if (map.TryGetValue(mapCheck.key, out NativeList<QuadrantEntity> cell) &&
                RadarCast(cell, ray, out hit))
            { return true; }

            if (rayLength1D.x < rayLength1D.y)
            {
                mapCheck.x += step.x;
                distance = rayLength1D.x;
                rayLength1D.x += rayUnitStepSize.x;
            }
            else
            {
                mapCheck.y += step.y;
                distance = rayLength1D.y;
                rayLength1D.y += rayUnitStepSize.y;
            }
        }

        hit = default;
        return false;
    }
}
