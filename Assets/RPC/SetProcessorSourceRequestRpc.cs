using Unity.Burst;
using Unity.Collections;
using Unity.NetCode;

[BurstCompile]
public struct SetProcessorSourceRequestRpc : IRpcCommand
{
    public required FixedString128Bytes Source;
    public required SpawnedGhost Entity;
    public required bool IsHotReload;
}
