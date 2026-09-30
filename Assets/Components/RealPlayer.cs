using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

public enum PlayerConnectionState : byte
{
    Connected,
    Local,
    Server,
    Disconnected,
    Virtual,
}

public struct RealPlayer : IComponentData
{
    public Entity Connection;
    [GhostField] public int ConnectionId;
    [GhostField] public PlayerConnectionState ConnectionState;
    public float3 Position;
    public long PingRequested;
    public long PingResponded;
    public int Ping;
}
