using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
public struct BufferedProducingUnit : IBufferElementData
{
    [GhostField(SendData = false)] public Entity Prefab;
    [GhostField] public FixedString32Bytes Name;
    [GhostField(Quantization = 100)] public float ProductionTime;
    [GhostField] public FileId Source;
}
