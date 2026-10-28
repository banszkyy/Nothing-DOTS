using Unity.Burst;
using Unity.NetCode;

public enum ProcessorCommand : byte
{
    Reset,
    HaltContinue,
    Key,
}

[BurstCompile]
public struct ProcessorCommandRequestRpc : IRpcCommand
{
    public required SpawnedGhost Entity;
    public required ProcessorCommand Command;
    public required ushort Data;
}
