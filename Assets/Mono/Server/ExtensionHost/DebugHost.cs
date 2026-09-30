using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using DebugServer;
using LanguageCore;
using LanguageCore.Compiler;
using LanguageCore.Runtime;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

partial class DebugHost : BytecodeDebugAdapterBase, IDisposable
{
    ProcessorSource _originalSource;
    FileId _originalFile;
    Entity _entity;
    Guid _playerGuid;
    int _playerConnectionId;
    Entity _playerEntity;
    readonly DebugHostManager _manager;
    StopContext? _lastStopContext;
    uint _unitLogPosition;
    protected bool _isDisconnected;

    protected override bool IsStopped
    {
        get
        {
            EntityManager e = ConnectionManager.ServerOrDefaultWorld.EntityManager;
            if (!e.Exists(_entity)) return false;

            Processor processor = e.GetComponentData<Processor>(_entity);
            return processor.DebugContext.IsBeingDebugged
                && processor.DebugContext.Stopped
                is ProcessorJob.StopReason.Pause
                or ProcessorJob.StopReason.Breakpoint
                or ProcessorJob.StopReason.Signal
                or ProcessorJob.StopReason.RuntimeException
                or ProcessorJob.StopReason.StepForward
                or ProcessorJob.StopReason.StepIn
                or ProcessorJob.StopReason.StepOut
                or ProcessorJob.StopReason.StepInstruction;
        }
    }

    ulong _unitTerminalPosition;
    readonly List<byte> _unitTerminalBuilder = new();

    protected override CompilerSettings CompilerSettings => CompilerSystemServer.CompilerSettings;

    public DebugHost(DebugHostManager manager, Stream stdIn, Stream stdOut, Logger log) : base(stdIn, stdOut, log)
    {
        _manager = manager;
    }

    unsafe bool GetProcessor([NotNullWhen(true)] out ReadOnlyProcessorState processor)
    {
        processor = default;

        EntityManager e = ConnectionManager.ServerOrDefaultWorld.EntityManager;
        if (!e.Exists(_entity)) return false;

        Processor _processor = e.GetComponentData<Processor>(_entity);
        if (_processor.Source != _originalSource) return false;

        processor = new ReadOnlyProcessorState(
            ProcessorSystemServer.BytecodeInterpreterSettings,
            _processor.Registers,
            new Span<byte>(Unsafe.AsPointer(ref _processor.Memory), global::Processor.TotalMemorySize),
            _processor.Source.Code.AsSpan(),
            _processor.Source.GeneratedFunctions.AsSpan(),
            _processor.Crash,
            _processor.Signal
        );
        return true;
    }

    bool GetSource(FileId file, [NotNullWhen(true)] out CompiledSourceServer? source)
    {
        CompilerSystemServer compilerSystem = ConnectionManager.ServerOrDefaultWorld.GetExistingSystemManaged<CompilerSystemServer>();
        return compilerSystem.CompiledSources.TryGetValue(file, out source);
    }

    protected override CompilerResult Compiled => GetSource(_originalFile, out CompiledSourceServer? source) ? source.Compiled : CompilerResult.MakeEmpty(_originalFile.ToUri());
    protected override ReadOnlyProcessorState Processor => GetProcessor(out ReadOnlyProcessorState processor) ? processor : default;
    protected override CompiledDebugInformation DebugInformation => GetSource(_originalFile, out CompiledSourceServer? source) ? source.DebugInformation : new(null);

    void RefreshProcessorBreakpoints()
    {
        EntityManager e = ConnectionManager.ServerOrDefaultWorld.EntityManager;
        if (e.Exists(_entity))
        {
            Processor _processor = e.GetComponentData<Processor>(_entity);
            _processor.DebugContext.Breakpoints.Clear();
            foreach ((Breakpoint Breakpoint, InstructionBreakpoint InstructionBreakpoint, int Address) item in _instructionBreakpoints)
            {
                _processor.DebugContext.Breakpoints.Add((ushort)item.Address);
            }
            foreach (CompiledBreakpoint? item in _breakpoints.Values.SelectMany(v => v))
            {
                _processor.DebugContext.Breakpoints.Add((ushort)item.Instruction);
            }
            e.SetComponentData(_entity, _processor);
        }
    }

