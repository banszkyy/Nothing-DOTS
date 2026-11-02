using Unity.Entities;
using Unity.NetCode;

public struct RadioJammer : IComponentData
{
    public float EirpDbm;
    [GhostField] public bool IsActive;
}
