using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct UnitLogSystemServer : ISystem
{
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<UnitLogRequestRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);
            NetworkId networkId = request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO;

            int sourceTeam = -1;

            if (SystemAPI.TryGetComponent(entity, out VirtualRpc virtualRpc))
            {
                foreach (var (player, _player, _entity) in
                    SystemAPI.Query<RefRO<Player>, RefRO<VirtualPlayer>>()
                    .WithEntityAccess())
                {
                    if (_player.ValueRO.Index != virtualRpc.PlayerIndex) continue;
                    sourceTeam = player.ValueRO.Team;
                    break;
                }
            }
            else
            {
                foreach (var (player, _player, _entity) in
                    SystemAPI.Query<RefRO<Player>, RefRO<RealPlayer>>()
                    .WithEntityAccess())
                {
                    if (_player.ValueRO.ConnectionId != networkId.Value) continue;
                    sourceTeam = player.ValueRO.Team;
                    break;
                }
            }

            if (sourceTeam == -1)
            {
                Debug.LogError($"{DebugEx.ServerPrefix} Invalid team");
                continue;
            }

            foreach (var (ghostInstance, team, log) in
                SystemAPI.Query<RefRO<GhostInstance>, RefRO<UnitTeam>, DynamicBuffer<BufferedLogPiece>>())
            {
                if (!command.ValueRO.Ghost.Equals(ghostInstance.ValueRO)) continue;

                if (team.ValueRO.Team != sourceTeam)
                {
                    Debug.LogError(string.Format($"{DebugEx.ServerPrefix} Can't request logs from units in other team. Source: {{0}} Target: {{1}}", sourceTeam, team.ValueRO.Team));
                    goto ok;
                }

                FixedList512Bytes<byte> data = new();

                ReadOnlySpan<byte> logBuffer = log.AsNativeArray().Reinterpret<byte>().AsReadOnlySpan();

                int i = 0;
                int count = 0;
                long first = 0;
                long last = 0;

                List<object> _debug = new();

                while (i < log.Length)
                {
                    LogPieceHeader header;
                    int start = i;

                    try
                    {
                        header = LogPieceExtensions.Read(logBuffer, ref i);
                    }
                    catch (Exception)
                    {
                        goto skip;
                    }

                    if (header.Timestamp > command.ValueRO.From) continue;

                    unsafe
                    {
                        fixed (byte* ptr = logBuffer[start..])
                        {
                            int length = i - start;
                            if (data.Length + length > data.Capacity) break;
                            data.AddRange(ptr, length);

                            //Debug.Log($"R {string.Join(" ", logBuffer.Slice(start, length).ToArray())}");
                        }
                    }

                    if (first == 0) first = header.Timestamp;
                    if (header.Timestamp > last) last = header.Timestamp;

                    if (++count >= command.ValueRO.Count) break;
                }

            skip:;

                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new UnitLogResponseRpc()
                {
                    Ghost = ghostInstance.ValueRO,
                    Data = data,
                    From = first,
                    To = last,
                    Count = count,
                }, request.ValueRO.SourceConnection);

                goto ok;
            }

            Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Ghost {{0}} not found", new GhostInstance() { ghostId = command.ValueRO.Ghost.ghostId, spawnTick = command.ValueRO.Ghost.spawnTick }));

        ok:;
        }
    }
}

