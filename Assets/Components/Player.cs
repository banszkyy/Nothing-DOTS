using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

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
    [GhostField] public int Team;
    [GhostField] public float Resources;
    [GhostField] public FixedString32Bytes Nickname;
    [GhostField] public GameOutcome Outcome;
    [GhostField] public bool InCreative;
    [GhostField] public bool IsAdmin;
    public bool IsCoreComputerSpawned;
    public Guid Guid;
}
