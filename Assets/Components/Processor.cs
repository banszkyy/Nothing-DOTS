using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LanguageCore.Runtime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

struct StatusLED<T> where T : struct, Enum
{
    [GhostField(SendData = false)] public Entity Entity;
    [GhostField] public T Status;

    public StatusLED(Entity entity)
    {
        Entity = entity;
        Status = default;
    }
}

struct BlinkingLED
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
    public bool ReceiveBlink()
    {
        if (LastBlinkedLocal != LastBlinkedNet)
        {
            LastBlinkedLocal = LastBlinkedNet;
            LastBlinked = Math.Max(LastBlinkedNet, MonoTime.Now);
            return true;
        }
        return false;
    }
}

[StructLayout(LayoutKind.Explicit)]
struct ProcessorMemory
{
    [FieldOffset(0)] public FixedBytes2048 Memory;
    [FieldOffset(Processor.MappedMemoryStart)] public MappedMemory MappedMemory;
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
struct ProcessorSource
{
    public UnsafeList<Instruction>.ReadOnly Code;
    public UnsafeList<UnitCommandDefinition>.ReadOnly UnitCommandDefinitions;
    public UnsafeList<ExternalFunctionScopedSync> GeneratedFunctions;
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
    public const int TotalMemorySize = 2048;
    public const int HeapSize = 512;
    public const int StackSize = 1024;

    public const int UserMemorySize = StackSize + HeapSize;
    public const int MappedMemoryStart = UserMemorySize;
    public const int MappedMemorySize = TotalMemorySize - UserMemorySize;

    [GhostField] public FileId SourceFile;
    public long CompiledSourceVersion;
    public ProcessorSource Source;
    public UnitAttributesPack Attributes;

    public int CyclesPerTick;
    public Registers Registers;
    public ProcessorMemory Memory;
    public HotFunctions HotFunctions;
    [GhostField] public int Crash;
    [GhostField] public Signal Signal;
    public bool SignalNotified;

    public FixedList128Bytes<BufferedUnitTransmission> IncomingTransmissions;
    public FixedList128Bytes<BufferedUnitTransmissionOutgoing> OutgoingTransmissions;
    public FixedList128Bytes<UnitCommandRequest> CommandQueue;

    public bool PendrivePlugRequested;
    public bool PendriveUnplugRequested;
    public (bool Write, Pendrive Pendrive, Entity Entity) PluggedPendrive;

    public bool IsKeyRequested;
    public FixedList128Bytes<char> InputKey;

    /// <summary>
    /// XZ direction in local space
    /// </summary>
    public float2 RadarRequest;
    public float3 RadarResponse;

    public ulong StdOutBufferCursor;
    public FixedString512Bytes StdOutBuffer;

    [GhostField] public StatusLED<ProcessorStatus> StatusLED;
    [GhostField] public BlinkingLED NetworkReceiveLED;
    [GhostField] public BlinkingLED NetworkSendLED;
    [GhostField] public BlinkingLED RadarLED;
    [GhostField] public BlinkingLED USBLED;
    [GhostField] public BlinkingLED CustomLED;
    public float3 USBPosition;
    public quaternion USBRotation;

    public static unsafe nint GetMemoryPtr(ref Processor processor) => (nint)Unsafe.AsPointer(ref processor.Memory);
}
