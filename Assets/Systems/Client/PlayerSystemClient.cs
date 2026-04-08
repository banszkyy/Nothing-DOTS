using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct PlayerSystemClient : ISystem
{
    const string SessionsDirectoryPath = "sessions";

    bool GuidRequestSent;
    bool SessionRequestSent;
    SessionStatusCode SessionStatus;
    Guid PlayerGuid;
    Guid ServerGuid;
    FixedString32Bytes Nickname;
    EntityQuery playersQ;
    EntityQuery connectionsQ;

    public static ref PlayerSystemClient GetInstance(in WorldUnmanaged world) => ref world.GetSystem<PlayerSystemClient>();

    void ISystem.OnCreate(ref SystemState state)
    {
        PlayerGuid = default;
        state.RequireForUpdate<NetworkStreamConnection>();
    }

    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (_, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<ServerGuidResponseRpc>>()
            .WithEntityAccess())
        {
            commandBuffer.DestroyEntity(entity);

            ServerGuid = Marshal.As<FixedBytes16, Guid>(command.ValueRO.Guid);
            GuidRequestSent = false;

            Debug.Log($"{DebugEx.ClientPrefix} Server guid: `{ServerGuid}`");
        }

        foreach (var (_, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<SessionResponseRpc>>()
            .WithEntityAccess())
        {
            commandBuffer.DestroyEntity(entity);

            SessionStatus = command.ValueRO.StatusCode;
            PlayerGuid = Marshal.As<FixedBytes16, Guid>(command.ValueRO.Guid);
            SessionRequestSent = false;

            switch (command.ValueRO.StatusCode)
            {
                case SessionStatusCode.AlreadyLoggedIn:
                case SessionStatusCode.OK:
                {
                    Nickname = command.ValueRO.Nickname;
                    Debug.Log($"{DebugEx.ClientPrefix} Successfully logged in ({command.ValueRO.StatusCode})\n  Guid: {PlayerGuid}\n  Nickname: {Nickname}");

                    if (!FindSavedSession(ServerGuid, out Guid savedPlayerGuid, out _) || savedPlayerGuid != PlayerGuid)
                    {
                        SaveSession(ServerGuid, PlayerGuid);
                    }

                    break;
                }
                case SessionStatusCode.InvalidGuid:
                {
                    Debug.Log($"{DebugEx.ClientPrefix} Invalid guid, resetting local player guid");

                    PlayerGuid = default;
                    break;
                }
                default: throw new UnreachableException();
            }
        }

        NetworkStreamConnection connection = SystemAPI.GetSingleton<NetworkStreamConnection>();

        if (connection.CurrentState != ConnectionState.State.Connected) return;

        if (TryGetLocalPlayer(ref state, out _)) return;

        if (ServerGuid == default)
        {
            if (GuidRequestSent) return;
            GuidRequestSent = true;

            Debug.Log($"{DebugEx.ClientPrefix} Requesting server guid");

            NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new ServerGuidRequestRpc());

            return;
        }

        if (SessionRequestSent) return;
        SessionRequestSent = true;

        if (PlayerGuid == default)
        {
            if (FindSavedSession(ServerGuid, out Guid savedPlayerGuid, out _) && SessionStatus != SessionStatusCode.InvalidGuid)
            {
                Debug.Log($"{DebugEx.ClientPrefix} No player found, logging in with saved session\nserver: {ServerGuid}\nplayer: {savedPlayerGuid}");

                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new SessionLoginRequestRpc()
                {
                    Guid = Marshal.As<Guid, FixedBytes16>(savedPlayerGuid),
                });
            }
            else
            {
                Debug.Log($"{DebugEx.ClientPrefix} No player found, registering");

                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new SessionRegisterRequestRpc()
                {
                    Nickname = Nickname,
                });
            }
        }
        else
        {
            Debug.Log($"{DebugEx.ClientPrefix} No player found, logging in with {PlayerGuid}");

            NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new SessionLoginRequestRpc()
            {
                Guid = Marshal.As<Guid, FixedBytes16>(PlayerGuid),
            });
        }
    }

    public bool TryGetLocalPlayer(ref SystemState state, out Player player)
    {
        if (state.WorldUnmanaged.IsLocal())
        {
            return SystemAPI.TryGetSingleton<Player>(out player);
        }

        if (!SystemAPI.TryGetSingleton(out NetworkId networkId))
        {
            player = default;
            return false;
        }

        foreach (var _player in
            SystemAPI.Query<RefRO<Player>>())
        {
            if (_player.ValueRO.ConnectionId != networkId.Value) continue;
            player = _player.ValueRO;
            return true;
        }

        player = default;
        return false;
    }

    public bool TryGetLocalPlayer(out Player player)
    {
        if (playersQ == default) playersQ = ConnectionManager.ClientOrDefaultWorld.EntityManager.CreateEntityQuery(typeof(Player));
        if (connectionsQ == default) connectionsQ = ConnectionManager.ClientOrDefaultWorld.EntityManager.CreateEntityQuery(typeof(NetworkId));

        if (ConnectionManager.ClientOrDefaultWorld.Unmanaged.IsLocal())
        {
            return playersQ.TryGetSingleton<Player>(out player);
        }

        if (!connectionsQ.TryGetSingleton(out NetworkId networkId))
        {
            player = default;
            return false;
        }

        using NativeArray<Player> players = playersQ.ToComponentDataArray<Player>(Allocator.Temp);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i].ConnectionId != networkId.Value) continue;
            player = players[i];
            return true;
        }

        player = default;
        return false;
    }

    public bool TryGetLocalPlayer(out Entity player)
    {
        if (playersQ == default) playersQ = ConnectionManager.ClientOrDefaultWorld.EntityManager.CreateEntityQuery(typeof(Player));
        if (connectionsQ == default) connectionsQ = ConnectionManager.ClientOrDefaultWorld.EntityManager.CreateEntityQuery(typeof(NetworkId));

        if (ConnectionManager.ClientOrDefaultWorld.Unmanaged.IsLocal())
        {
            return playersQ.TryGetSingletonEntity<Player>(out player);
        }

        if (!connectionsQ.TryGetSingleton(out NetworkId networkId))
        {
            player = default;
            return false;
        }

        using NativeArray<Entity> players = playersQ.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < players.Length; i++)
        {
            if (ConnectionManager.ClientOrDefaultWorld.EntityManager.GetComponentData<Player>(players[i]).ConnectionId != networkId.Value) continue;
            player = players[i];
            return true;
        }

        player = default;
        return false;
    }

    public void SetNickname(FixedString32Bytes nickname)
    {
        Nickname = nickname;

        if (ConnectionManager.ClientOrDefaultWorld.Unmanaged.IsLocal())
        {
            using EntityQuery playersQ = ConnectionManager.ClientOrDefaultWorld.EntityManager.CreateEntityQuery(typeof(Player));
            playersQ.GetSingletonRW<Player>().ValueRW.Nickname = nickname;
        }
    }

    static bool FindSavedSession(Guid serverGuid, out Guid playerGuid, [NotNullWhen(true)] out string? file)
    {
        playerGuid = default;
        file = default;

        if (!Directory.Exists(SessionsDirectoryPath)) return false;

        foreach (string _file in Directory.GetFiles(SessionsDirectoryPath))
        {
            if (LoadSession(_file, out Guid _serverGuid, out playerGuid) && _serverGuid == serverGuid)
            {
                file = _file;
                return true;
            }
        }

        return false;
    }

    static bool LoadSession(string file, out Guid serverGuid, out Guid playerGuid)
    {
        using FileBinaryReader reader = new(file);
        try
        {
            serverGuid = reader.ReadGuid();
            playerGuid = reader.ReadGuid();
            return reader.IsEOF;
        }
        catch
        {
            serverGuid = default;
            playerGuid = default;
            return false;
        }
    }

    static string GetNonceFromIndex(uint index)
    {
        const string Chars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        StringBuilder result = new();
        while (true)
        {
            if (index < Chars.Length)
            {
                result.Insert(0, Chars[(int)index]);
                return result.ToString();
            }
            else
            {
                int i = (int)(index % Chars.Length);
                result.Insert(0, Chars[i]);
                index /= (uint)Chars.Length;
            }
        }
    }

    static void SaveSession(Guid serverGuid, Guid playerGuid)
    {
        if (!Directory.Exists(SessionsDirectoryPath))
        {
            Directory.CreateDirectory(SessionsDirectoryPath);
        }

        uint counter = 1;
        string fileName;
        while (File.Exists(fileName = Path.Combine(SessionsDirectoryPath, $"{GetNonceFromIndex(counter)}.bin")))
        {
            counter++;
            if (counter == 0) throw new Exception($"Failed to generate a session file name");
        }

        using FileBinaryWriter writer = new(fileName);
        writer.Write(serverGuid);
        writer.Write(playerGuid);

        Debug.Log($"{DebugEx.ClientPrefix} Session saved to file \"{fileName}\"");
    }

    public void OnDisconnect()
    {
        if (playersQ != default) playersQ.Dispose();
        if (connectionsQ != default) connectionsQ.Dispose();
    }
}
