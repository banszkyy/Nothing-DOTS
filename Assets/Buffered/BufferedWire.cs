using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
public struct BufferedWire : IBufferElementData
{
    [GhostField(SendData = false)] public Entity EntityA;
    [GhostField(SendData = false)] public Entity EntityB;
    [GhostField] public byte PortA;
    [GhostField] public byte PortB;
    [GhostField] public SpawnedGhost GhostA;
    [GhostField] public SpawnedGhost GhostB;

    public readonly EntityPortIdentifier PortIdentifierA => new(EntityA, PortA);
    public readonly EntityPortIdentifier PortIdentifierB => new(EntityB, PortB);
}
