using LanguageCore.Runtime;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct ProcessorDebugSystemServer : ISystem
{
    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<StopDebugRequestRpc>>()
            .WithEntityAccess())
        {
            commandBuffer.DestroyEntity(entity);

            foreach (var (ghost, processor) in SystemAPI.Query<RefRO<GhostInstance>, RefRW<Processor>>())
            {
                if (!command.ValueRO.Entity.Equals(ghost.ValueRO)) continue;
                processor.ValueRW.DebugContext.IsBeingDebugged = false;
                break;
            }
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<StartDebugRequestRpc>>()
            .WithEntityAccess())
        {
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
                Debug.LogError(string.Format($"{DebugEx.ServerPrefix} Failed to start debug: requested by {{0}} but doesn't have a team", networkId));
                return;
            }

            var p = SystemAPI.GetComponentRO<Player>(requestPlayer);

            foreach (var (ghost, processor, processorEntity) in SystemAPI.Query<RefRO<GhostInstance>, RefRW<Processor>>().WithEntityAccess())
            {
                if (!command.ValueRO.Entity.Equals(ghost.ValueRO)) continue;

                if (processor.ValueRO.IsVersionLocked) break;

                foreach (var ext in ExtServer.Instance.ConnectedExtensions)
                {
                    if (ext.Token != p.ValueRO.Guid.ToString()) continue;

                    string name = GetEntityName(state.EntityManager, processorEntity, ghost.ValueRO);

                    ExtServer.Instance.StartDebuggingUnit(ghost.ValueRO, name, ext);
                    break;
                }

                break;
            }
        }
    }

    public static string GetEntityName(EntityManager entityManager, Entity entity, SpawnedGhost ghost)
    {
        string name = $"Unit {ghost.ghostId}:{ghost.spawnTick}";

        if (entityManager.TryGetComponentData<UnitPrefabInstance>(entity, out var unitPrefabInstance))
        {
            Entity db = entityManager.GetSingletonEntity<UnitDatabase>();
            DynamicBuffer<BufferedUnit> units = entityManager.GetBuffer<BufferedUnit>(db);
            name = units[unitPrefabInstance.Index].Name.ToString();
        }
        else if (entityManager.TryGetComponentData<BuildingPrefabInstance>(entity, out var buildingPrefabInstance))
        {
            Entity db = entityManager.GetSingletonEntity<BuildingDatabase>();
            DynamicBuffer<BufferedBuilding> units = entityManager.GetBuffer<BufferedBuilding>(db);
            name = units[buildingPrefabInstance.Index].Name.ToString();
        }

        return name;
    }

    public enum StopReason
    {
        No,
        Pause,
        Breakpoint,
        Signal,
        RuntimeException,

        StepForward,
        StepIn,
        StepOut,
        StepInstruction,

        StepForwardUnfinished,
        StepInUnfinished,
        StepOutUnfinished,
        StepInstructionUnfinished,
    }

    public struct DebugContext
    {
        [GhostField] public required bool IsBeingDebugged;
        [GhostField(SendData = false)] public required FixedList128Bytes<ushort> Breakpoints;
        [GhostField(SendData = false)] public StopReason Stopped;
        [GhostField(SendData = false)] public bool SkipCurrentBreakpoint;
        [GhostField(SendData = false)] public bool IsStopUnhandled;
        [GhostField(SendData = false)] public bool IsContinueUnhandled;
    }

    public static void HandleTickDebug(ref Processor processor, ref ProcessorState processorState)
    {
        if (processor.DebugContext.Stopped
            is StopReason.Breakpoint
            or StopReason.Pause
            or StopReason.RuntimeException
            or StopReason.Signal
            or StopReason.StepForward
            or StopReason.StepIn
            or StopReason.StepOut
            or StopReason.StepInstruction) return;

        try
        {
            for (int i = 0; i < processor.CyclesPerTick && !processorState.IsDone; i++)
            {
                if (!processor.DebugContext.SkipCurrentBreakpoint)
                {
                    for (int j = 0; j < processor.DebugContext.Breakpoints.Length; j++)
                    {
                        if (processor.DebugContext.Breakpoints[j] != processorState.Registers.CodePointer) continue;

                        processor.DebugContext.Stopped = StopReason.Breakpoint;
                        processor.DebugContext.IsStopUnhandled = true;
                        return;
                    }
                }
                else
                {
                    processor.DebugContext.SkipCurrentBreakpoint = false;
                }

                if (processorState.Signal != Signal.None)
                {
                    if (!processor.SignalNotified)
                    {
                        processor.SignalNotified = true;
                    }
                    processor.DebugContext.Stopped = StopReason.Signal;
                    processor.DebugContext.IsStopUnhandled = true;
                    break;
                }

                processor.SignalNotified = false;

                if (processor.IsKeyRequested)
                {
                    if (processor.InputKey.Length == 0) break;
                    byte key = processor.InputKey[0];
                    processor.InputKey.RemoveAt(0);
                    processor.IsKeyRequested = false;
                    processorState.Pop(1);
                    processorState.Push(key);
                }

                processorState.Tick();

                if (processor.DebugContext.Stopped
                    is StopReason.StepForwardUnfinished
                    or StopReason.StepInUnfinished
                    or StopReason.StepOutUnfinished
                    or StopReason.StepInstructionUnfinished)
                {
#pragma warning disable IDE0072 // Add missing cases
                    processor.DebugContext.Stopped = processor.DebugContext.Stopped switch
                    {
                        StopReason.StepForwardUnfinished => StopReason.StepForward,
                        StopReason.StepInUnfinished => StopReason.StepIn,
                        StopReason.StepOutUnfinished => StopReason.StepOut,
                        StopReason.StepInstructionUnfinished => StopReason.StepInstruction,
                        _ => processor.DebugContext.Stopped,
                    };
#pragma warning restore IDE0072 // Add missing cases
                    processor.DebugContext.IsStopUnhandled = true;
                    return;
                }
            }
        }
        catch (RuntimeException ex)
        {
            Debug.LogError(ex.ToString());
            processor.DebugContext.Stopped = StopReason.RuntimeException;
            processor.DebugContext.IsStopUnhandled = true;
        }
    }
}
