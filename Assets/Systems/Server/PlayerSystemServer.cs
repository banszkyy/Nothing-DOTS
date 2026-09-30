using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct PlayerSystemServer : ISystem
{
    int TeamCounter;
    public Guid ServerGuid;

    [BurstCompile]
    void ISystem.OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PrefabDatabase>();
        TeamCounter = 1;
    }

    void ISystem.OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonBuffer(out DynamicBuffer<BufferedSpawn> spawns, false)) return;
        if (!SystemAPI.TryGetSingletonBuffer(out DynamicBuffer<BufferedVirtualPlayerSettings> virtualPlayerSettings, false)) return;

        EntityCommandBuffer commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        PrefabDatabase prefabs = SystemAPI.GetSingleton<PrefabDatabase>();

        foreach (var (id, entity) in
            SystemAPI.Query<RefRO<NetworkId>>()
            .WithNone<InitializedClient>()
            .WithEntityAccess())
        {
            Debug.Log(string.Format($"{DebugEx.ServerPrefix} Client `{{0}}` initialized", id.ValueRO));
            commandBuffer.AddComponent<InitializedClient>(entity);
        }

        ReadOnlySpan<byte> bytes = stackalloc byte[16];

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<ServerGuidRequestRpc>>()
            .WithEntityAccess())
        {
            NetworkId source = request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO;
            commandBuffer.DestroyEntity(entity);

            Debug.Log(string.Format($"{DebugEx.ServerPrefix} Received guid request from client `{{0}}`", source));

            if (ServerGuid == default)
            {
                unsafe
                {
                    Unity.Mathematics.Random random = state.GetRandom();
                    ServerGuid = new Guid(random.NextInt(), (short)random.NextInt(short.MinValue, short.MaxValue), (short)random.NextInt(short.MinValue, short.MaxValue), random.NextByte(), random.NextByte(), random.NextByte(), random.NextByte(), random.NextByte(), random.NextByte(), random.NextByte(), random.NextByte());
                }
                Debug.Log($"{DebugEx.ServerPrefix} Server guid generated: {ServerGuid}");
            }

            NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new ServerGuidResponseRpc()
            {
                Guid = Marshal.As<Guid, FixedBytes16>(ref ServerGuid),
            }, request.ValueRO.SourceConnection);
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<SessionRegisterRequestRpc>>()
            .WithEntityAccess())
        {
            NetworkId source = request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO;
            commandBuffer.DestroyEntity(entity);

            Debug.Log($"{DebugEx.ServerPrefix} Received register request from client `{source}`\n  Nickname: \"{command.ValueRO.Nickname}\"");

            RealPlayer requestedPlayerR = default;
            Player requestedPlayer = default;
            Entity requestedPlayerE = default;

            foreach (var (player, playerR, playerE) in
                SystemAPI.Query<RefRO<Player>, RefRO<RealPlayer>>()
                .WithEntityAccess())
            {
                if (playerR.ValueRO.ConnectionId == source.Value)
                {
                    requestedPlayerE = playerE;
                    requestedPlayerR = playerR.ValueRO;
                    requestedPlayer = player.ValueRO;
                }
            }

            if (requestedPlayerE != default)
            {
                Debug.LogWarning($"{DebugEx.ServerPrefix} Already logged in");
                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new SessionResponseRpc()
                {
                    StatusCode = SessionStatusCode.AlreadyLoggedIn,
                    Nickname = requestedPlayer.Nickname,
                    Guid = default,
                }, request.ValueRO.SourceConnection);
            }
            else
            {
                Guid guid;
                unsafe
                {
                    byte* ptr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(bytes));
                    *(int*)(ptr + 0) = source.Value; // 4
                    *(double*)(ptr + 4) = SystemAPI.Time.ElapsedTime; // 8
                    *(uint*)(ptr + 12) = 0x69420; // 4
                    guid = new Guid(bytes);
                }

                Entity newPlayer = commandBuffer.Instantiate(prefabs.Player);
                commandBuffer.SetComponent<Player>(newPlayer, new()
                {
                    Team = Player.UnassignedTeam,
                    IsCoreComputerSpawned = false,
                    Guid = guid,
                    Nickname = command.ValueRO.Nickname,
                });
                commandBuffer.SetComponent<RealPlayer>(newPlayer, new()
                {
                    Connection = request.ValueRO.SourceConnection,
                    ConnectionId = source.Value,
                    ConnectionState = PlayerConnectionState.Connected,
                });

                Debug.Log($"{DebugEx.ServerPrefix} Player created\n  Nickname: \"{command.ValueRO.Nickname}\"\n  Guid: {guid}\n  ConnectionId: {source.Value}");
                ChatSystemServer.SendChatMessage(commandBuffer, state.WorldUnmanaged, $"Player {command.ValueRO.Nickname} connected", MonoTime.UnixSeconds);

                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new SessionResponseRpc()
                {
                    StatusCode = SessionStatusCode.OK,
                    Guid = Marshal.As<Guid, FixedBytes16>(ref guid),
                    Nickname = command.ValueRO.Nickname,
                }, request.ValueRO.SourceConnection);
            }
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<SessionLoginRequestRpc>>()
            .WithEntityAccess())
        {
            NetworkId source = request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO;
            commandBuffer.DestroyEntity(entity);

            FixedBytes16 guid = command.ValueRO.Guid;

            Debug.Log($"{DebugEx.ServerPrefix} Received login request from client `{source}`\n  Guid: {Marshal.As<FixedBytes16, Guid>(guid)}");

            bool exists = false;
            foreach (var (player, playerR) in
                SystemAPI.Query<RefRO<Player>, RefRW<RealPlayer>>())
            {
                if (player.ValueRO.Guid != Marshal.As<FixedBytes16, Guid>(guid)) continue;

                exists = true;
                bool loggedIn = playerR.ValueRO.ConnectionId != -1;
                if (!loggedIn)
                {
                    playerR.ValueRW.Connection = request.ValueRO.SourceConnection;
                    playerR.ValueRW.ConnectionId = source.Value;
                    playerR.ValueRW.ConnectionState = PlayerConnectionState.Connected;
                    ChatSystemServer.SendChatMessage(commandBuffer, state.WorldUnmanaged, $"Player {player.ValueRO.Nickname} reconnected", MonoTime.UnixSeconds);
                }

                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new SessionResponseRpc()
                {
                    StatusCode = loggedIn ? SessionStatusCode.AlreadyLoggedIn : SessionStatusCode.OK,
                    Guid = guid,
                    Nickname = player.ValueRO.Nickname,
                }, request.ValueRO.SourceConnection);
                break;
            }

            if (!exists)
            {
                Debug.Log($"{DebugEx.ServerPrefix} Player does not exists {Marshal.As<FixedBytes16, Guid>(guid)}");

                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new SessionResponseRpc()
                {
                    StatusCode = SessionStatusCode.InvalidGuid,
                    Guid = guid,
                    Nickname = default,
                }, request.ValueRO.SourceConnection);
            }
        }

        bool isAdminAssigned = false;

        foreach (var (player, playerR) in
            SystemAPI.Query<RefRW<Player>, RefRW<RealPlayer>>())
        {
            if (player.ValueRO.Team == Player.UnassignedTeam)
            {
                player.ValueRW.Team = TeamCounter++;
                Debug.Log($"{DebugEx.ServerPrefix} Assigning team {player.ValueRO.Team} to player \"{player.ValueRO.Nickname}\" ({playerR.ValueRO.ConnectionState} {playerR.ValueRO.ConnectionId})");
            }

            if (playerR.ValueRO.ConnectionState is not PlayerConnectionState.Connected and not PlayerConnectionState.Local) continue;

            bool found = false;

            if (playerR.ValueRO.ConnectionState == PlayerConnectionState.Connected)
            {
                foreach (var id in
                    SystemAPI.Query<RefRO<NetworkId>>()
                    .WithAll<InitializedClient>())
                {
                    if (id.ValueRO.Value == playerR.ValueRO.ConnectionId)
                    {
                        found = true;
                        break;
                    }
                }
            }
            else
            {
                found = true;
            }

            if (!found)
            {
                Debug.Log($"{DebugEx.ServerPrefix} Client {playerR.ValueRO.ConnectionId} disconnected");
                ChatSystemServer.SendChatMessage(commandBuffer, state.WorldUnmanaged, $"Player {player.ValueRO.Nickname} disconnected", MonoTime.UnixSeconds);

                playerR.ValueRW.Connection = Entity.Null;
                playerR.ValueRW.ConnectionId = -1;
                playerR.ValueRW.ConnectionState = PlayerConnectionState.Disconnected;
            }
            else
            {
                if (!player.ValueRO.IsCoreComputerSpawned)
                {
                    Debug.Log($"{DebugEx.ServerPrefix} Spawning core computer for player \"{player.ValueRO.Nickname}\" ({playerR.ValueRO.ConnectionState} {playerR.ValueRO.ConnectionId})");

                    for (int i = 0; i < spawns.Length; i++)
                    {
                        if (spawns[i].IsOccupied) continue;
                        spawns[i] = spawns[i] with { IsOccupied = true };

                        Entity coreComputer = commandBuffer.Instantiate(prefabs.CoreComputer);
                        commandBuffer.SetComponent<UnitTeam>(coreComputer, new()
                        {
                            Team = player.ValueRO.Team
                        });
                        commandBuffer.SetComponent<LocalTransform>(coreComputer, LocalTransform.FromPosition(spawns[i].Position));
                        commandBuffer.SetComponent<GhostOwner>(coreComputer, new()
                        {
                            NetworkId = playerR.ValueRO.ConnectionId,
                        });

                        Entity builder = commandBuffer.Instantiate(prefabs.Builder);
                        commandBuffer.SetComponent<UnitTeam>(builder, new()
                        {
                            Team = player.ValueRO.Team
                        });
                        commandBuffer.SetComponent<LocalTransform>(builder, LocalTransform.FromPosition(spawns[i].Position + new Unity.Mathematics.float3(2f, 0f, 2f)));
                        commandBuffer.SetComponent<GhostOwner>(builder, new()
                        {
                            NetworkId = playerR.ValueRO.ConnectionId,
                        });

                        goto spawned;
                    }

                    Debug.LogError($"{DebugEx.ServerPrefix} Cannot spawn core computer for player \"{player.ValueRO.Nickname}\" ({playerR.ValueRO.ConnectionState} {playerR.ValueRO.ConnectionId})");

                spawned:
                    player.ValueRW.IsCoreComputerSpawned = true;
                    player.ValueRW.Resources = 30;
                }

                if (player.ValueRO.IsAdmin)
                {
                    isAdminAssigned = true;
                }
            }
        }

        foreach (var (player, playerV) in
            SystemAPI.Query<RefRW<Player>, RefRW<VirtualPlayer>>())
        {
            if (player.ValueRO.Team == Player.UnassignedTeam)
            {
                player.ValueRW.Team = TeamCounter++;
                Debug.Log($"{DebugEx.ServerPrefix} Assigning team {player.ValueRO.Team} to virtual player \"{player.ValueRO.Nickname}\"");
            }

            if (!player.ValueRO.IsCoreComputerSpawned)
            {
                Debug.Log($"{DebugEx.ServerPrefix} Spawning core computer for virtual player \"{player.ValueRO.Nickname}\"");

                for (int i = 0; i < spawns.Length; i++)
                {
                    if (spawns[i].IsOccupied) continue;
                    spawns[i] = spawns[i] with { IsOccupied = true };

                    Entity coreComputer = commandBuffer.Instantiate(prefabs.CoreComputer);
                    commandBuffer.SetComponent<UnitTeam>(coreComputer, new()
                    {
                        Team = player.ValueRO.Team
                    });
                    commandBuffer.SetComponent<LocalTransform>(coreComputer, LocalTransform.FromPosition(spawns[i].Position));

                    Entity builder = commandBuffer.Instantiate(prefabs.Builder);
                    commandBuffer.SetComponent<UnitTeam>(builder, new()
                    {
                        Team = player.ValueRO.Team
                    });
                    commandBuffer.SetComponent<LocalTransform>(builder, LocalTransform.FromPosition(spawns[i].Position + new Unity.Mathematics.float3(2f, 0f, 2f)));

                    goto spawned;
                }

                Debug.LogError($"{DebugEx.ServerPrefix} Cannot spawn core computer for virtual player \"{player.ValueRO.Nickname}\"");

            spawned:
                player.ValueRW.IsCoreComputerSpawned = true;
                player.ValueRW.Resources = 30;
            }
        }

        for (int i = 0; i < virtualPlayerSettings.Length; i++)
        {
            var settings = virtualPlayerSettings[i];

            bool exists = false;
            foreach (var player in
                SystemAPI.Query<RefRW<VirtualPlayer>>())
            {
                if (player.ValueRO.Index != i) continue;
                exists = true;
                break;
            }
            if (exists) continue;

            Guid guid;
            unsafe
            {
                byte* ptr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(bytes));
                *(int*)(ptr + 0) = i; // 4
                *(double*)(ptr + 4) = SystemAPI.Time.ElapsedTime; // 8
                *(uint*)(ptr + 12) = 0x69420; // 4
                guid = new Guid(bytes);
            }

            Entity newPlayer = commandBuffer.Instantiate(prefabs.VirtualPlayer);
            commandBuffer.SetComponent<Player>(newPlayer, new()
            {
                Team = Player.UnassignedTeam,
                IsCoreComputerSpawned = false,
                Guid = guid,
                Nickname = settings.Nickname,
            });
            commandBuffer.SetComponent<VirtualPlayer>(newPlayer, new()
            {
                Index = i,
            });

            Debug.Log($"{DebugEx.ServerPrefix} Virtual player created\n  Nickname: \"{settings.Nickname}\"\n  Guid: {guid}");
            ChatSystemServer.SendChatMessage(commandBuffer, state.WorldUnmanaged, $"Player {settings.Nickname} connected", MonoTime.UnixSeconds);
        }

        if (!isAdminAssigned)
        {
            foreach (var (player, playerR) in
                SystemAPI.Query<RefRW<Player>, RefRO<RealPlayer>>())
            {
                if (playerR.ValueRO.ConnectionState is not PlayerConnectionState.Connected and not PlayerConnectionState.Local) continue;

                Debug.Log(string.Format($"{DebugEx.ServerPrefix} Assigning admin to player {{0}} (connection: {{1}} nickname: `{{2}}`)", player.ValueRO.Guid, playerR.ValueRO.ConnectionId, player.ValueRO.Nickname));
                player.ValueRW.IsAdmin = true;
                break;
            }
        }
    }

    public static bool FindPlayer(WorldUnmanaged world, Guid token, out Player player, Allocator allocator = Allocator.Temp)
    {
        using EntityQuery q = world.EntityManager.CreateEntityQuery(typeof(Player));
        using NativeArray<Entity> playerEntities = q.ToEntityArray(allocator);

        for (int i = 0; i < playerEntities.Length; i++)
        {
            Player item = world.EntityManager.GetComponentData<Player>(playerEntities[i]);
            if (item.Guid == token)
            {
                player = item;
                return true;
            }
        }

        player = default;
        return false;
    }
}
