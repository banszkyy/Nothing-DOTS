using System;
using LanguageCore.Runtime;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial class ProcessorSourceSystemServer : SystemBase
{
    static readonly bool EnableLogging = false;

    protected override void OnUpdate()
    {
        EntityCommandBuffer commandBuffer = default;
        CompilerSystemServer compilerSystem = World.GetExistingSystemManaged<CompilerSystemServer>();

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<ProcessorCommandRequestRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
            commandBuffer.DestroyEntity(entity);

            foreach (var (ghostInstance, processor, processorEntity) in
                SystemAPI.Query<RefRO<GhostInstance>, RefRW<Processor>>()
                .WithEntityAccess())
            {
                if (!command.ValueRO.Entity.Equals(ghostInstance.ValueRO)) continue;

                switch (command.ValueRO.Command)
                {
                    case ProcessorCommand.Halt:
                        processor.ValueRW.Signal = Signal.Halt;
                        break;
                    case ProcessorCommand.Reset:
                        DisposeProcessorResources(World.Unmanaged, processorEntity);
                        ResetProcessor(ref processor.ValueRW);
                        if (processor.ValueRO.DebugContext.IsBeingDebugged)
                        {
                            processor.ValueRW.DebugContext.IsContinueUnhandled = true;
                        }
                        break;
                    case ProcessorCommand.Continue:
                        processor.ValueRW.Signal = Signal.None;
                        processor.ValueRW.Crash = 0;
                        if (processor.ValueRO.DebugContext.IsBeingDebugged)
                        {
                            processor.ValueRW.DebugContext.IsContinueUnhandled = true;
                        }
                        break;
                    case ProcessorCommand.Key:
                        if (processor.ValueRW.InputKey.Length >= processor.ValueRW.InputKey.Capacity)
                        {
                            Debug.LogWarning($"{DebugEx.ServerPrefix} Standard input buffer is full");
                            break;
                        }
                        processor.ValueRW.InputKey.Add((byte)command.ValueRO.Data);
                        break;
                    default: throw new UnreachableException();
                }

                break;
            }
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<SetProcessorSourceRequestRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
            commandBuffer.DestroyEntity(entity);

            NetcodeEndPoint ep;
            if (SystemAPI.TryGetComponent(entity, out VirtualRpc virtualRpc))
            {
                if (virtualRpc.PlayerIndex != -1)
                {
                    ep = NetcodeEndPoint.Server;
                }
                else
                {
                    Entity c = Entity.Null;
                    foreach (var (player, playerR) in SystemAPI.Query<RefRO<Player>, RefRO<RealPlayer>>())
                    {
                        if (player.ValueRO.Team != virtualRpc.Team) continue;
                        if (c == Entity.Null)
                        {
                            c = playerR.ValueRO.Connection;
                        }
                        else
                        {
                            Debug.LogError($"{DebugEx.ServerPrefix} Cannot set the source of the processor, virtual RPC team matched multiple players");
                            c = Entity.Null;
                            break;
                        }
                    }

                    if (c == default)
                    {
                        Debug.LogError($"{DebugEx.ServerPrefix} Cannot set the source of the processor");
                        continue;
                    }

                    ep = new NetcodeEndPoint(SystemAPI.GetComponent<NetworkId>(c), c);
                }
            }
            else
            {
                if (request.ValueRO.SourceConnection == default)
                {
                    ep = NetcodeEndPoint.Server;
                }
                else
                {
                    ep = new(SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO, request.ValueRO.SourceConnection);
                    if (!World.IsServer()) ep = NetcodeEndPoint.Server;
                }
            }

            foreach (var (ghostInstance, processor) in
                SystemAPI.Query<RefRO<GhostInstance>, RefRW<Processor>>())
            {
                if (!command.ValueRO.Entity.Equals(ghostInstance.ValueRO)) continue;

                processor.ValueRW.SourceFile = new FileId(command.ValueRO.Source, ep);

                if (compilerSystem.CompiledSources.TryGetValue(processor.ValueRO.SourceFile, out CompiledSourceServer? source))
                {
                    if (EnableLogging) Debug.Log($"{DebugEx.ServerPrefix} Update source file `{command.ValueRO.Source}` ({source.LatestVersion} -> {source.LatestVersion + 1})");
                    source.LatestVersion++;
                    if (command.ValueRO.IsHotReload)
                    {
                        source.HotReloadVersion = source.LatestVersion;
                    }
                }
                else
                {
                    if (EnableLogging) Debug.Log($"{DebugEx.ServerPrefix} Creating new source file `{command.ValueRO.Source}`");
                    compilerSystem.AddEmpty(processor.ValueRO.SourceFile, 1);
                    processor.ValueRW.CompiledSourceVersion = 0;
                }

                break;
            }
        }

        foreach (var (processor, initialization, entity) in
            SystemAPI.Query<RefRW<Processor>, RefRO<ProcessorInitialization>>()
            .WithEntityAccess())
        {
            processor.ValueRW.SourceFile = initialization.ValueRO.SourceFile;

            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
            commandBuffer.RemoveComponent<ProcessorInitialization>(entity);
        }

        foreach (var (processor, processorEntity) in
            SystemAPI.Query<RefRW<Processor>>()
            .WithEntityAccess())
        {
            if (processor.ValueRO.SourceFile == default)
            {
                if (processor.ValueRW.Source.Code.IsCreated)
                {
                    if (EnableLogging) Debug.Log($"{DebugEx.ServerPrefix} Disposing instructions (source file is null)");
                    DisposeProcessorResources(World.Unmanaged, processorEntity);
                    processor.ValueRW.Source.Code = default;
                }
                continue;
            }

            if (!compilerSystem.CompiledSources.TryGetValue(processor.ValueRO.SourceFile, out CompiledSourceServer? source))
            {
                if (EnableLogging) Debug.Log($"{DebugEx.ServerPrefix} Creating new source file {processor.ValueRO.SourceFile} (internal)");
                DisposeProcessorResources(World.Unmanaged, processorEntity);
                compilerSystem.AddEmpty(processor.ValueRO.SourceFile, 1);
                processor.ValueRW.CompiledSourceVersion = 0;
                if (processor.ValueRW.Source.Code.IsCreated)
                {
                    if (EnableLogging) Debug.Log($"{DebugEx.ServerPrefix} Disposing instructions (new source made)");
                    processor.ValueRW.Source.Code = default;
                }
                continue;
            }

            if (!source.Code.HasValue)
            {
                DisposeProcessorResources(World.Unmanaged, processorEntity);
                if (processor.ValueRW.Source.Code.IsCreated)
                {
                    if (EnableLogging) Debug.Log($"{DebugEx.ServerPrefix} Disposing instructions (source has no instructions)");
                    processor.ValueRW.Source.Code = default;
                }
                continue;
            }

            if (processor.ValueRO.CompiledSourceVersion != source.CompiledVersion)
            {
                if (source.HotReloadVersion == source.CompiledVersion)
                {
                    if (EnableLogging) Debug.Log($"{DebugEx.ServerPrefix} New source version avaliable ({processor.ValueRO.CompiledSourceVersion} -> {source.CompiledVersion}), HOT RELOAD!!!");
                }
                else
                {
                    if (EnableLogging) Debug.Log($"{DebugEx.ServerPrefix} New source version avaliable ({processor.ValueRO.CompiledSourceVersion} -> {source.CompiledVersion}), reloading processor ...");
                    DisposeProcessorResources(World.Unmanaged, processorEntity);
                    ResetProcessor(ref processor.ValueRW);
                }
                processor.ValueRW.CompiledSourceVersion = source.CompiledVersion;
                processor.ValueRW.Source.Code = source.Code.Value.AsUnsafe().AsReadOnly();
                processor.ValueRW.Source.GeneratedFunctions = source.GeneratedFunctions?.AsUnsafe() ?? default;
                processor.ValueRW.Source.UnitCommandDefinitions = source.UnitCommandDefinitions?.AsUnsafe().AsReadOnly() ?? default;

                continue;
            }

            if (!source.IsSuccess)
            {
                DisposeProcessorResources(World.Unmanaged, processorEntity);
                if (processor.ValueRW.Source.Code.IsCreated)
                {
                    if (EnableLogging) Debug.Log($"{DebugEx.ServerPrefix} Disposing instructions (source has errors)");
                    processor.ValueRW.Source.Code = default;
                }
            }
        }
    }

    public static void DisposeProcessorResources(WorldUnmanaged world, Entity entity)
    {
        world.GetExistingSystemState<ProcessorSystemServer>().CompleteDependency();
        NativeList<EntityOwnedData<UserUIElement>> uiElements = world.GetSystem<ProcessorSystemServer>().uiElements;
        for (int i = 0; i < uiElements.Length; i++)
        {
            if (uiElements[i].OwnerEntity != entity) continue;
            uiElements[i] = default;
        }
    }

    public static void ResetProcessor(ref Processor processor)
    {
        ProcessorState processorState = new(
            ProcessorSystemServer.BytecodeInterpreterSettings,
            default,
            default,
            default,
            default
        );
        processorState.Setup();

        processor.StdOutBuffer.Clear();
        processor.StdOutBufferCursor = 0;
        processor.Registers = processorState.Registers;
        processor.Signal = processorState.Signal;
        processor.Crash = processorState.Crash;
    }
}
