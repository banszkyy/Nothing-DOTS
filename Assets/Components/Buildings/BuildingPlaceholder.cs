using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

public struct BuildingPlaceholder : IComponentData
{
    public FixedString32Bytes BuildingName;
    public Entity BuildingPrefab;
    [GhostField(Quantization = 10)] public float TotalProgress;
    [GhostField(Quantization = 10)] public float CurrentProgress;
    public FileId Source;
}
