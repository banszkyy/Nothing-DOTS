using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
public struct BufferedVirtualPlayerSettings : IBufferElementData
{
    public required FixedString32Bytes Nickname;
}
