using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public struct QuadrantEntity
{
    public readonly Entity Entity;
    public readonly Collider Collider;
    public float3 Position;
    public float3 ResolvedOffset;
    public uint Key;
    public uint Layer;

    public QuadrantEntity(
        Entity entity,
        Collider collider,
        float3 position,
        uint key,
        uint layer)
    {
        Entity = entity;
        Collider = collider;
        Position = position;
        ResolvedOffset = default;
        Key = key;
        Layer = layer;
    }

    [BurstCompile] public override readonly int GetHashCode() => Entity.GetHashCode();
}

[BurstCompile]
[StructLayout(LayoutKind.Explicit)]
public struct Cell : IEquatable<Cell>
{
    public const int Size = 20;

    [FieldOffset(0)] public uint key;

    [FieldOffset(0)] public short x;
    [FieldOffset(2)] public short y;

    public static readonly Collider Collider = new AABBCollider(true, new AABB()
    {
        Center = new float3(Size / 2f, Size / 2f, Size / 2f),
        Extents = new float3(Size / 2f, Size / 2f, Size / 2f),
    });

    public Cell(uint key)
    {
        this.key = key;
    }

    public Cell(short x, short y)
    {
        this.x = x;
        this.y = y;
    }

    public Cell(int x, int y)
    {
        this.x = (short)math.clamp(x, short.MinValue, short.MaxValue);
        this.y = (short)math.clamp(y, short.MinValue, short.MaxValue);
    }

    public static Cell operator +(Cell a, Cell b) => new(a.x + b.x, a.y + b.y);
    public static Cell operator -(Cell a, Cell b) => new(a.x - b.x, a.y - b.y);

    public static bool operator ==(Cell a, Cell b) => a.key == b.key;
    public static bool operator !=(Cell a, Cell b) => a.key != b.key;

    [BurstCompile] public override readonly int GetHashCode() => unchecked((int)key);
    public override readonly bool Equals(object obj) => obj is Cell other && Equals(other);
    [BurstCompile] public readonly bool Equals(Cell other) => key == other.key;
    public override readonly string ToString() => $"Cell({x}, {y})";

    public static float2 TL(Cell cell) => new(
        cell.x * Size,
        cell.y * Size
    );
    public static float2 TR(Cell cell) => new(
        cell.x * Size + Size,
        cell.y * Size
    );
    public static float2 BL(Cell cell) => new(
        cell.x * Size,
        cell.y * Size + Size
    );
    public static float2 BR(Cell cell) => new(
        cell.x * Size + Size,
        cell.y * Size + Size
    );

    public static Cell ToGrid(float3 worldPosition)
    {
        if (worldPosition.x < 0f) worldPosition.x += -Size;
        if (worldPosition.z < 0f) worldPosition.z += -Size;
        return new(
            (int)(worldPosition.x / Size),
            (int)(worldPosition.z / Size)
        );
    }

    [BurstCompile]
    public static void ToGrid(in float3 worldPosition, out Cell position)
    {
        float3 fixedWorldPosition = worldPosition;
        if (worldPosition.x < 0f) fixedWorldPosition.x += -Size;
        if (worldPosition.z < 0f) fixedWorldPosition.z += -Size;
        position = new(
            (int)(fixedWorldPosition.x / Size),
            (int)(fixedWorldPosition.z / Size)
        );
    }

    [BurstCompile]
    public static void ToGrid(in float2 worldPosition, out Cell position)
    {
        float2 fixedWorldPosition = worldPosition;
        if (worldPosition.x < 0f) fixedWorldPosition.x += -Size;
        if (worldPosition.y < 0f) fixedWorldPosition.y += -Size;
        position = new(
            (int)(fixedWorldPosition.x / Size),
            (int)(fixedWorldPosition.y / Size)
        );
    }

    public static float2 ToGridF(float3 worldPosition) => new(
        math.clamp(worldPosition.x / Size, short.MinValue, short.MaxValue),
        math.clamp(worldPosition.z / Size, short.MinValue, short.MaxValue)
    );

    [BurstCompile]
    public static void ToGridF(in float3 worldPosition, out float2 position)
    {
        position = new(
            math.clamp(worldPosition.x / Size, short.MinValue, short.MaxValue),
            math.clamp(worldPosition.z / Size, short.MinValue, short.MaxValue)
        );
    }

