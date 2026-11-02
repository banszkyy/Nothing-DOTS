using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[InternalBufferCapacity(0)]
public struct ProcessorMemoryBuffer : IBufferElementData
{
    public byte _v;
}
