using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public static partial class Utils
{
    [BurstCompile]
    public static ref T GetSystem<T>(this WorldUnmanaged world) where T : unmanaged, ISystem => ref world.GetUnsafeSystemRef<T>(world.GetExistingUnmanagedSystem<T>());

    public static T GetSystem<T>(this World world) where T : ComponentSystemBase => world.GetExistingSystemManaged<T>();

    public static void GetAllComponents<T>(this GameObject o, List<T> result, bool includeInactive = false)
    {
        o.GetComponents(result);
        o.GetComponentsInChildren(includeInactive, result);
    }

    public static List<T> GetAllComponents<T>(this GameObject o, bool includeInactive = false)
    {
        List<T> result = new();
        o.GetAllComponents(result, includeInactive);
        return result;
    }

    public static unsafe Unity.Mathematics.Random GetRandom(this ref SystemState state)
    {
        float v = (float)state.WorldUnmanaged.Time.ElapsedTime;
        return new Unity.Mathematics.Random(*(uint*)&v);
    }

    public delegate void ModifyComponentCallback<T>(ref T component) where T : unmanaged, IComponentData;

    public static void ModifyComponent<T>(this EntityManager entityManager, Entity entity, ModifyComponentCallback<T> callback) where T : unmanaged, IComponentData
    {
        T component = entityManager.GetComponentData<T>(entity);
        callback(ref component);
        entityManager.SetComponentData(entity, component);
    }

    public static int IndexOf<T>(this IEnumerable<T> collection, Func<T, bool> predicate)
    {
        int i = 0;
        foreach (T item in collection)
        {
            if (predicate.Invoke(item)) return i;
            i++;
        }
        return -1;
    }

    [BurstCompile]
    public static unsafe void NextNonce(this ref Unity.Mathematics.Random random, byte* buffer, int length)
    {
        for (int i = 0; i < length; i++)
        {
            buffer[i] = random.NextAlphanumeric();
        }
    }

    [BurstCompile]
    public static byte NextByte(this ref Unity.Mathematics.Random random) => (byte)random.NextInt(byte.MinValue, byte.MaxValue);

    [BurstCompile]
    public static byte NextAlphanumeric(this ref Unity.Mathematics.Random random) => random.NextInt(0, 2) switch
    {
        0 => (byte)random.NextInt('a', 'z'),
        1 => (byte)random.NextInt('A', 'A'),
        2 => (byte)random.NextInt('0', '9'),
        _ => throw new UnreachableException(),
    };

    [BurstCompile]
    public static float Distance(in float3 point, in float3x2 line)
    {
        float a = math.distance(line.c0, line.c1);
        float b = math.distance(line.c0, point);
        float c = math.distance(line.c1, point);
        float s = (a + b + c) / 2f;
        return 2f * math.sqrt(s * (s - a) * (s - b) * (s - c)) / a;
    }

    public static float3 ToEuler(this in float3 direction) => new(-Mathf.Asin(direction.y), Mathf.Atan2(direction.x, direction.z), 0f);

    public static bool RayIntersectsTriangle(
        in float3 rayOrigin,
        in float3 rayVector,
        in float3x3 triangle,
        out float t)
    {
        float3 edge1 = triangle.c1 - triangle.c0;
        float3 edge2 = triangle.c2 - triangle.c0;
        float3 rayCrossE2 = math.cross(rayVector, edge2);
        float det = math.dot(edge1, rayCrossE2);

        if (det is > -float.Epsilon and < float.Epsilon)
        {
            t = default;
            return false;
        }

        float invDet = 1f / det;
        float3 s = rayOrigin - triangle.c0;
        float u = invDet * math.dot(s, rayCrossE2);

        if ((u < 0 && math.abs(u) > float.Epsilon) || (u > 1 && math.abs(u - 1) > float.Epsilon))
        {
            t = default;
            return false;
        }

        float3 sCrossE1 = math.cross(s, edge1);
        float v = invDet * math.dot(rayVector, sCrossE1);

        if ((v < 0 && math.abs(v) > float.Epsilon) || (u + v > 1 && math.abs(u + v - 1) > float.Epsilon))
        {
            t = default;
            return false;
        }

        t = invDet * math.dot(edge2, sCrossE1);
        return t > float.Epsilon;
    }
}
