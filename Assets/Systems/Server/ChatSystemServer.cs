using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct ChatSystemServer : ISystem
{
    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<ChatMessageRequestRpc>>()
            .WithEntityAccess())
        {
            commandBuffer.DestroyEntity(entity);
            NetworkId networkId = request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO;

            Player senderPlayer = default;
            Entity senderPlayerE = default;
            foreach (var (player, playerE) in
                SystemAPI.Query<RefRO<Player>>()
                .WithEntityAccess())
            {
                if (player.ValueRO.ConnectionId == networkId.Value)
                {
                    senderPlayer = player.ValueRO;
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

                Span<byte> cmd = message.AsSpan()[1..];
                if (cmd.StartsWith("creative"u8))
                {
                    if (senderPlayer.ConnectionState is PlayerConnectionState.Local or PlayerConnectionState.Server)
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
