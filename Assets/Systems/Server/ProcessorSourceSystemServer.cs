using System;
using LanguageCore.Runtime;
using Unity.Entities;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial class ProcessorSourceSystemServer : SystemBase
{
    const bool EnableLogging = false;

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

            foreach (var (ghostInstance, processor) in
                SystemAPI.Query<RefRO<GhostInstance>, RefRW<Processor>>())
            {
                if (!command.ValueRO.Entity.Equals(ghostInstance.ValueRO)) continue;

                switch (command.ValueRO.Command)
                {
                    case ProcessorCommand.Halt:
                        processor.ValueRW.Signal = Signal.Halt;
                        break;
                    case ProcessorCommand.Reset:
                        ResetProcessor(processor);
                        break;
                    case ProcessorCommand.Continue:
                        processor.ValueRW.Signal = Signal.None;
                        processor.ValueRW.Crash = 0;
                        break;
                    case ProcessorCommand.Key:
                        if (processor.ValueRW.InputKey.Length >= processor.ValueRW.InputKey.Capacity)
                        {
                            Debug.LogWarning($"{DebugEx.ServerPrefix} Standard input buffer is full");
                            break;
                        }
                        processor.ValueRW.InputKey.Add(unchecked((char)command.ValueRO.Data));
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

            foreach (var (ghostInstance, processor) in
                SystemAPI.Query<RefRO<GhostInstance>, RefRW<Processor>>())
            {
                NetcodeEndPoint ep;
                if (request.ValueRO.SourceConnection == default)
                {
                    ep = NetcodeEndPoint.Server;
                }
                else
                {
                    ep = new(SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO, request.ValueRO.SourceConnection);
                    if (!World.IsServer()) ep = NetcodeEndPoint.Server;
                }

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

        foreach (var processor in
            SystemAPI.Query<RefRW<Processor>>())
        {
            if (processor.ValueRO.SourceFile == default)
            {
                if (processor.ValueRW.Source.Code.IsCreated)
                {
                    if (EnableLogging) Debug.Log($"{DebugEx.ServerPrefix} Disposing instructions (source file is null)");
                    processor.ValueRW.Source.Code = default;
                }
                continue;
            }

            if (!compilerSystem.CompiledSources.TryGetValue(processor.ValueRO.SourceFile, out CompiledSourceServer? source))
            {
                if (EnableLogging) Debug.Log($"{DebugEx.ServerPrefix} Creating new source file {processor.ValueRO.SourceFile} (internal)");
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
                    ResetProcessor(processor);
                }
                processor.ValueRW.CompiledSourceVersion = source.CompiledVersion;
                processor.ValueRW.Source.Code = source.Code.Value.AsUnsafe().AsReadOnly();
                processor.ValueRW.Source.GeneratedFunctions = source.GeneratedFunctions?.AsUnsafe() ?? default;
                processor.ValueRW.Source.UnitCommandDefinitions = source.UnitCommandDefinitions?.AsUnsafe().AsReadOnly() ?? default;

                continue;
            }

            if (!source.IsSuccess)
            {
                if (processor.ValueRW.Source.Code.IsCreated)
                {
                    if (EnableLogging) Debug.Log($"{DebugEx.ServerPrefix} Disposing instructions (source has errors)");
                    processor.ValueRW.Source.Code = default;
                }
            }
        }
    }

    public static void ResetProcessor(RefRW<Processor> processor)
    {
        ProcessorState processorState_ = new(
            ProcessorSystemServer.BytecodeInterpreterSettings,
            default,
            default,
            default,
            default
        );
        processorState_.Setup();

        processor.ValueRW.StdOutBuffer.Clear();
        processor.ValueRW.StdOutBufferCursor = 0;
        processor.ValueRW.Registers = processorState_.Registers;
        processor.ValueRW.Signal = processorState_.Signal;
        processor.ValueRW.Crash = processorState_.Crash;
    }
}
