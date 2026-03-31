using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

public enum PlayerConnectionState : byte
{
    Connected,
    Local,
    Server,
    Disconnected,
}

public enum GameOutcome : byte
{
    None,
    Won,
    Lost,
}

[BurstCompile]
public struct Player : IComponentData
{
    public const int UnassignedTeam = -1;

    [GhostField] public int ConnectionId;
    [GhostField] public PlayerConnectionState ConnectionState;
    [GhostField] public int Team;
    [GhostField] public float Resources;
    [GhostField] public FixedString32Bytes Nickname;
    [GhostField] public GameOutcome Outcome;
    [GhostField] public bool InCreative;
    public bool IsCoreComputerSpawned;
    public Guid Guid;
    public float3 Position;
    public long PingRequested;
    public long PingResponded;
    public int Ping;
}
