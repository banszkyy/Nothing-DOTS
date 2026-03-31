using System;
using Unity.Entities;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct ChatSystemClient : ISystem
{
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (_, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<ChatMessageNotificationRpc>>()
            .WithEntityAccess())
        {
            commandBuffer.DestroyEntity(entity);
            ChatManager.Instance.AppendChatMessageElement(command.ValueRO.Sender, command.ValueRO.Message.ToString(), DateTimeOffset.FromUnixTimeSeconds(command.ValueRO.Time));
        }
    }
}
