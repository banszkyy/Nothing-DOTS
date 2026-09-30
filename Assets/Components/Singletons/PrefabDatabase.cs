using Unity.Entities;

public struct PrefabDatabase : IComponentData
{
    public Entity Player;
    public Entity VirtualPlayer;
    public Entity CoreComputer;
    public Entity Builder;
    public Entity Resource;
}
