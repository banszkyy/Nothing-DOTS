using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[StructLayout(LayoutKind.Explicit)]
public struct IncomingUnitTransmissionMetadata
{
    [FieldOffset(0)] public required bool IsWireless;
    [FieldOffset(1)] public IncomingWirelessUnitTransmissionMetadata Wireless;
    [FieldOffset(1)] public IncomingWiredUnitTransmissionMetadata Wired;
}

[BurstCompile]
public struct IncomingWirelessUnitTransmissionMetadata
{
    public required float3 Direction;
    public required sbyte MarginDb;
}

[BurstCompile]
public struct IncomingWiredUnitTransmissionMetadata
{
    public required byte Port;
}

[BurstCompile]
public struct BufferedUnitTransmission : IBufferElementData
{
    public required FixedList32Bytes<byte> Data;
    public required IncomingUnitTransmissionMetadata Metadata;
}
