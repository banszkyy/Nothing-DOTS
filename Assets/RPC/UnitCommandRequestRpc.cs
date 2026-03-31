using Unity.NetCode;

public struct UnitCommandRequestRpc : IRpcCommand
{
    public required SpawnedGhost Entity;
    public required int CommandId;
    public required UnitCommandArguments Arguments;
}
