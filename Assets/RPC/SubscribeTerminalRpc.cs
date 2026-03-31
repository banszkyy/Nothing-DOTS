using Unity.Burst;
using Unity.NetCode;

[BurstCompile]
public struct SubscribeTerminalRpc : IRpcCommand
{
    public required SpawnedGhost Entity;
    public required ulong Offset;
}
