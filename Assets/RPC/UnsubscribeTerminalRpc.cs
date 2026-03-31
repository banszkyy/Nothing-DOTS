using Unity.Burst;
using Unity.NetCode;

[BurstCompile]
public struct UnsubscribeTerminalRpc : IRpcCommand
{
    public required SpawnedGhost Entity;
}
