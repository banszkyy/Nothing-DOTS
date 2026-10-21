using Unity.NetCode;

struct ToggleSourceLockRequestRpc : IRpcCommand
{
    public required SpawnedGhost Entity;
}

