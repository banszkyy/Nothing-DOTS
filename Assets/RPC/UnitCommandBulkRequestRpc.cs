using Unity.Collections;
using Unity.NetCode;

public struct UnitCommandBulkRequestRpc : IRpcCommand
{
    public required FixedList64Bytes<SpawnedGhost> Entities;
    public required int CommandId;
    public required UnitCommandArguments Arguments;
}
