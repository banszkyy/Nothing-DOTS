using Unity.Burst;
using Unity.Collections;
using Unity.NetCode;

[BurstCompile]
public struct TerminalDataRpc : IRpcCommand
{
    public required SpawnedGhost Entity;
    public required FixedString64Bytes Data;
    public required ulong Offset;
}