    public static string? GetFilePath(FileId fileId, Guid playerGuid)
    {
        if (fileId == default) return null;

        if (ConnectionManager.ServerOrDefaultWorld.IsServer() || ConnectionManager.ServerOrDefaultWorld.IsLocal())
        {
            if (fileId.Source.IsServer)
            {
                return FileChunkManagerSystem.ResolveFile(fileId.Name.ToString());
            }
            else
            {
                if (playerGuid == default)
                {
                    goto authenticated;
                }
                else
                {
                    using EntityQuery playerQ = ConnectionManager.ServerOrDefaultWorld.EntityManager.CreateEntityQuery(typeof(Player), typeof(RealPlayer));
                    using NativeArray<Entity> playerEntities = playerQ.ToEntityArray(Allocator.Temp);

                    for (int i = 0; i < playerEntities.Length; i++)
                    {
                        var player = ConnectionManager.ServerOrDefaultWorld.EntityManager.GetComponentData<Player>(playerEntities[i]);
                        var playerR = ConnectionManager.ServerOrDefaultWorld.EntityManager.GetComponentData<RealPlayer>(playerEntities[i]);
                        if (playerR.ConnectionId == fileId.Source.ConnectionId.Value)
                        {
                            if (player.Guid == playerGuid)
                            {
                                goto authenticated;
                            }
                            break;
                        }
                    }

                    return null;
                }

            authenticated:
                FileChunkManagerSystem fileManager = ConnectionManager.ServerOrDefaultWorld.GetExistingSystemManaged<FileChunkManagerSystem>();
                if (fileManager.TryGetRemoteFile(fileId, out RemoteFile remoteFile))
                {
                    return remoteFile.RemotePath.ToString(); // FIXME: this will expose all other client's local paths
                }
            }
        }
        else
        {
            Debug.LogError($"Not implemented: ServerOrDefaultWorld is neither a server or a local world");
        }
        //else if (ConnectionManager.ServerOrDefaultWorld.IsClient())
        //{
        //    using EntityQuery q = ConnectionManager.ServerOrDefaultWorld.EntityManager.CreateEntityQuery(typeof(LocalConnection));
        //    if (q.TryGetSingletonEntity<LocalConnection>(out var localConnection))
        //    {
        //        NetworkId localNetworkId = ConnectionManager.ServerOrDefaultWorld.EntityManager.GetComponentData<NetworkId>(localConnection);
        //        if (fileId.Source.ConnectionId == localNetworkId)
        //        {
        //            return FileChunkManagerSystem.ResolveFile(fileId.Name.ToString());
        //        }
        //    }
        //    else
        //    {
        //        Debug.LogError($"Failed to get LocalConnection singleton entity");
        //    }
        //}

        return null;
    }

    [return: NotNullIfNotNull(nameof(file))]
    protected override Source? ToSource(Uri? file)
    {
        if (file is null) return null;

        if (!FileId.FromUri(file, out FileId fileId))
        {
            return new Source()
            {
                Name = Path.GetFileName(file.ToString()),
                Path = file.ToString(),
                Origin = null,
                AdapterData = file.ToString(),
            };
        }

        string? localFile = GetFilePath(fileId, _playerGuid);

        if (localFile is not null)
        {
            return new Source()
            {
                Name = Path.GetFileName(file.ToString()),
                Path = localFile,
                Origin = fileId.Source.ToString(),
                AdapterData = file.ToString(),
            };
        }

        return new Source()
        {
            Name = Path.GetFileName(file.ToString()),
            Path = file.ToString(),
            Origin = fileId.Source.ToString(),
            AdapterData = file.ToString(),
        };
    }