    [BurstCompile]
    public static void ToGridF(in float2 worldPosition, out float2 position)
    {
        position = new(
            math.clamp(worldPosition.x / Size, short.MinValue, short.MaxValue),
            math.clamp(worldPosition.y / Size, short.MinValue, short.MaxValue)
        );
    }

    public static float3 ToWorld(Cell position) => new(
        position.x * Size,
        0f,
        position.y * Size
    );

    [BurstCompile]
    public static void ToWorld(in Cell position, out float3 worldPosition)
    {
        worldPosition = new(
            position.x * Size,
            0f,
            position.y * Size
        );
    }

    public static Color Color(uint key)
    {
        if (key == uint.MaxValue) return UnityEngine.Color.white;
        var random = Unity.Mathematics.Random.CreateFromIndex(key);
        var c = random.NextFloat3();
        return new Color(c.x, c.y, c.z);
    }

    public static void Draw(Cell cell, float duration = 0.1f)
    {
#if UNITY_EDITOR && EDITOR_DEBUG
        float3 start = ToWorld(cell) + new float3(0f, 1f, 0f);
        float3 end = start + new float3(Size, 0f, Size);
        DebugEx.DrawRectangle(start, end, Color(cell.key), duration);
#endif
    }
}

[BurstCompile]
public readonly struct Hit
{
    public readonly QuadrantEntity Entity;
    public readonly float Distance;

    public Hit(QuadrantEntity entity, float distance)
    {
        Entity = entity;
        Distance = distance;
    }
}

[BurstCompile]
public partial struct QuadrantSystem : ISystem
{
    NativeParallelHashMap<uint, NativeList<QuadrantEntity>> HashMap;

    public static NativeParallelHashMap<uint, NativeList<QuadrantEntity>>.ReadOnly GetMap(ref SystemState state) => GetMap(state.WorldUnmanaged);
    public static NativeParallelHashMap<uint, NativeList<QuadrantEntity>>.ReadOnly GetMap(in WorldUnmanaged world)
    {
        QuadrantSystem system = world.GetSystem<QuadrantSystem>();
        return system.HashMap.AsReadOnly();
    }

    [BurstCompile]
    void ISystem.OnCreate(ref SystemState state)
    {
        HashMap = new(128, Allocator.Persistent);
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        foreach (KeyValue<uint, NativeList<QuadrantEntity>> cell in HashMap)
        {
            for (int i = 0; i < cell.Value.Length; i++)
            {
                if (!state.EntityManager.Exists(cell.Value[i].Entity))
                {
                    cell.Value.RemoveAtSwapBack(i);
                    i--;
                }
                else
                {
                    CollisionSystem.Debug(cell.Value[i].Collider, cell.Value[i].Position, Color.lightGreen);
                }
            }
        }

        foreach (var (inQuadrant, collider, transform, entity) in
            SystemAPI.Query<RefRW<QuadrantEntityIdentifier>, RefRO<Collider>, RefRO<LocalToWorld>>()
            .WithEntityAccess())
        {
            Cell.ToGrid(transform.ValueRO.Position, out Cell cell);

            if (inQuadrant.ValueRO.Added)
            {
                NativeList<QuadrantEntity> list = HashMap[inQuadrant.ValueRO.Key];

                if (inQuadrant.ValueRO.Key == cell.key)
                {
                    for (int i = 0; i < list.Length; i++)
                    {
                        if (list[i].Entity != entity) continue;
                        list[i] = list[i] with { Position = transform.ValueRO.Position };
                        break;
                    }
                    continue;
                }
                else
                {
                    for (int i = 0; i < list.Length; i++)
                    {
                        if (list[i].Entity != entity) continue;
                        list.RemoveAtSwapBack(i);
                        break;
                    }
                }
            }

            inQuadrant.ValueRW.Added = true;
            inQuadrant.ValueRW.Key = cell.key;

            if (!HashMap.ContainsKey(cell.key))
            { HashMap.Add(cell.key, new(32, Allocator.Persistent)); }

            HashMap[cell.key].Add(new QuadrantEntity(
                entity,
                collider.ValueRO,
                transform.ValueRO.Position,
                cell.key,
                inQuadrant.ValueRO.Layer));
        }
    }
}
