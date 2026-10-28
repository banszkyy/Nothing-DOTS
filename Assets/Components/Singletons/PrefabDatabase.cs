using Unity.Entities;

public struct PrefabDatabase : IComponentData
{
    public Entity Player;
    public Entity VirtualPlayer;
    public Entity PlayerInterface;
    public Entity Builder;
    public Entity Resource;
}
