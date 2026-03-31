using System;
using LanguageCore.Runtime;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct UnitCommandSystemServer : ISystem
{
    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<UnitCommandRequestRpc>>()
            .WithEntityAccess())
        {
            commandBuffer.DestroyEntity(entity);
            NetworkId networkId = request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO;

            int sourceTeam = -1;
            foreach (var player in
                SystemAPI.Query<RefRO<Player>>())
            {
                if (player.ValueRO.ConnectionId != networkId.Value) continue;
                sourceTeam = player.ValueRO.Team;
                break;
            }

            if (sourceTeam == -1)
            {
                Debug.LogError($"{DebugEx.ServerPrefix} Invalid team");
                continue;
            }

            HandleCommand(ref state, sourceTeam, command.ValueRO.Entity, command.ValueRO.CommandId, command.ValueRO.Arguments);
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<UnitCommandBulkRequestRpc>>()
            .WithEntityAccess())
        {
            commandBuffer.DestroyEntity(entity);
            NetworkId networkId = request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO;

            int sourceTeam = -1;
            foreach (var player in
                SystemAPI.Query<RefRO<Player>>())
            {
                if (player.ValueRO.ConnectionId != networkId.Value) continue;
                sourceTeam = player.ValueRO.Team;
                break;
            }

            if (sourceTeam == -1)
            {
                Debug.LogError($"{DebugEx.ServerPrefix} Invalid team");
                continue;
            }

            for (int i = 0; i < command.ValueRO.Entities.Length; i++)
            {
                HandleCommand(ref state, sourceTeam, command.ValueRO.Entities[i], command.ValueRO.CommandId, command.ValueRO.Arguments);
            }
        }
    }

    [BurstCompile]
    unsafe void HandleCommand(ref SystemState _, int sourceTeam, in SpawnedGhost entity, int commandId, in UnitCommandArguments arguments)
    {
        foreach (var (ghostInstance, processor, team) in
            SystemAPI.Query<RefRO<GhostInstance>, RefRW<Processor>, RefRO<UnitTeam>>())
        {
            if (!entity.Equals(ghostInstance.ValueRO)) continue;

            if (team.ValueRO.Team != sourceTeam)
            {
                Debug.LogError(string.Format($"{DebugEx.ServerPrefix} Can't send commands to units in other team. Source: {{0}} Target: {{1}}", sourceTeam, team.ValueRO.Team));
                return;
            }

            ReadOnlySpan<UnitCommandDefinition> commandDefinitions = processor.ValueRO.Source.UnitCommandDefinitions.AsSpan();
            int k = -1;

            for (int i = 0; i < commandDefinitions.Length; i++)
            {
                if (commandDefinitions[i].Id != commandId) continue;
                k = i;
                break;
            }

            if (k == -1)
            {
                Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Command {{0}} not found", commandId));
                return;
            }

            FixedBytes30 data = default;
            nint dataPtr = (nint)(&data);
            int dataLength = 0;
            for (int j = 0; j < commandDefinitions[k].ParameterCount; j++)
            {
                switch (commandDefinitions[k].GetParameter(j))
                {
                    case UnitCommandParameter.Position2:
                    {
                        if (arguments.WorldPosition.Equals(default))
                        {
                            Debug.LogWarning($"{DebugEx.ServerPrefix} Position data not provided");
                            return;
                        }

                        dataPtr.Set(new float2(arguments.WorldPosition.x, arguments.WorldPosition.z));
                        dataPtr += sizeof(float2);
                        dataLength += sizeof(float2);
                        break;
                    }
                    case UnitCommandParameter.Position3:
                    {
                        if (arguments.WorldPosition.Equals(default))
                        {
                            Debug.LogWarning($"{DebugEx.ServerPrefix} Position data not provided");
                            return;
                        }

                        dataPtr.Set(arguments.WorldPosition);
                        dataPtr += sizeof(float3);
                        dataLength += sizeof(float3);
                        break;
                    }
                    default:
                        throw new UnreachableException();
                }
            }

            if (processor.ValueRW.CommandQueue.Length >= processor.ValueRW.CommandQueue.Capacity)
            {
                Debug.LogWarning($"{DebugEx.ServerPrefix} Too much commands");
                processor.ValueRW.CommandQueue.RemoveAt(0);
            }

            processor.ValueRW.CommandQueue.Add(new UnitCommandRequest(commandId, (ushort)dataLength, data));

            return;
        }

        Debug.LogWarning($"{DebugEx.ServerPrefix} Ghost not found");
    }
}
