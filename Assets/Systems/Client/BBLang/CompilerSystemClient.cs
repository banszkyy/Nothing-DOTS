using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using LanguageCore;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial class CompilerSystemClient : SystemBase
{
    readonly Dictionary<FileId, CompiledSourceClient> CompiledSources = new();
    readonly Dictionary<FileId, float> StatusRequestTimestamps = new();
    const float StatusRequestCooldownSec = 2f;

    protected override void OnUpdate()
    {
        EntityCommandBuffer commandBuffer = default;

        foreach ((RefRO<CompilerStatusRpc> command, Entity entity) in
            SystemAPI.Query<RefRO<CompilerStatusRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
            commandBuffer.DestroyEntity(entity);

            Debug.Log($"{DebugEx.ClientPrefix} Received compilation status `{command.ValueRO.Status}` for \"{command.ValueRO.FileName}\"");

            if (CompiledSources.TryGetValue(command.ValueRO.FileName, out CompiledSourceClient? source))
            {
                Debug.Log($"{DebugEx.ClientPrefix} Disposing old source \"{command.ValueRO.FileName}\"");

                source.Code?.Dispose();
                source.GeneratedFunction?.Dispose();
                source.UnitCommandDefinitions?.Dispose();

                source.Code = default;
                source.GeneratedFunction = default;
                source.UnitCommandDefinitions = default;
            }

            CompiledSources[command.ValueRO.FileName] = new(
                command.ValueRO.FileName,
                command.ValueRO.CompiledVersion,
                command.ValueRO.LatestVersion,
                command.ValueRO.Status,
                command.ValueRO.Progress,
                command.ValueRO.IsSuccess,
                new NativeArray<UnitCommandDefinition>(command.ValueRO.UnitCommands, Allocator.Persistent)
            );
        }

        foreach ((RefRO<CompilerSubstatusRpc> command, Entity entity) in
            SystemAPI.Query<RefRO<CompilerSubstatusRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
            commandBuffer.DestroyEntity(entity);

            if (!CompiledSources.TryGetValue(command.ValueRO.FileName, out CompiledSourceClient source))
            {
                Debug.LogWarning($"{DebugEx.ClientPrefix} Received substatus for unknown compiled source \"{command.ValueRO.FileName}\"");
                continue;
            }

            source.SubFiles.TryAdd(command.ValueRO.SubFileName, new ProgressRecord<(int Current, int Total)>(null));
            source.SubFiles[command.ValueRO.SubFileName].Report((command.ValueRO.CurrentProgress, command.ValueRO.TotalProgress));
        }

        foreach ((RefRO<UnitCommandDefinitionRpc> command, Entity entity) in
            SystemAPI.Query<RefRO<UnitCommandDefinitionRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
            commandBuffer.DestroyEntity(entity);

            if (!CompiledSources.TryGetValue(command.ValueRO.FileName, out CompiledSourceClient source))
            {
                Debug.LogWarning($"{DebugEx.ClientPrefix} Received unit command for unknown compiled source \"{command.ValueRO.FileName}\"");
                continue;
            }

            if (!source.UnitCommandDefinitions.HasValue)
            {
                Debug.LogWarning($"{DebugEx.ClientPrefix} Received unit command for compiled source \"{command.ValueRO.FileName}\" but the array is not created");
                continue;
            }

            source.UnitCommandDefinitions.Value.AsSpan()[command.ValueRO.Index] = new(
                command.ValueRO.Id,
                command.ValueRO.Label,
                command.ValueRO.Parameters
            );
        }

        foreach ((RefRO<CompilationAnalysticsRpc> command, Entity entity) in
            SystemAPI.Query<RefRO<CompilationAnalysticsRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
            commandBuffer.DestroyEntity(entity);

            if (!CompiledSources.TryGetValue(command.ValueRO.Source, out CompiledSourceClient source))
            {
                Debug.LogWarning($"{DebugEx.ClientPrefix} Received diagnostics for unknown compiled source \"{command.ValueRO.FileName}\"");
                continue;
            }

            IReadOnlyList<CompilationAnalysticsRpc> TakeOrphanSubdiagnostics(uint parent)
            {
                List<CompilationAnalysticsRpc> result = new();
                for (int i = source.OrphanDiagnostics.Count - 1; i >= 0; i--)
                {
                    if (source.OrphanDiagnostics[i].Parent == parent)
                    {
                        source.OrphanDiagnostics.RemoveAt(i);
                        result.Add(source.OrphanDiagnostics[i]);
                    }
                }
                return result;
            }

            ClientSimpleDiagnostic ToDiagnostic(CompilationAnalysticsRpc diagnostic)
            {
                return new ClientSimpleDiagnostic(
                    diagnostic.Id,
                    command.ValueRO.Level,
                    command.ValueRO.Message.ToString(),
                    new Position(command.ValueRO.Position, command.ValueRO.AbsolutePosition),
                    command.ValueRO.FileName.ToUri(),
                    TakeOrphanSubdiagnostics(diagnostic.Id).ToImmutableArray(ToDiagnostic)
                );
            }

            if (command.ValueRO.Parent != 0)
            {
                for (int i = 0; i < source.ClientDiagnostics.Count; i++)
                {
                    ClientSimpleDiagnostic item = source.ClientDiagnostics[i];
                    if (item.Id == command.ValueRO.Parent)
                    {
                        item.SubErrors.Add(ToDiagnostic(command.ValueRO));
                        goto found;
                    }
                }
                source.OrphanDiagnostics.Add(command.ValueRO);
            found:;
            }
            else
            {
                source.ClientDiagnostics.Add(ToDiagnostic(command.ValueRO));
            }
        }
    }

    public bool TryGetSource(in FileId file, [NotNullWhen(true)] out CompiledSourceClient source, in WorldUnmanaged world, in EntityCommandBuffer commandBuffer)
    {
        if (CompiledSources.TryGetValue(file, out source)) return true;

        float now = MonoTime.Now;
        if (StatusRequestTimestamps.TryGetValue(file, out float time) && now - time < StatusRequestCooldownSec) return false;
        StatusRequestTimestamps[file] = now;

        NetcodeUtils.CreateRPC(commandBuffer, world, new CompilerStatusRequestRpc()
        {
            FileName = file,
        });

        return false;
    }

    public bool TryGetSource(in FileId file, [NotNullWhen(true)] out CompiledSourceClient source, in WorldUnmanaged world)
    {
        if (CompiledSources.TryGetValue(file, out source)) return true;

        float now = MonoTime.Now;
        if (StatusRequestTimestamps.TryGetValue(file, out float time) && now - time < StatusRequestCooldownSec) return false;
        StatusRequestTimestamps[file] = now;

        NetcodeUtils.CreateRPC(world, new CompilerStatusRequestRpc()
        {
            FileName = file,
        });

        return false;
    }

    public bool TryGetSource(in FileId file, [NotNullWhen(true)] out CompiledSourceClient source) => CompiledSources.TryGetValue(file, out source);

    public void OnDisconnect()
    {
        Debug.Log($"{DebugEx.ClientPrefix} Clearing compiled sources");

        CompiledSources.Clear();
        StatusRequestTimestamps.Clear();
    }
}
