#pragma warning disable CS0162 // Unreachable code detected

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LanguageCore;
using LanguageCore.BBLang.Generator;
using LanguageCore.Compiler;
using LanguageCore.Parser;
using LanguageCore.Parser.Statements;
using LanguageCore.Runtime;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Profiling;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial class CompilerSystemServer : SystemBase
{
    const bool EnableLogging = false;

    static readonly CompilerSettings CompilerSettings = new(CodeGeneratorForMain.DefaultCompilerSettings)
    {
        Optimizations = OptimizationSettings.All,
        Cache = CompilerCache,
    };

    static readonly MainGeneratorSettings GeneratorSettings = new(MainGeneratorSettings.Default)
    {
        StackSize = ProcessorSystemServer.BytecodeInterpreterSettings.StackSize,
        //ILGeneratorSettings = new LanguageCore.IL.Generator.ILGeneratorSettings()
        //{
        //    AllowCrash = false,
        //    AllowHeap = false,
        //    AllowPointers = true,
        //},
    };

    [NotNull] public readonly Dictionary<FileId, CompiledSourceServer>? CompiledSources = new();

    readonly List<(Task, CancellationTokenSource)> Tasks = new();

    protected override void OnUpdate()
    {
        EntityCommandBuffer commandBuffer = default;

        foreach ((RefRO<CompilerStatusRequestRpc> command, RefRO<ReceiveRpcCommandRequest> request, Entity entity) in
            SystemAPI.Query<RefRO<CompilerStatusRequestRpc>, RefRO<ReceiveRpcCommandRequest>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
            commandBuffer.DestroyEntity(entity);

            Debug.Log($"{DebugEx.ClientPrefix} Received compilation status request for \"{command.ValueRO.FileName}\"");

            if (!CompiledSources.TryGetValue(command.ValueRO.FileName, out var source))
            { continue; }

            SendCompilationStatus(source, commandBuffer, request.ValueRO.SourceConnection);
        }

        foreach ((FileId file, CompiledSourceServer source) in CompiledSources)
        {
            lock (source)
            {
                switch (source.Status)
                {
                    case CompilationStatus.None:
                        break;
                    case CompilationStatus.Secuedued:
                        source.Status = CompilationStatus.Compiling;
                        CancellationTokenSource cancellationTokenSource = new();
                        Tasks.Add((Task.Factory.StartNew(static v => CompileSourceTask(((FileId, bool, CompiledSourceServer, CancellationToken))v), (file, false, source, cancellationTokenSource.Token))
                            .ContinueWith(task =>
                            {
                                if (task.IsCanceled || (task.Exception is not null && task.Exception.InnerExceptions.Count == 1 && task.Exception.InnerExceptions[0] is TaskCanceledException))
                                { Debug.LogWarning($"{DebugEx.ServerPrefix} Compilation task cancelled"); }
                                else if (task.IsFaulted)
                                {
                                    foreach (Exception? ex in task.Exception!.Flatten().InnerExceptions)
                                    {
                                        Debug.LogException(ex);
                                    }
                                }
                            }), cancellationTokenSource));
                        if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
                        SendCompilationStatus(source, commandBuffer);
                        break;
                    case CompilationStatus.Compiling:
                    case CompilationStatus.Uploading:
                    case CompilationStatus.Generating:
                        break;
                    case CompilationStatus.Generated:
                        source.Status = CompilationStatus.Done;

                        if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
                        SendCompilationStatus(source, commandBuffer);
                        SendDiagnostics(source, commandBuffer);
                        break;
                    case CompilationStatus.Done:
                        if (source.CompiledVersion < source.LatestVersion)
                        {
                            Debug.Log($"{DebugEx.ServerPrefix} [{nameof(CompilerSystemServer)}] Source version changed ({source.CompiledVersion} -> {source.LatestVersion}), recompiling \"{source.SourceFile}\"");
                            source.Status = CompilationStatus.Secuedued;
                            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
                            SendCompilationStatus(source, commandBuffer);
                        }
                        break;
                    default:
                        throw new UnreachableException();
                }

                if (source.StatusChanged && source.LastStatusSync + 0.5d < SystemAPI.Time.ElapsedTime)
                {
                    if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
                    SendCompilationStatus(source, commandBuffer);
                }
            }
        }
    }

    protected override void OnDestroy()
    {
        if (Tasks.Count > 0)
        {
            Debug.Log($"{DebugEx.ServerPrefix} Cancelling {Tasks.Count} compilation tasks");
            foreach ((Task task, CancellationTokenSource? cancellationTokenSource) in Tasks)
            {
                if (task.IsCompleted) continue;
                cancellationTokenSource.Cancel();
            }

            Debug.Log($"{DebugEx.ServerPrefix} Waiting for {Tasks.Count} compilation tasks to finish ...");
            Task.WaitAll(Tasks.Select(v => v.Item1).ToArray(), 10000);

            Debug.Log($"{DebugEx.ServerPrefix} Compilation tasks completed");
        }
    }

    void SendCompilationStatus(CompiledSourceServer source, EntityCommandBuffer commandBuffer, Entity connectionEntity = default)
    {
        source.LastStatusSync = SystemAPI.Time.ElapsedTime;
        source.StatusChanged = false;

        if (World.Unmanaged.IsLocal()) return;

        {
            NetcodeUtils.CreateRPC(commandBuffer, World.Unmanaged, new CompilerStatusRpc()
            {
                FileName = source.SourceFile,
                Status = source.Status,
                Progress = source.Progress,
                IsSuccess = source.IsSuccess,
                CompiledVersion = source.CompiledVersion,
                LatestVersion = source.LatestVersion,
                UnitCommands = source.UnitCommandDefinitions?.Length ?? 0,
            }, connectionEntity);
            if (EnableLogging) Debug.Log($"{DebugEx.ServerPrefix} [{nameof(CompilerSystemServer)}] Sending compilation status for {source.SourceFile} to {source.SourceFile.Source}");
        }

        if (source.IsSuccess && source.UnitCommandDefinitions.HasValue)
        {
            FixedList64Bytes<UnitCommandParameter> parameters = new();

            for (int i = 0; i < source.UnitCommandDefinitions.Value.Length; i++)
            {
                UnitCommandDefinition item = source.UnitCommandDefinitions.Value[i];
                unsafe
                {
                    parameters.Clear();
                    parameters.AddRange(&item.Parameters, item.ParameterCount);
                }
                NetcodeUtils.CreateRPC(commandBuffer, World.Unmanaged, new UnitCommandDefinitionRpc()
                {
                    FileName = source.SourceFile,
                    Index = i,
                    Id = item.Id,
                    Label = item.Label,
                    Parameters = parameters,
                }, connectionEntity);
            }
        }

        foreach ((FileId name, ProgressRecord<(int Current, int Total)> value) in source.SubFiles.ToArray())
        {
            NetcodeUtils.CreateRPC(commandBuffer, World.Unmanaged, new CompilerSubstatusRpc()
            {
                FileName = source.SourceFile,
                SubFileName = name,
                CurrentProgress = value.Progress.Current,
                TotalProgress = value.Progress.Total,
            }, connectionEntity);
        }
    }

    void SendDiagnostic(Diagnostic diagnostic, CompiledSourceServer source, EntityCommandBuffer commandBuffer, Dictionary<Diagnostic, uint> diagnosticIds, ref uint diagnosticIdCounter, uint parent)
    {
        if (World.Unmanaged.IsLocal()) return;

        FileId file = default;
        Position position = default;

        if (diagnostic is DiagnosticAt _diagnostic)
        {
            if (_diagnostic.File is null) return;
            if (!FileId.FromUri(_diagnostic.File, out file)) return;
            position = _diagnostic.Position;
        }

        if (!diagnosticIds.TryGetValue(diagnostic, out uint id))
        {
            if (diagnosticIdCounter == uint.MaxValue) return;
            diagnosticIds.Add(diagnostic, id = diagnosticIdCounter++);
        }

        NetcodeUtils.CreateRPC(commandBuffer, World.Unmanaged, new CompilationAnalysticsRpc()
        {
            Id = id,
            Parent = parent,
            Source = source.SourceFile,
            FileName = file,
            Position = position.Range.ToMutable(),
            AbsolutePosition = position.AbsoluteRange.ToMutable(),
            Level = diagnostic.Level,
            Message = diagnostic.Message,
        });

        foreach (Diagnostic subdiagnostic in diagnostic.SubErrors)
        {
            SendDiagnostic(subdiagnostic, source, commandBuffer, diagnosticIds, ref diagnosticIdCounter, id);
        }
    }

    void SendDiagnostics(CompiledSourceServer source, EntityCommandBuffer commandBuffer)
    {
        if (World.Unmanaged.IsLocal()) return;

        Dictionary<Diagnostic, uint> diagnosticIds = new();
        uint diagnosticIdCounter = 1;

        // .ToArray() because the collection can be modified somewhere idk
        foreach (DiagnosticAt item in source.Diagnostics.Diagnostics.ToArray())
        {
            if (item.Level == DiagnosticsLevel.Error) Debug.LogWarning($"{DebugEx.ServerPrefix} [{nameof(CompilerSystemServer)}] {item}\r\n{item.GetArrows()}");

            SendDiagnostic(item, source, commandBuffer, diagnosticIds, ref diagnosticIdCounter, 0);
        }

        // .ToArray() because the collection can be modified somewhere idk
        foreach (Diagnostic item in source.Diagnostics.DiagnosticsWithoutContext.ToArray())
        {
            if (item.Level == DiagnosticsLevel.Error) Debug.LogWarning($"{DebugEx.ServerPrefix} [{nameof(CompilerSystemServer)}] {item}");

            NetcodeUtils.CreateRPC(commandBuffer, World.Unmanaged, new CompilationAnalysticsRpc()
            {
                Id = 0,
                Source = source.SourceFile,
                Level = item.Level,
                Message = item.Message,
            });
        }
    }

    static readonly ProfilerMarker _markerCompiler = new("Compiler");
    static readonly ProfilerMarker _markerCompilerCompilation = new("Compiler.Compilation");
    static readonly ProfilerMarker _markerCompilerGeneration = new("Compiler.Generation");

    static readonly ConcurrentDictionary<Uri, CacheItem> CompilerCache = new();

    static unsafe void CompileSourceTask((FileId File, bool Force, CompiledSourceServer Source, CancellationToken CancellationToken) args)
    {
        using ProfilerMarker.AutoScope _m = _markerCompiler.Auto();

        (FileId file, bool force, CompiledSourceServer source, CancellationToken cancellationToken) = args;

        Uri sourceUri = file.ToUri();
        if (EnableLogging) Debug.Log($"{DebugEx.ServerPrefix} [{nameof(CompilerSystemServer)}] Compilation started for \"{sourceUri}\" ...");

        lock (source)
        {
            source.Diagnostics = new DiagnosticsCollection();
            source.Status = CompilationStatus.Uploading;
            source.StatusChanged = true;
        }

        List<ProgressRecord<(int, int)>> progresses = new();

        ImmutableArray<UserDefinedAttribute> attributes = ImmutableArray.Create<UserDefinedAttribute>(
            new("UnitCommand", ImmutableArray.Create(LiteralType.Integer, LiteralType.String), CanUseOn.Struct, static (IHaveAttributes context, AttributeUsage attribute, [NotNullWhen(false)] out PossibleDiagnostic? error) =>
            {
                if (context is not CompiledStruct @struct)
                {
                    error = new PossibleDiagnostic($"Attribute `UnitCommand` is only valid on struct declarations", attribute);
                    return false;
                }

                error = null;
                return true;
            }),
            new("Context", ImmutableArray.Create(LiteralType.String), CanUseOn.Field, static (IHaveAttributes context, AttributeUsage attribute, [NotNullWhen(false)] out PossibleDiagnostic? error) =>
            {
                if (context is not CompiledField field)
                {
                    error = new PossibleDiagnostic($"Attribute `Context` is only valid on field declarations", attribute);
                    return false;
                }

                if (!field.Context.Definition.Attributes.TryGetAttribute("UnitCommand", out _))
                {
                    error = new PossibleDiagnostic($"The struct should be flagged with [UnitCommand] attribute", attribute);
                    return false;
                }

                if (attribute.Parameters[0] is not StringLiteralExpression stringLiteral)
                {
                    error = new PossibleDiagnostic($"The struct should be flagged with [UnitCommand] attribute", attribute);
                    return false;
                }

                switch (stringLiteral.Value)
                {
                    case "position2":
                    {
                        if (!StatementCompiler.FindSize(field.Type, out int size, out error, new CodeGeneratorForMain(CompilerResult.MakeEmpty(null!), MainGeneratorSettings.Default, new())))
                        {
                            return false;
                        }

                        if (size != sizeof(float2))
                        {
                            error = new PossibleDiagnostic($"Fields with unit command context \"{stringLiteral.Value}\" should be a size of {sizeof(float2)} (a 2D float vector) (type {field.Type} has a size of {size} bytes)");
                            return false;
                        }

                        break;
                    }
                    case "position3":
                    {
                        if (!StatementCompiler.FindSize(field.Type, out int size, out error, new CodeGeneratorForMain(CompilerResult.MakeEmpty(null!), MainGeneratorSettings.Default, new())))
                        {
                            return false;
                        }

                        if (size != sizeof(float3))
                        {
                            error = new PossibleDiagnostic($"Fields with unit command context \"{stringLiteral.Value}\" should be a size of {sizeof(float3)} (a 3D float vector) (type {field.Type} has a size of {size} bytes)");
                            return false;
                        }

                        break;
                    }
                    default:
                    {
                        error = new PossibleDiagnostic($"Unknown unit command context \"{stringLiteral.Value}\"", stringLiteral);
                        return false;
                    }
                }

                error = null;
                return true;
            })
        );

        CompilerResult compiled = CompilerResult.MakeEmpty(sourceUri);
        BBLangGeneratorResult generated = new()
        {
            Code = ImmutableArray<Instruction>.Empty,
            DebugInfo = null,
        };
        try
        {
            IExternalFunction[] externalFunctions = ProcessorAPI.GenerateManagedExternalFunctions();

            Debug.Log($"{DebugEx.ServerPrefix} Compiling {sourceUri} ...");

            lock (source)
            {
                source.Status = CompilationStatus.Uploading;
                source.StatusChanged = true;
            }

            using (ProfilerMarker.AutoScope _2 = _markerCompilerCompilation.Auto())
            {
                compiled = StatementCompiler.CompileFile(
                    sourceUri.ToString(),
                    new CompilerSettings(CompilerSettings)
                    {
                        UserDefinedAttributes = attributes,
                        ExternalFunctions = externalFunctions.ToImmutableArray(),
                        SourceProviders = ImmutableArray.Create<ISourceProvider>(
                            new NetcodeSourceProvider(source, progresses, EnableLogging)
                        ),
                        CancellationToken = cancellationToken,
                    },
                    source.Diagnostics
                );
            }

            lock (source)
            {
                source.Status = CompilationStatus.Generating;
                source.StatusChanged = true;
            }

            cancellationToken.ThrowIfCancellationRequested();

            Debug.Log($"{DebugEx.ServerPrefix} Generating \"{sourceUri}\" ...");

            using (ProfilerMarker.AutoScope _2 = _markerCompilerGeneration.Auto())
            {
                generated = CodeGeneratorForMain.Generate(
                    compiled,
                    new MainGeneratorSettings(GeneratorSettings)
                    {
                        CancellationToken = cancellationToken,
                    },
                    null,
                    source.Diagnostics
                );
            }

            cancellationToken.ThrowIfCancellationRequested();

            //if (file.Name == "unit-6.bbc")
            //{
            //    const string _out = "/home/bb/Projects/BBLang/Core/Utility/out2.il";
            //    System.IO.File.WriteAllBytes(_out, Array.Empty<byte>());
            //    using System.IO.FileStream stream = System.IO.File.OpenWrite(_out);
            //    using System.IO.StreamWriter writer = new(stream);
            //    generated.CodeEmitter.WriteTo(writer, false);
            //    Debug.Log($"Bytecode has been written to {_out}");
            //}

            //using (StreamWriter stringWriter = new($"/home/bb/Projects/BBLang/Core/out-{DateTime.Now:O}-{file.Name.ToString().Replace('/', '_')}.bbc"))
            //{
            //    stringWriter.WriteLine(compiled.Stringify());
            //    if (!generated.ILGeneratorBuilders.IsDefault)
            //    {
            //        foreach (string builder in generated.ILGeneratorBuilders)
            //        {
            //            stringWriter.WriteLine(builder);
            //        }
            //    }
            //}

            Debug.Log($"{DebugEx.ServerPrefix} {sourceUri} done");
        }
        catch (LanguageExceptionAt exception)
        {
            lock (source)
            {
                source.IsSuccess = false;
                source.Diagnostics.Add(exception.ToDiagnostic());
            }
        }
        catch (LanguageException exception)
        {
            lock (source)
            {
                source.IsSuccess = false;
                source.Diagnostics.Add(exception.ToDiagnostic());
            }
        }
        catch (TaskCanceledException)
        {
            Debug.LogWarning($"{DebugEx.ServerPrefix} Compilation canceled");
            source.Status = CompilationStatus.Generated;
            source.StatusChanged = true;
            source.Progress = float.NaN;
            return;
        }

        if (source.Diagnostics.HasErrors)
        {
            lock (source)
            {
                source.Status = CompilationStatus.Generated;
                source.Compiled = compiled;
                source.Generated = generated;
                Debug.Log($"{DebugEx.ServerPrefix} Updating source version ({source.CompiledVersion} -> {source.LatestVersion})");
                source.CompiledVersion = source.LatestVersion;
                source.IsSuccess = false;

                source.DebugInformation = new CompiledDebugInformation(null);
                source.Code?.Dispose();
                source.Code = default;
                source.GeneratedFunction?.Dispose();
                source.GeneratedFunction = default;
                source.UnitCommandDefinitions?.Dispose();
                source.UnitCommandDefinitions = default;

                source.Progress = float.NaN;
            }
        }
        else
        {
            lock (source)
            {
                source.Compiled = compiled;
                source.Generated = generated;
                source.DebugInformation = new CompiledDebugInformation(generated.DebugInfo);
                source.Code?.Dispose();
                source.Code = new NativeArray<Instruction>(generated.Code.ToArray(), Allocator.Persistent);
                source.GeneratedFunction?.Dispose();
                source.GeneratedFunction = new NativeArray<ExternalFunctionScopedSync>(generated.GeneratedUnmanagedFunctions.ToArray(), Allocator.Persistent);
                source.UnitCommandDefinitions?.Dispose();
                List<UnitCommandDefinition> commandDefinitions = new();
                foreach (CompiledStruct @struct in source.Compiled.Structs)
                {
                    if (!@struct.Definition.Attributes.TryGetAttribute("UnitCommand", out AttributeUsage? structAttribute))
                    { continue; }

                    if (structAttribute.Parameters[0] is not IntLiteralExpression idArgument) continue;
                    if (structAttribute.Parameters[1] is not StringLiteralExpression nameArgument) continue;

                    FixedList32Bytes<UnitCommandParameter> parameterTypes = new();
                    bool ok = true;

                    foreach (CompiledField field in @struct.Fields)
                    {
                        if (!field.Definition.Attributes.TryGetAttribute("Context", out AttributeUsage? attribute)) continue;
                        if (attribute.Parameters[0] is not StringLiteralExpression stringLiteral) continue;
                        switch (stringLiteral.Value)
                        {
                            case "position2":
                                parameterTypes.Add(UnitCommandParameter.Position2);
                                break;
                            case "position3":
                                parameterTypes.Add(UnitCommandParameter.Position3);
                                break;
                            default:
                                Debug.LogError($"{DebugEx.ServerPrefix} Invalid unit command field context `{stringLiteral.Value}`");
                                ok = false;
                                break;
                        }
                    }

                    if (!ok) continue;

                    commandDefinitions.Add(new(idArgument.Value, nameArgument.Value, parameterTypes));
                }
                source.UnitCommandDefinitions = new(commandDefinitions.ToArray(), Allocator.Persistent);

                source.Status = CompilationStatus.Generated;
                Debug.Log($"{DebugEx.ServerPrefix} Updating source version ({source.CompiledVersion} -> {source.LatestVersion})");
                source.CompiledVersion = source.LatestVersion;
                source.IsSuccess = true;
                source.Progress = float.NaN;
            }
        }
    }

    public void AddEmpty(FileId file, long latestVersion) => CompiledSources.Add(file, new(
        file,
        default,
        latestVersion,
        default,
        CompilationStatus.Secuedued,
        0,
        false,
        default,
        default,
        default,
        new DiagnosticsCollection()
    ));
}
