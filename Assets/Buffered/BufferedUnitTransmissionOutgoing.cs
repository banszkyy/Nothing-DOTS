using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[StructLayout(LayoutKind.Explicit)]
public struct OutgoingUnitTransmissionMetadata
{
    [FieldOffset(0)] public required bool IsWireless;
    [FieldOffset(1)] public OutgoingWirelessUnitTransmissionMetadata Wireless;
    [FieldOffset(1)] public OutgoingWiredUnitTransmissionMetadata Wired;
}

[BurstCompile]
public struct OutgoingWirelessUnitTransmissionMetadata
{
    public required float3 Source;
    /// <summary>
    /// Direction in local space
    /// </summary>
    public required float3 Direction;
    public required float Angle;
    public required float CosAngle;
}

[BurstCompile]
public struct OutgoingWiredUnitTransmissionMetadata
{
    public byte Port;
}

[BurstCompile]
public struct BufferedUnitTransmissionOutgoing : IBufferElementData
{
    public required FixedList32Bytes<byte> Data;
    public required OutgoingUnitTransmissionMetadata Metadata;
}