    [return: NotNullIfNotNull(nameof(source))]
    protected override Uri? ToUri(Source? source)
    {
        if (source is null) return null;

        if (source.AdapterData is string adapterData && Uri.TryCreate(adapterData, UriKind.Absolute, out Uri? result))
        {
            return result;
        }

        return base.ToUri(source);
    }

    protected override void Continue(StopReason? step)
    {
        EntityManager e = ConnectionManager.ServerOrDefaultWorld.EntityManager;
        if (!e.Exists(_entity)) return;

        Processor processor = e.GetComponentData<Processor>(_entity);

        Continue(step, ref processor);

        e.SetComponentData(_entity, processor);
    }

    void Continue(StopReason? step, ref Processor processor)
    {
        if (step is not null)
        {
            processor.DebugContext.SkipCurrentBreakpoint = true;
            RequestStop(step, ref processor);
        }
        else
        {
            processor.DebugContext.Stopped = ProcessorJob.StopReason.No;
            processor.DebugContext.IsStopUnhandled = false;
            processor.DebugContext.SkipCurrentBreakpoint = true;
            _lastStopContext = null;
        }
    }

    protected override void RequestStop(StopReason reason)
    {
        if (NoDebug) throw new InvalidOperationException($"Cannot stop the runtime in no-debug mode");

        EntityManager e = ConnectionManager.ServerOrDefaultWorld.EntityManager;
        if (e.Exists(_entity))
        {
            Processor processor = e.GetComponentData<Processor>(_entity);
            RequestStop(reason, ref processor);
            e.SetComponentData(_entity, processor);
        }
    }

    void RequestStop(StopReason reason, ref Processor processor)
    {
        processor.DebugContext.Stopped = reason switch
        {
            StopReason_Crash => ProcessorJob.StopReason.Signal,
            StopReason_Breakpoint => ProcessorJob.StopReason.Breakpoint,
            StopReason_StepForward => ProcessorJob.StopReason.StepForwardUnfinished,
            StopReason_StepIn => ProcessorJob.StopReason.StepInUnfinished,
            StopReason_StepOut => ProcessorJob.StopReason.StepOutUnfinished,
            StopReason_StepInstruction => ProcessorJob.StopReason.StepInstructionUnfinished,
            StopReason_Pause => ProcessorJob.StopReason.Pause,
            _ => throw new NotImplementedException(),
        };
        processor.DebugContext.IsStopUnhandled = true;
    }

    protected override void SendKey(byte c)
    {
        EntityManager e = ConnectionManager.ServerOrDefaultWorld.EntityManager;
        if (!e.Exists(_entity)) return;

        Processor processor = e.GetComponentData<Processor>(_entity);

        if (processor.InputKey.Length >= processor.InputKey.Capacity)
        {
            Debug.LogWarning($"{DebugEx.ServerPrefix} Standard input buffer is full");
            return;
        }

        processor.InputKey.Add(c);
        e.SetComponentData(_entity, processor);
    }

    public override void Run()
    {
        Log.Info("Starting protocol");
        Protocol.Run();
    }

