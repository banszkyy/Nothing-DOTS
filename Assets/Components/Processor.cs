using System;
using LanguageCore.Runtime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Rendering;

[MaterialProperty("_LedBlink")] public struct LedBlinkProp : IComponentData { public float4 Value; }
[MaterialProperty("_LedStatus")] public struct LedStatusProp : IComponentData { public float4 Value; }
public struct LedGlowLink : IComponentData { public Entity Glow; }

public struct StatusLED<T> where T : struct, Enum
{
    [GhostField(SendData = false)] public Entity Entity;
    [GhostField] public T Status;

    public StatusLED(Entity entity)
    {
        Entity = entity;
        Status = default;
    }
}

public struct BlinkingLED
{
    [GhostField(SendData = false)] public Entity Entity;
    [GhostField(SendData = false)] public float LastBlinked;
    [GhostField(SendData = false)] public uint LastBlinkedLocal;
    [GhostField] public uint LastBlinkedNet;

    public BlinkingLED(Entity led)
    {
        Entity = led;
    }

    public void Blink() => LastBlinkedNet = unchecked(LastBlinkedNet + 1);
    public bool ReceiveBlink(float now, float flashTime)
    {
        if (LastBlinkedLocal != LastBlinkedNet)
        {
            LastBlinkedLocal = LastBlinkedNet;
            LastBlinked = now;
            return true;
        }

        return now - LastBlinked < flashTime;
    }
}

public struct BlinkingStatusLED
{
    [GhostField(SendData = false)] public Entity Entity;
    [GhostField(SendData = false)] public float LastBlinked;
    [GhostField(SendData = false)] public uint LastBlinkedLocal;
    [GhostField] public byte Status;
    [GhostField] public uint LastBlinkedNet;

    public BlinkingStatusLED(Entity led)
    {
        Entity = led;
    }

    public void Blink(byte status)
    {
        LastBlinkedNet = unchecked(LastBlinkedNet + 1);
        Status = status;
    }

    public bool ReceiveBlink(float now, float flashTime, out byte status)
    {
        if (LastBlinkedLocal != LastBlinkedNet)
        {
            LastBlinkedLocal = LastBlinkedNet;
            LastBlinked = now;
            status = Status;
            return true;
        }

        if (now - LastBlinked < flashTime)
        {
            status = Status;
            return true;
        }

        status = default;
        return false;
    }
}

readonly struct AttributeMeta : IEquatable<AttributeMeta>
{
    public readonly byte Offset;
    public readonly byte Size;

    public AttributeMeta(byte offset, byte size)
    {
        Offset = offset;
        Size = size;
    }

    public override bool Equals(object? obj) => obj is AttributeMeta meta && Equals(meta);
    public bool Equals(AttributeMeta other) => Offset == other.Offset && Size == other.Size;
    public override int GetHashCode() => HashCode.Combine(Offset, Size);

    public static bool operator ==(AttributeMeta left, AttributeMeta right) => left.Equals(right);
    public static bool operator !=(AttributeMeta left, AttributeMeta right) => !left.Equals(right);
}

[ChunkSerializable]
struct UnitAttributesPack
{
    public UnsafeList<AttributeMeta>.ReadOnly Fields;
    public UnsafeList<byte>.ReadOnly Data;
}

[ChunkSerializable]
struct ProcessorSource : IEquatable<ProcessorSource>
{
    public UnsafeList<Instruction>.ReadOnly Code;
    public UnsafeList<UnitCommandDefinition>.ReadOnly UnitCommandDefinitions;
    public UnsafeList<ExternalFunctionScopedSync> GeneratedFunctions;

    public override bool Equals(object? obj) => obj is ProcessorSource other && Equals(other);
    public readonly unsafe bool Equals(ProcessorSource other) =>
        Code.Ptr == other.Code.Ptr &&
        UnitCommandDefinitions.Ptr == other.UnitCommandDefinitions.Ptr &&
        GeneratedFunctions.Ptr == other.GeneratedFunctions.Ptr;

    public static bool operator ==(ProcessorSource left, ProcessorSource right) => left.Equals(right);
    public static bool operator !=(ProcessorSource left, ProcessorSource right) => !left.Equals(right);

    public override int GetHashCode() => Code.GetHashCode();
}

public enum ProcessorStatus : byte
{
    Off,
    Running,
    Error,
    Halted,
}

struct Processor : IComponentData
{
    [GhostField] public FileId SourceFile;
    public long CompiledSourceVersion;
    public ProcessorSource Source;
    public UnitAttributesPack Attributes;
    [GhostField] public ProcessorDebugSystemServer.DebugContext DebugContext;
    [GhostField] public bool IsVersionLocked;

    public int CyclesPerTick;
    public int MemorySize;
    public float EmissionPower;
    public Registers Registers;
    public HotFunctions HotFunctions;
    [GhostField] public int Crash;
    [GhostField] public Signal Signal;
    public bool SignalNotified;

    public FixedList128Bytes<UnitCommandRequest> CommandQueue;

    public bool IsKeyRequested;
    public FixedList128Bytes<byte> InputKey;

    /// <summary>
    /// XZ direction in local space
    /// </summary>
    public float2 RadarRequest;
    public RadarResponse RadarResponse;

    public ulong StdOutBufferCursor;
    public FixedList512Bytes<byte> StdOutBuffer;

    [GhostField] public StatusLED<ProcessorStatus> StatusLED;
    [GhostField] public BlinkingStatusLED NetworkReceiveLED;
    [GhostField] public BlinkingLED NetworkSendLED;
    [GhostField] public BlinkingLED USBLED;
    [GhostField] public BlinkingLED CustomLED;
    public float3 USBPosition;
    public quaternion USBRotation;
}
