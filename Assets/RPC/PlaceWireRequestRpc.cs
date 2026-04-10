using Unity.Burst;
using Unity.NetCode;

[BurstCompile]
public struct PlaceWireRequestRpc : IRpcCommand
{
    public required SpawnedGhost EntityA;
    public required SpawnedGhost EntityB;
    public required byte PortA;
    public required byte PortB;
    public required bool IsRemove;
}