    public void Update()
    {
        if (Protocol.IsRunning && _isDisconnected)
        {
            Debug.Log($"{DebugEx.ServerPrefix} [DAP] Stopping protocol");
            Protocol.Stop();
        }

        if (_entity == Entity.Null)
        {
            return;
        }

        EntityManager e = ConnectionManager.ServerOrDefaultWorld.EntityManager;
        if (!e.Exists(_entity))
        {
            Protocol.SendEvent(new ExitedEvent() { ExitCode = 1 });
            Protocol.SendEvent(new TerminatedEvent());
            _entity = Entity.Null;
            return;
        }

        Processor processor = e.GetComponentData<Processor>(_entity);

        if (!processor.DebugContext.IsBeingDebugged)
        {
            Protocol.SendEvent(new ExitedEvent() { ExitCode = 1 });
            Protocol.SendEvent(new TerminatedEvent());
            _entity = Entity.Null;
            return;
        }

        {
            ulong beginOffset = Math.Max(0, processor.StdOutBufferCursor - (ulong)processor.StdOutBuffer.Length);
            ulong endOffset = processor.StdOutBufferCursor;

            Debug.Assert(endOffset >= beginOffset);
            Debug.Assert(endOffset - beginOffset == (ulong)processor.StdOutBuffer.Length);

            if (endOffset > _unitTerminalPosition)
            {
                ulong sendStart = Math.Max(_unitTerminalPosition, beginOffset);
                int offset = (int)(sendStart - beginOffset);
                ReadOnlySpan<byte> data = processor.StdOutBuffer.AsReadOnlySpan()[offset..];

                _unitTerminalPosition = sendStart + (ulong)data.Length;
                _unitTerminalBuilder.AddRange(data.ToArray());

                int i;
                while ((i = _unitTerminalBuilder.IndexOf((byte)'\n')) != -1)
                {
                    string line = Encoding.UTF8.GetString(_unitTerminalBuilder.ToArray().AsSpan()[..(i + 1)]);
                    _unitTerminalBuilder.RemoveRange(0, i + 1);

                    Protocol.SendEvent(new OutputEvent()
                    {
                        Category = OutputEvent.CategoryValue.Stdout,
                        Output = line,
                    });
                }
            }
        }

        {
            ReadOnlySpan<byte> logBuffer = e.GetBuffer<BufferedLogPiece>(_entity, true).AsNativeArray().Reinterpret<byte>().AsReadOnlySpan();

            for (int i = 0; i < logBuffer.Length;)
            {
                LogPieceType type = (LogPieceType)logBuffer[i];
                switch (type)
                {
                    case LogPieceType.Message:
                    {
                        break;
                    }
                    case LogPieceType.CombatTurret_Shoot:
                    {
                        UnitLog_CombatTurret_Shoot log = UnitLog_CombatTurret_Shoot.Read(logBuffer, ref i);
                        if (log.Header.Index <= _unitLogPosition) continue;
                        _unitLogPosition = log.Header.Index;
                        Protocol.SendEvent(new UnitLogEvent(log.Header));
                        break;
                    }
                    case LogPieceType.Command:
                    {
                        UnitLog_Command log = UnitLog_Command.Read(logBuffer, ref i);
                        if (log.Header.Index <= _unitLogPosition) continue;
                        _unitLogPosition = log.Header.Index;

                        ReadOnlySpan<UnitCommandDefinition> commandDefinitions = processor.Source.UnitCommandDefinitions.AsSpan();
                        var data = log.Data.AsReadOnlySpan();
                        for (int j = 0; j < commandDefinitions.Length; j++)
                        {
                            if (commandDefinitions[j].Id == log.CommandId)
                            {
                                object[] parameters = new object[commandDefinitions[j].ParameterCount];
                                int ptr = 0;
                                for (int k = 0; k < commandDefinitions[j].ParameterCount; k++)
                                {
                                    switch (commandDefinitions[j].GetParameter(k))
                                    {
                                        case UnitCommandParameter.Position2:
                                        {
                                            var v = data.Get<float2>(ptr);
                                            unsafe
                                            {
                                                ptr += sizeof(float2);
                                            }

                                            parameters[k] = new { v.x, v.y };
                                            break;
                                        }
                                        case UnitCommandParameter.Position3:
                                        {
                                            var v = data.Get<float3>(ptr);
                                            unsafe
                                            {
                                                ptr += sizeof(float3);
                                            }

                                            parameters[k] = new { v.x, v.y, v.z };
                                            break;
                                        }
                                        default:
                                            throw new UnreachableException();
                                    }
                                }
                                Protocol.SendEvent(new UnitLogEvent(log.Header, new
                                {
                                    id = log.CommandId,
                                    parameters,
                                }));
                                goto ok;
                            }
                        }

                        Protocol.SendEvent(new UnitLogEvent(log.Header, new
                        {
                            id = log.CommandId,
                            data = Convert.ToBase64String(data),
                        }));
                    ok:
                        break;
                    }
                    case LogPieceType.Radar:
                    {
                        UnitLog_Radar log = UnitLog_Radar.Read(logBuffer, ref i);
                        if (log.Header.Index <= _unitLogPosition) continue;
                        _unitLogPosition = log.Header.Index;
                        Protocol.SendEvent(new UnitLogEvent(log.Header, log.Success ? new
                        {
                            response = new
                            {
                                point = new { log.RadarResponse.Point.x, log.RadarResponse.Point.y, log.RadarResponse.Point.z },
                                speedSignal = log.RadarResponse.SpeedSignal,
                                clutter = log.RadarResponse.Clutter,
                                fingerprint = log.RadarResponse.Fingerprint,
                                meta = log.RadarResponse.Meta,
                            },
                        } : null));
                        break;
                    }
                    case LogPieceType.Transmission_WiredOut:
                    {
                        UnitLog_Transmission_WiredOut log = UnitLog_Transmission_WiredOut.Read(logBuffer, ref i);
                        if (log.Header.Index <= _unitLogPosition) continue;
                        _unitLogPosition = log.Header.Index;
                        Protocol.SendEvent(new UnitLogEvent(log.Header, new
                        {
                            data = Convert.ToBase64String(log.Data.ToArray()),
                            meta = new
                            {
                                port = log.Metadata.Port,
                            },
                        }));
                        break;
                    }
                    case LogPieceType.Transmission_WiredIn:
                    {
                        UnitLog_Transmission_WiredIn log = UnitLog_Transmission_WiredIn.Read(logBuffer, ref i);
                        if (log.Header.Index <= _unitLogPosition) continue;
                        _unitLogPosition = log.Header.Index;
                        Protocol.SendEvent(new UnitLogEvent(log.Header, new
                        {
                            data = Convert.ToBase64String(log.Data.ToArray()),
                            meta = new
                            {
                                port = log.Metadata.Port,
                            },
                        }));
                        break;
                    }
                    case LogPieceType.Transmission_WirelessOut:
                    {
                        UnitLog_Transmission_WirelessOut log = UnitLog_Transmission_WirelessOut.Read(logBuffer, ref i);
                        if (log.Header.Index <= _unitLogPosition) continue;
                        _unitLogPosition = log.Header.Index;
                        Protocol.SendEvent(new UnitLogEvent(log.Header, new
                        {
                            data = Convert.ToBase64String(log.Data.ToArray()),
                            meta = new
                            {
                                source = new { log.Metadata.Source.x, log.Metadata.Source.y, log.Metadata.Source.z },
                                direction = new { log.Metadata.Direction.x, log.Metadata.Direction.y, log.Metadata.Direction.z },
                                cosAngle = log.Metadata.CosAngle,
                                angle = log.Metadata.Angle,
                            },
                        }));
                        break;
                    }
                    case LogPieceType.Transmission_WirelessIn:
                    {
                        UnitLog_Transmission_WirelessIn log = UnitLog_Transmission_WirelessIn.Read(logBuffer, ref i);
                        if (log.Header.Index <= _unitLogPosition) continue;
                        _unitLogPosition = log.Header.Index;
                        Protocol.SendEvent(new UnitLogEvent(log.Header, new
                        {
                            data = Convert.ToBase64String(log.Data.ToArray()),
                            meta = new
                            {
                                source = new { log.Metadata.Source.x, log.Metadata.Source.y, log.Metadata.Source.z },
                            },
                        }));
                        break;
                    }
                    case LogPieceType.ProcessorSignal:
                    {
                        break;
                    }
                    case LogPieceType.Unknown0:
                    case LogPieceType.Unknown1:
                    default:
                        goto invalid;
                }
            }

        invalid:;
        }

        if (processor.DebugContext.IsStopUnhandled)
        {
            processor.DebugContext.IsStopUnhandled = false;

            GatherInformation();

            List<CallTraceItem> stacktrace = new();
            DebugUtils.TraceStack(global::Processor.GetMemorySpan(ref processor), processor.Registers.BasePointer, DebugInformation.StackOffsets, stacktrace);
            FunctionInformation function = DebugInformation.GetFunctionInformation(processor.Registers.CodePointer);
            if (!DebugInformation.TryGetSourceLocation(processor.Registers.CodePointer, out SourceCodeLocation sourceLocation))
            {
                sourceLocation = default;
            }

            StopContext stopContext = new()
            {
                CodePointer = processor.Registers.CodePointer,
                Function = function,
                Location = sourceLocation,
                StackTrace = stacktrace.ToImmutableArray(),
            };

            switch (processor.DebugContext.Stopped)
            {
                case ProcessorJob.StopReason.Pause:
                    Protocol.SendEvent(new StoppedEvent()
                    {
                        Reason = StoppedEvent.ReasonValue.Pause,
                        AllThreadsStopped = true,
                        ThreadId = 1,
                    });
                    _lastStopContext = stopContext;
                    break;
                case ProcessorJob.StopReason.Breakpoint:
                    bool shouldContinue = true;
                    List<int> hitBreakpoints = new();

                    foreach (var item in _instructionBreakpoints)
                    {
                        if (item.Address != processor.Registers.CodePointer
                            || !item.Breakpoint.Id.HasValue)
                        { continue; }

                        hitBreakpoints.Add(item.Breakpoint.Id.Value);
                        shouldContinue = false;
                    }

                    foreach (CompiledBreakpoint breakpoint in _breakpoints.Values.SelectMany(v => v))
                    {
                        if (breakpoint.Instruction != processor.Registers.CodePointer
                            || !breakpoint.Breakpoint.Id.HasValue)
                        { continue; }

                        if (!string.IsNullOrWhiteSpace(breakpoint.Condition))
                        {
                            DiagnosticsCollection diagnostics = new();

                            if (TryEvaluate(breakpoint.Condition, StackFrames.Count > 0 ? StackFrames[0].Id : null, diagnostics, out bool result, out var error))
                            {
                                if (!result) goto skip;
                            }
                            else
                            {
                                StringBuilder b = new();
                                b.AppendLine($"Failed to evaluate breakpoint condition `{breakpoint.Condition}` at {breakpoint.SourceBreakpoint.Line}:{breakpoint.SourceBreakpoint.Column} in {breakpoint.Breakpoint.Source.Name}");
                                diagnostics.WriteErrorsTo(b);
                                if (error is not null) b.AppendLine(error.ToString());
                                Protocol.SendEvent(new OutputEvent()
                                {
                                    Output = b.ToString(),
                                    Severity = OutputEvent.SeverityValue.Error,
                                });
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(breakpoint.LogMessage))
                        {
                            List<ExpressionVariable> variables = StackFrames.Count > 0 ? GetExpressionVariables(StackFrames[0].Id) : new();
                            string template = breakpoint.LogMessage;
                            int i = 0;
                            StringBuilder res = new();
                            while (i < template.Length)
                            {
                                int j = template.IndexOf('{', i);
                                if (j != -1)
                                {
                                    int k = template.IndexOf('}', j);
                                    if (k != -1)
                                    {
                                        res.Append(template[i..j]);

                                        string item = template[(j + 1)..k];

                                        if (variables.TryFind(v => v.Name == item, out ExpressionVariable variable))
                                        {
                                            UniqueIds uniqueIds = new();
                                            unsafe
                                            {
                                                res.Append(ToVariable(variable.Address, variable.Type, new ReadOnlySpan<byte>(Unsafe.AsPointer(ref processor.Memory.Memory), global::Processor.TotalMemorySize), variable.Name, ref uniqueIds).Value);
                                            }
                                        }

                                        i = k + 1;
                                        continue;
                                    }
                                }

                                res.Append(template[i..]);
                                break;
                            }
                            res.AppendLine();
                            Protocol.SendEvent(new OutputEvent()
                            {
                                Output = res.ToString(),
                                Category = OutputEvent.CategoryValue.Console,
                                Source = breakpoint.Breakpoint.Source,
                                Line = breakpoint.SourceBreakpoint.Line,
                                Column = breakpoint.SourceBreakpoint.Column,
                            });
                            goto skip;
                        }

                        hitBreakpoints.Add(breakpoint.Breakpoint.Id.Value);
                        shouldContinue = false;
                    skip:;
                    }

                    if (shouldContinue)
                    {
                        processor.DebugContext.Stopped = ProcessorJob.StopReason.No;
                        break;
                    }

                    Protocol.SendEvent(new StoppedEvent()
                    {
                        Reason = StoppedEvent.ReasonValue.Breakpoint,
                        AllThreadsStopped = true,
                        ThreadId = 1,
                        HitBreakpointIds = hitBreakpoints,
                    });
                    _lastStopContext = stopContext;
                    break;
                case ProcessorJob.StopReason.Signal:
                    if (processor.Signal == Signal.Halt)
                    {
                        Protocol.SendEvent(new StoppedEvent()
                        {
                            Reason = StoppedEvent.ReasonValue.Pause,
                            AllThreadsStopped = true,
                            ThreadId = 1,
                            Description = "Processor halted",
                        });
                    }
                    else
                    {
                        Protocol.SendEvent(new StoppedEvent()
                        {
                            Reason = StoppedEvent.ReasonValue.Exception,
                            AllThreadsStopped = true,
                            ThreadId = 1,
                            Description = processor.Signal switch
                            {
                                Signal.UserCrash => $"Crashed ({processor.Crash})",
                                Signal.StackOverflow => $"Stack Overflow",
                                Signal.UndefinedExternalFunction => $"Undefined external function {processor.Crash}",
                                Signal.PointerOutOfRange => $"Pointer out of Range",
                                Signal.None => "Crashed",
                                Signal.Halt or _ => throw new UnreachableException(),
                            },
                        });
                    }
                    _lastStopContext = stopContext;
                    break;
                case ProcessorJob.StopReason.RuntimeException:
                    Protocol.SendEvent(new StoppedEvent()
                    {
                        Reason = StoppedEvent.ReasonValue.Exception,
                        AllThreadsStopped = true,
                        ThreadId = 1,
                        Description = "Unhandled RuntimeException",
                    });
                    _lastStopContext = stopContext;
                    break;
                case ProcessorJob.StopReason.StepForward:
                    if (sourceLocation.Location.IsDefault)
                    {
                        Log.Warn($"Cannot get source location at {processor.Registers.CodePointer}");
                        processor.DebugContext.Stopped = ProcessorJob.StopReason.StepForwardUnfinished;
                        break;
                    }

                    if (_lastStopContext is not null)
                    {
                        if (sourceLocation.Location == _lastStopContext.Location.Location || stacktrace.Count > _lastStopContext.StackTrace.Length)
                        {
                            processor.DebugContext.Stopped = ProcessorJob.StopReason.StepForwardUnfinished;
                            break;
                        }
                    }

                    Protocol.SendEvent(new StoppedEvent()
                    {
                        Reason = StoppedEvent.ReasonValue.Step,
                        AllThreadsStopped = true,
                        ThreadId = 1,
                    });
                    _lastStopContext = stopContext;
                    break;
                case ProcessorJob.StopReason.StepIn:
                    if (sourceLocation.Location.IsDefault)
                    {
                        Log.Warn($"Cannot get source location at {processor.Registers.CodePointer}");
                        processor.DebugContext.Stopped = ProcessorJob.StopReason.StepInUnfinished;
                        break;
                    }

                    if (_lastStopContext is not null)
                    {
                        if (sourceLocation.Location == _lastStopContext.Location.Location)
                        {
                            processor.DebugContext.Stopped = ProcessorJob.StopReason.StepInUnfinished;
                            break;
                        }
                    }

                    Protocol.SendEvent(new StoppedEvent()
                    {
                        Reason = StoppedEvent.ReasonValue.Step,
                        AllThreadsStopped = true,
                        ThreadId = 1,
                    });
                    _lastStopContext = stopContext;
                    break;
                case ProcessorJob.StopReason.StepOut:
                    if (sourceLocation.Location.IsDefault)
                    {
                        Log.Warn($"Cannot get source location at {processor.Registers.CodePointer}");
                        processor.DebugContext.Stopped = ProcessorJob.StopReason.StepOutUnfinished;
                        break;
                    }

                    if (_lastStopContext is not null)
                    {
                        if (sourceLocation.Location == _lastStopContext.Location.Location || stacktrace.Count >= _lastStopContext.StackTrace.Length)
                        {
                            processor.DebugContext.Stopped = ProcessorJob.StopReason.StepOutUnfinished;
                            break;
                        }
                    }

                    Protocol.SendEvent(new StoppedEvent()
                    {
                        Reason = StoppedEvent.ReasonValue.Step,
                        AllThreadsStopped = true,
                        ThreadId = 1,
                    });
                    _lastStopContext = stopContext;
                    break;
                case ProcessorJob.StopReason.StepInstruction:
                    Protocol.SendEvent(new StoppedEvent()
                    {
                        Reason = StoppedEvent.ReasonValue.Step,
                        AllThreadsStopped = true,
                        ThreadId = 1,
                    });
                    _lastStopContext = stopContext;
                    break;
                case ProcessorJob.StopReason.StepForwardUnfinished:
                case ProcessorJob.StopReason.StepInUnfinished:
                case ProcessorJob.StopReason.StepOutUnfinished:
                case ProcessorJob.StopReason.StepInstructionUnfinished:
                    break;
                case ProcessorJob.StopReason.No:
                    Protocol.SendEvent(new ContinuedEvent()
                    {
                        AllThreadsContinued = true,
                        ThreadId = 1,
                    });
                    break;
                default:
                    throw new UnreachableException();
            }
            e.SetComponentData(_entity, processor);
        }

        if (processor.DebugContext.IsContinueUnhandled)
        {
            processor.DebugContext.IsContinueUnhandled = false;
            Continue(null, ref processor);
            Protocol.SendEvent(new ContinuedEvent()
            {
                AllThreadsContinued = true,
                ThreadId = 1,
            });

            e.SetComponentData(_entity, processor);
        }
    }

    protected override void ResetSession()
    {
        base.ResetSession();

        _lastStopContext = null;
    }

    protected override void DisposeSession()
    {
        base.DisposeSession();

        EntityManager e = ConnectionManager.ServerOrDefaultWorld.EntityManager;
        if (e.Exists(_entity))
        {
            Processor _processor = e.GetComponentData<Processor>(_entity);
            _processor.DebugContext = default;
            e.SetComponentData(_entity, _processor);
        }

        _originalSource = default;
        _originalFile = default;
        _entity = default;
        _playerGuid = default;
        _playerConnectionId = default;
        _playerEntity = default;
        _unitLogPosition = default;
        _unitTerminalPosition = default;
        _unitTerminalBuilder.Clear();
        _isDisconnected = false;
    }

    public void Dispose()
    {
        if (Protocol.IsRunning)
        {
            Debug.Log($"{DebugEx.ServerPrefix} [DAP] Stopping protocol");
            Protocol.Stop();
        }

        DisposeSession();
    }
}
