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
        TeamCounter = 0;
    }

    void ISystem.OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonBuffer(out DynamicBuffer<BufferedSpawn> spawns, false)) return;

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

            (bool, Player) exists = default;

            foreach (var player in
                SystemAPI.Query<RefRO<Player>>())
            {
                if (player.ValueRO.ConnectionId == source.Value)
                {
                    exists = (true, player.ValueRO);
                }
            }

            if (exists.Item1)
            {
                Debug.LogWarning($"{DebugEx.ServerPrefix} Already logged in");
                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new SessionResponseRpc()
                {
                    StatusCode = SessionStatusCode.AlreadyLoggedIn,
                    Nickname = exists.Item2.Nickname,
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
                    ConnectionId = source.Value,
                    ConnectionState = PlayerConnectionState.Connected,
                    Team = Player.UnassignedTeam,
                    IsCoreComputerSpawned = false,
                    Guid = guid,
                    Nickname = command.ValueRO.Nickname,
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
            foreach (var player in
                SystemAPI.Query<RefRW<Player>>())
            {
                if (player.ValueRO.Guid != Marshal.As<FixedBytes16, Guid>(guid)) continue;

                exists = true;
                bool loggedIn = player.ValueRO.ConnectionId != -1;
                if (!loggedIn)
                {
                    player.ValueRW.ConnectionId = source.Value;
                    player.ValueRW.ConnectionState = PlayerConnectionState.Connected;
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

        foreach (var player in
            SystemAPI.Query<RefRW<Player>>())
        {
            if (player.ValueRO.Team == Player.UnassignedTeam)
            {
                player.ValueRW.Team = TeamCounter++;
            }

            if (player.ValueRO.ConnectionState is not PlayerConnectionState.Connected and not PlayerConnectionState.Local) continue;

            bool found = false;

            if (player.ValueRO.ConnectionState == PlayerConnectionState.Connected)
            {
                foreach (var id in
                    SystemAPI.Query<RefRO<NetworkId>>()
                    .WithAll<InitializedClient>())
                {
                    if (id.ValueRO.Value == player.ValueRO.ConnectionId)
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
                Debug.Log($"{DebugEx.ServerPrefix} Client {player.ValueRO.ConnectionId} disconnected");
                ChatSystemServer.SendChatMessage(commandBuffer, state.WorldUnmanaged, $"Player {player.ValueRO.Nickname} disconnected", MonoTime.UnixSeconds);

                player.ValueRW.ConnectionId = -1;
                player.ValueRW.ConnectionState = PlayerConnectionState.Disconnected;
            }
            else
            {
                if (!player.ValueRO.IsCoreComputerSpawned)
                {
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
                            NetworkId = player.ValueRO.ConnectionId,
                        });

                        Entity builder = commandBuffer.Instantiate(prefabs.Builder);
                        commandBuffer.SetComponent<UnitTeam>(builder, new()
                        {
                            Team = player.ValueRO.Team
                        });
                        commandBuffer.SetComponent<LocalTransform>(builder, LocalTransform.FromPosition(spawns[i].Position + new Unity.Mathematics.float3(2f, 0f, 2f)));
                        commandBuffer.SetComponent<GhostOwner>(builder, new()
                        {
                            NetworkId = player.ValueRO.ConnectionId,
                        });

                        break;
                    }
                    player.ValueRW.IsCoreComputerSpawned = true;
                    player.ValueRW.Resources = 30;
                }
            }
        }
    }
}
