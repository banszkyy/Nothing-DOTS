using Unity.NetCode;

struct StartDebugRequestRpc : IRpcCommand
{
    public required SpawnedGhost Entity;
}

