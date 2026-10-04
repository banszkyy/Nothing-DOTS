using Unity.Entities;

struct VirtualRpc : IComponentData
{
    public required int PlayerIndex;
    public required int Team;
}
