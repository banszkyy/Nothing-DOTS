using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct ChatSystemServer : ISystem
{
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<ChatMessageRequestRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);
            NetworkId networkId = request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO;

            Player senderPlayer = default;
            RealPlayer senderPlayerR = default;
            Entity senderPlayerE = default;
            foreach (var (player, playerR, playerE) in
                SystemAPI.Query<RefRO<Player>, RefRO<RealPlayer>>()
                .WithEntityAccess())
            {
                if (playerR.ValueRO.ConnectionId == networkId.Value)
                {
                    senderPlayer = player.ValueRO;
                    senderPlayerR = playerR.ValueRO;
                    senderPlayerE = playerE;
                    break;
                }
            }

            FixedString64Bytes message = command.ValueRO.Message;

            if (senderPlayerE == Entity.Null)
            {
                Debug.LogWarning($"Sender player for chat message (\"{message}\" {command.ValueRO.Time}) not found");
            }

            if (message.StartsWith('/'))
            {
                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new ChatMessageNotificationRpc()
                {
                    Sender = networkId.Value,
                    Message = command.ValueRO.Message,
                    Time = command.ValueRO.Time,
                }, request.ValueRO.SourceConnection);

                ReadOnlySpan<byte> cmd = message.AsSpan()[1..];
                if (cmd.SequenceEqual("creative"u8))
                {
                    if (senderPlayerR.ConnectionState is PlayerConnectionState.Local or PlayerConnectionState.Server || senderPlayer.IsAdmin)
                    {
                        SystemAPI.GetComponentRW<Player>(senderPlayerE).ValueRW.InCreative = true;
                        NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new ChatMessageNotificationRpc()
                        {
                            Sender = 0,
                            Message = "Ok",
                            Time = MonoTime.UnixSeconds,
                        }, request.ValueRO.SourceConnection);
                    }
                    else
                    {
                        NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new ChatMessageNotificationRpc()
                        {
                            Sender = 0,
                            Message = "Unauthorized",
                            Time = MonoTime.UnixSeconds,
                        }, request.ValueRO.SourceConnection);
                    }
                }
                else if (cmd.StartsWith("research"u8))
                {
                    if (senderPlayerR.ConnectionState is PlayerConnectionState.Local or PlayerConnectionState.Server || senderPlayer.IsAdmin)
                    {
                        ReadOnlySpan<byte> arg = cmd["research".Length..].TrimStart();
                        if (arg.SequenceEqual("all"u8))
                        {
                            DynamicBuffer<BufferedAcquiredResearch> acquiredResearches = SystemAPI.GetBuffer<BufferedAcquiredResearch>(senderPlayerE);
                            int n = 0;

                            foreach (var _research in
                                SystemAPI.Query<RefRO<Research>>())
                            {
                                bool alreadyResearched = false;
                                foreach (BufferedAcquiredResearch acquired in acquiredResearches)
                                {
                                    if (_research.ValueRO.Name != acquired.Name) continue;
                                    alreadyResearched = true;
                                    break;
                                }
                                if (alreadyResearched) continue;

                                if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

                                acquiredResearches.Add(new BufferedAcquiredResearch()
                                {
                                    Name = _research.ValueRO.Name,
                                });

                                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new ResearchDoneRpc()
                                {
                                    Name = _research.ValueRO.Name,
                                }, request.ValueRO.SourceConnection);
                                n++;
                            }

                            NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new ChatMessageNotificationRpc()
                            {
                                Sender = 0,
                                Message = $"Researched {n} technologies",
                                Time = MonoTime.UnixSeconds,
                            }, request.ValueRO.SourceConnection);
                        }
                        else
                        {
                            DynamicBuffer<BufferedAcquiredResearch> acquiredResearches = SystemAPI.GetBuffer<BufferedAcquiredResearch>(senderPlayerE);

                            foreach (var _research in
                                SystemAPI.Query<RefRO<Research>>())
                            {
                                var n = _research.ValueRO.Name;
                                if (!n.AsSpan().SequenceEqual(arg)) continue;

                                bool alreadyResearched = false;
                                foreach (BufferedAcquiredResearch acquired in acquiredResearches)
                                {
                                    if (_research.ValueRO.Name != acquired.Name) continue;
                                    alreadyResearched = true;
                                    break;
                                }
                                if (alreadyResearched)
                                {
                                    NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new ChatMessageNotificationRpc()
                                    {
                                        Sender = 0,
                                        Message = "Technology already researched",
                                        Time = MonoTime.UnixSeconds,
                                    }, request.ValueRO.SourceConnection);
                                    break;
                                }

                                if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

                                acquiredResearches.Add(new BufferedAcquiredResearch()
                                {
                                    Name = _research.ValueRO.Name,
                                });

                                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new ResearchDoneRpc()
                                {
                                    Name = _research.ValueRO.Name,
                                }, request.ValueRO.SourceConnection);

                                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new ChatMessageNotificationRpc()
                                {
                                    Sender = 0,
                                    Message = "Ok",
                                    Time = MonoTime.UnixSeconds,
                                }, request.ValueRO.SourceConnection);

                                break;
                            }

                            NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new ChatMessageNotificationRpc()
                            {
                                Sender = 0,
                                Message = "Technology doesn't exists",
                                Time = MonoTime.UnixSeconds,
                            }, request.ValueRO.SourceConnection);
                        }
                    }
                    else
                    {
                        NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new ChatMessageNotificationRpc()
                        {
                            Sender = 0,
                            Message = "Unauthorized",
                            Time = MonoTime.UnixSeconds,
                        }, request.ValueRO.SourceConnection);
                    }
                }
                continue;
            }

            NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new ChatMessageNotificationRpc()
            {
                Sender = networkId.Value,
                Message = command.ValueRO.Message,
                Time = command.ValueRO.Time,
            });
        }
    }

    [BurstCompile]
    public static void SendChatMessage(in EntityCommandBuffer commandBuffer, in WorldUnmanaged world, in FixedString64Bytes message, long time)
    {
        NetcodeUtils.CreateRPC(in commandBuffer, in world, new ChatMessageNotificationRpc()
        {
            Sender = 0,
            Message = message,
            Time = time,
        });
    }
}
