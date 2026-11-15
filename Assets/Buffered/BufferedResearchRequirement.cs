using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
public struct BufferedResearchRequirement : IBufferElementData
{
    public required FixedString64Bytes Name;
}
