using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct ResearchSystemServer : ISystem
{
    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<ResearchesRequestRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);
            NetworkId networkId = request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO;

            Entity requestPlayer = default;

            if (SystemAPI.TryGetComponent(entity, out VirtualRpc virtualRpc))
            {
                foreach (var (player, _entity) in
                    SystemAPI.Query<RefRO<VirtualPlayer>>()
                    .WithEntityAccess())
                {
                    if (player.ValueRO.Index != virtualRpc.PlayerIndex) continue;
                    requestPlayer = _entity;
                    break;
                }
            }
            else
            {
                foreach (var (player, _entity) in
                    SystemAPI.Query<RefRO<RealPlayer>>()
                    .WithEntityAccess())
                {
                    if (player.ValueRO.ConnectionId != networkId.Value) continue;
                    requestPlayer = _entity;
                    break;
                }
            }

            if (requestPlayer == Entity.Null)
            {
                Debug.LogError(string.Format($"{DebugEx.ServerPrefix} Player with network id {{0}} doesn't have a team", networkId));
                continue;
            }

            DynamicBuffer<BufferedAcquiredResearch> acquiredResearches = SystemAPI.GetBuffer<BufferedAcquiredResearch>(requestPlayer);

            foreach (var (_research, requirements) in
                SystemAPI.Query<RefRO<Research>, DynamicBuffer<BufferedResearchRequirement>>())
            {
                bool hasAllRequirements = true;

                foreach (BufferedResearchRequirement requirement in requirements)
                {
                    bool hasThis = false;

                    foreach (BufferedAcquiredResearch acquired in acquiredResearches)
                    {
                        if (requirement.Name != acquired.Name) continue;
                        hasThis = true;
                        break;
                    }

                    if (!hasThis)
                    {
                        hasAllRequirements = false;
                        break;
                    }
                }
                if (!hasAllRequirements) continue;

                bool alreadyResearched = false;
                foreach (BufferedAcquiredResearch acquired in acquiredResearches)
                {
                    if (_research.ValueRO.Name != acquired.Name) continue;
                    alreadyResearched = true;
                    break;
                }
                if (alreadyResearched) continue;

                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new ResearchesResponseRpc()
                {
                    Name = _research.ValueRO.Name,
                }, request.ValueRO.SourceConnection);
            }
        }
    }
}
