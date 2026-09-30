using System;
using Unity.Entities;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct PingSystemClient : ISystem
{
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;
        long now = MonoTime.Ticks;

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<PingResponseFinalRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);

            foreach (var player in
                SystemAPI.Query<RefRW<RealPlayer>>())
            {
                if (player.ValueRO.ConnectionId != command.ValueRO.Source) continue;

                if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

                if (player.ValueRO.PingRequested == command.ValueRO.Tick)
                {
                    player.ValueRW.Ping = (int)(now - command.ValueRO.Tick);
                }

                player.ValueRW.PingResponded = now;
            }
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<PingRequestRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);

            NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new PingResponseRpc()
            {
                Tick = command.ValueRO.Tick,
            }, request.ValueRO.SourceConnection);
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<PingRequestForwardRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);

            NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new PingResponseForwardRpc()
            {
                Tick = command.ValueRO.Tick,
                Target = command.ValueRO.Source,
            }, request.ValueRO.SourceConnection);
        }

        foreach (var player in
            SystemAPI.Query<RefRW<RealPlayer>>())
        {
            if ((player.ValueRO.PingResponded == 0 || TimeSpan.FromTicks(now - player.ValueRO.PingResponded).TotalSeconds > 2) && TimeSpan.FromTicks(now - player.ValueRO.PingRequested).TotalSeconds > 2)
            {
                if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new PingRequestRpc()
                {
                    Target = (byte)player.ValueRO.ConnectionId,
                    Tick = now,
                });
                player.ValueRW.PingRequested = now;
            }
        }
    }
}
