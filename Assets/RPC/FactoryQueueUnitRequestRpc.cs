using Unity.Burst;
using Unity.Collections;
using Unity.NetCode;

[BurstCompile]
public struct FactoryQueueUnitRequestRpc : IRpcCommand
{
    public required SpawnedGhost Entity;
    public required FixedString32Bytes Unit;
    public required FixedString64Bytes Source;
}
