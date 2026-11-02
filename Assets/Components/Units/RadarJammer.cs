using Unity.Entities;
using Unity.NetCode;

public struct RadarJammer : IComponentData
{
    public float Radius;
    [GhostField] public bool IsActive;
}
