#define UNITY_PROFILER
#if UNITY_EDITOR && EDITOR_DEBUG
#define DEBUG_LINES
#endif

using System;
using System.Runtime.CompilerServices;
using LanguageCore;
using LanguageCore.Runtime;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Profiling;
using Unity.Transforms;
using System.Runtime.InteropServices;
using System.Linq;

enum UserUIElementType : byte
{
    MIN,
    Box,
    Label,
    Image,
    MAX,
}

enum UserUIDirection : byte
{
    Horizontal,
    Vertical,
    HorizontalReverse,
    VerticalReverse,
}

[BurstCompile]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct UserUIElement
{
    public bool IsDirty;
    public int Id;
    public int Parent;
    public int Index;
    public UserUIDirection Direction;
    public int Margin;
    public int Padding;
    public int2 Size;
    public UserUIElementType Type;
    public UserUIElementMeta Meta;
}

[BurstCompile]
[StructLayout(LayoutKind.Explicit)]
struct UserUIElementMeta
{
    [FieldOffset(0)] public UserUIElementLabel Label;
    [FieldOffset(0)] public UserUIElementImage Image;
}

[BurstCompile]
[StructLayout(LayoutKind.Sequential)]
struct UserUIElementLabel
{
    public float3 Color;
    public FixedBytes30 Text;
}

[BurstCompile]
[StructLayout(LayoutKind.Sequential)]
struct UserUIElementImage
{
    public short Width;
    public short Height;
    public FixedBytes510 Image;
}

struct OwnedData<T>
{
    public readonly int Owner;
    public T Value;

    public OwnedData(int owner, T value)
    {
        Owner = owner;
        Value = value;
    }
}

struct EntityOwnedData<T>
{
    public readonly int OwnerTeam;
    public readonly Entity OwnerEntity;
    public T Value;

    public EntityOwnedData(int ownerTeam, Entity ownerEntity, T value)
    {
        OwnerTeam = ownerTeam;
        OwnerEntity = ownerEntity;
        Value = value;
    }
}

struct TerminalSubscriptionServer
{
    public SpawnedGhost Entity;
    public ulong Offset;
    public Entity Connection;
}

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
unsafe partial struct ProcessorSystemServer : ISystem
{
    public static readonly BytecodeInterpreterSettings BytecodeInterpreterSettings = new()
    {
        HeapSize = Processor.HeapSize,
        StackSize = Processor.StackSize,
    };

    [BurstCompile]
    public ref struct ProcessorRef
    {
        public required void* Memory;
        public required int* Crash;
        public required Signal* Signal;
        public required Registers* Registers;

        public readonly Span<byte> MemorySpan => new(Memory, Processor.TotalMemorySize);

        [BurstCompile]
        public void Push(scoped ReadOnlySpan<byte> data)
        {
            Registers->StackPointer += data.Length * ProcessorState.StackDirection;

            if (Registers->StackPointer is >= Processor.UserMemorySize or < Processor.HeapSize)
            {
                *Signal = LanguageCore.Runtime.Signal.StackOverflow;
                return;
            }

            ((nint)Memory).Set(Registers->StackPointer, data);
        }

        public void DoCrash()
        {
            *Crash = 0;
            *Signal = LanguageCore.Runtime.Signal.UserCrash;
        }

        [BurstCompile]
        public void DoCrash(in FixedString32Bytes message)
        {
            char* ptr = stackalloc char[message.Length * sizeof(char)];
            Unicode.Utf8ToUtf16(message.GetUnsafePtr(), message.Length, ptr, out int utf16Length, message.Length * sizeof(char));
            Push(new Span<byte>(ptr, utf16Length * sizeof(char)));

            *Crash = Registers->StackPointer;
            *Signal = LanguageCore.Runtime.Signal.UserCrash;
        }

        [BurstCompile]
        public readonly void GetString(int pointer, out FixedString32Bytes @string)
        {
            @string = new();
            for (int i = pointer; i < pointer + 32; i += sizeof(char))
            {
                char c = *(char*)((byte*)Memory + i);
                if (c == '\0') break;
                @string.Append(c);
            }
        }
    }

    [BurstCompile]
    public ref struct EntityRef
    {
        public required Entity Entity;
        public required Processor* Processor;
        public required LocalToWorld WorldTransform;
        public required LocalTransform LocalTransform;
        public required UnitTeam Team;
    }

    [BurstCompile]
    public ref struct FunctionScope
    {
        public required NativeList<OwnedData<BufferedLine>>.ParallelWriter DebugLines;
        public required NativeList<OwnedData<BufferedWorldLabel>>.ParallelWriter WorldLabels;
        public required NativeList<EntityOwnedData<UserUIElement>>.ParallelWriter UIElements;
        public required ProcessorRef ProcessorRef;
        public required EntityRef EntityRef;
        public required FixedList128Bytes<BufferedLogPiece>* Log;
    }

    NativeArray<ExternalFunctionScopedSync> scopedExternalFunctions;
    NativeList<OwnedData<BufferedLine>> debugLines;
    NativeList<OwnedData<BufferedWorldLabel>> worldLabels;
    public NativeList<EntityOwnedData<UserUIElement>> uiElements;
    NativeList<TerminalSubscriptionServer> subscribedTerminals;

    void ISystem.OnCreate(ref SystemState state)
    {
        //state.RequireForUpdate<WorldLabelSettings>();

        NativeList<ExternalFunctionScopedSync> _scopedExternalFunctions = new(Allocator.Temp);
        ProcessorAPI.GenerateExternalFunctions(ref _scopedExternalFunctions);
        scopedExternalFunctions = new NativeArray<ExternalFunctionScopedSync>(_scopedExternalFunctions.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        scopedExternalFunctions.CopyFrom(_scopedExternalFunctions.AsArray());
        _scopedExternalFunctions.Dispose();

        debugLines = new(256, Allocator.Persistent);
        worldLabels = new(256, Allocator.Persistent);
        uiElements = new(256, Allocator.Persistent);
        subscribedTerminals = new(4, Allocator.Persistent);

        // SystemAPI.GetSingleton<RpcCollection>()
        //     .RegisterRpc(ComponentType.ReadWrite<UIElementUpdateRpc>(), default(UIElementUpdateRpc).CompileExecute());
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (player, realPlayer, lines, labels) in
            SystemAPI.Query<RefRO<Player>, RefRO<RealPlayer>, DynamicBuffer<BufferedLine>, DynamicBuffer<BufferedWorldLabel>>())
        {
            Entity connection = Entity.Null;
            foreach (var (_connection, _connectionEntity) in
                SystemAPI.Query<RefRO<NetworkId>>()
                .WithEntityAccess())
            {
                if (_connection.ValueRO.Value != realPlayer.ValueRO.ConnectionId) continue;
                connection = _connectionEntity;
                break;
            }

            if (connection != Entity.Null && realPlayer.ValueRO.ConnectionState == PlayerConnectionState.Connected)
            {
                for (int i = 0; i < debugLines.Length; i++)
                {
                    if (debugLines[i].Owner != player.ValueRO.Team) continue;
                    if (Utils.Distance(realPlayer.ValueRO.Position, debugLines[i].Value.Position) >= 50f) continue;

                    NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new DebugLineRpc()
                    {
                        Position = debugLines[i].Value.Position,
                        Color = debugLines[i].Value.Color,
                    }, connection);
                }

                for (int i = 0; i < worldLabels.Length; i++)
                {
                    if (worldLabels[i].Owner != player.ValueRO.Team) continue;
                    if (math.distancesq(realPlayer.ValueRO.Position, worldLabels[i].Value.Position) >= 50f * 50f) continue;

                    NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new DebugLabelRpc()
                    {
                        Text = worldLabels[i].Value.Text,
                        Position = worldLabels[i].Value.Position,
                        Color = worldLabels[i].Value.Color,
                    }, connection);
                }

                for (int i = 0; i < uiElements.Length; i++)
                {
                    if (uiElements[i].OwnerTeam != player.ValueRO.Team) continue;
                    if (!uiElements[i].Value.IsDirty && uiElements[i].Value.Id != 0) continue;

                    if (uiElements[i].Value.Id == 0)
                    {
                        // Debug.Log($"{DebugEx.ServerPrefix} {uiElements[i]} destroyed");

                        NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new UIElementDestroyRpc()
                        {
                            Id = uiElements[i].Value.Id,
                        }, connection);
                        uiElements.RemoveAt(i--);
                    }
                    else
                    {
                        // Debug.Log($"{DebugEx.ServerPrefix} {uiElements[i]} updated, {uiElements[i].Value.Label.Text.AsString()}");

                        NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new UIElementUpdateRpc()
                        {
                            UIElement = uiElements[i].Value,
                        }, connection);
                        uiElements.AsArray().AsSpan()[i].Value.IsDirty = false;
                    }
                }
            }
            else if (state.WorldUnmanaged.IsLocal())
            {
                for (int i = 0; i < debugLines.Length; i++)
                {
                    if (debugLines[i].Owner != player.ValueRO.Team) continue;

                    for (int j = 0; j < lines.Length; j++)
                    {
                        if (!debugLines[i].Value.Position.Equals(lines[j].Position)) continue;
                        lines.Set(j, debugLines[i].Value with
                        {
                            DieAt = (float)SystemAPI.Time.ElapsedTime + DebugLinesSystemClient.Lifetime
                        });
                        goto next;
                    }
                    lines.Add(debugLines[i].Value with
                    {
                        DieAt = (float)SystemAPI.Time.ElapsedTime + DebugLinesSystemClient.Lifetime
                    });
                next:;
                }

                for (int i = 0; i < worldLabels.Length; i++)
                {
                    if (worldLabels[i].Owner != player.ValueRO.Team) continue;

                    for (int j = 0; j < labels.Length; j++)
                    {
                        if (math.distancesq(worldLabels[i].Value.Position, labels[j].Position) >= 1f) continue;
                        labels.Set(j, worldLabels[i].Value with
                        {
                            DieAt = (float)SystemAPI.Time.ElapsedTime + DebugLabelSystemClient.Lifetime
                        });
                        goto next;
                    }
                    labels.Add(worldLabels[i].Value with
                    {
                        DieAt = (float)SystemAPI.Time.ElapsedTime + DebugLabelSystemClient.Lifetime
                    });
                next:;
                }

                for (int i = 0; i < uiElements.Length; i++)
                {
                    if (uiElements[i].OwnerTeam != player.ValueRO.Team) continue;
                    if (!uiElements[i].Value.IsDirty && uiElements[i].Value.Id != 0) continue;

                    if (uiElements[i].Value.Id == 0)
                    {
                        // Debug.Log($"{DebugEx.ServerPrefix} {uiElements[i]} destroyed");

                        NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new UIElementDestroyRpc()
                        {
                            Id = uiElements[i].Value.Id,
                        });
                        uiElements.RemoveAt(i--);
                    }
                    else
                    {
                        // Debug.Log($"{DebugEx.ServerPrefix} {uiElements[i]} updated, {uiElements[i].Value.Label.Text.AsString()}");

                        NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new UIElementUpdateRpc()
                        {
                            UIElement = uiElements[i].Value,
                        });
                        uiElements.AsArray().AsSpan()[i].Value.IsDirty = false;
                    }
                }
            }

            /*
            for (int i = 0; i < worldLabels.Length; i++)
            {
                if (worldLabels[i].Owner != player.ValueRO.Team) continue;

                for (int j = 0; j < labels.Length; j++)
                {
                    if (worldLabels[i].Value.Position.Equals(labels[j].Position))
                    {
                        labels.Set(j, worldLabels[i].Value);
                        goto next;
                    }
                }
                labels.Add(worldLabels[i].Value);
            next:;
            }
            */
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<SubscribeTerminalRpc>>()
            .WithEntityAccess())
        {
            commandBuffer.DestroyEntity(entity);

            for (int i = 0; i < subscribedTerminals.Length; i++)
            {
                if (subscribedTerminals[i].Connection == request.ValueRO.SourceConnection
                    && subscribedTerminals[i].Entity.Equals(command.ValueRO.Entity))
                {
                    Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Client {{0}} is already subscribed to terminal of entity {{1}} ({{2}})", request.ValueRO.SourceConnection, command.ValueRO.Entity, command.ValueRO.Offset));
                    goto exists;
                }
            }

            Debug.Log(string.Format($"{DebugEx.ServerPrefix} Client {{0}} subscribed to terminal of entity {{1}} ({{2}})", request.ValueRO.SourceConnection, command.ValueRO.Entity, command.ValueRO.Offset));
            subscribedTerminals.Add(new TerminalSubscriptionServer()
            {
                Entity = command.ValueRO.Entity,
                Offset = command.ValueRO.Offset,
                Connection = request.ValueRO.SourceConnection,
            });
        exists:;
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<UnsubscribeTerminalRpc>>()
            .WithEntityAccess())
        {
            commandBuffer.DestroyEntity(entity);

            for (int i = 0; i < subscribedTerminals.Length; i++)
            {
                if (subscribedTerminals[i].Connection == request.ValueRO.SourceConnection
                    && subscribedTerminals[i].Entity.Equals(command.ValueRO.Entity))
                {
                    Debug.Log(string.Format($"{DebugEx.ServerPrefix} Client {{0}} unsubscribed from terminal of entity {{1}}", request.ValueRO.SourceConnection, command.ValueRO.Entity));
                    subscribedTerminals.RemoveAt(i--);
                }
            }
        }

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

        for (int i = 0; i < subscribedTerminals.Length; i++)
        {
            TerminalSubscriptionServer subscription = subscribedTerminals[i];
            if (!SystemAPI.Exists(subscription.Connection)) continue;

            foreach (var (ghostInstance, processor) in
                SystemAPI.Query<RefRO<GhostInstance>, RefRW<Processor>>())
            {
                if (!subscription.Entity.Equals(ghostInstance.ValueRO)) continue;
                ulong beginOffset = Math.Max(0, processor.ValueRO.StdOutBufferCursor - (ulong)processor.ValueRO.StdOutBuffer.Length);
                ulong endOffset = processor.ValueRO.StdOutBufferCursor;

                Debug.Assert(endOffset >= beginOffset);
                Debug.Assert(endOffset - beginOffset == (ulong)processor.ValueRO.StdOutBuffer.Length);

                if (endOffset > subscription.Offset)
                {
                    ulong sendStart = Math.Max(subscription.Offset, beginOffset);
                    int offset = (int)(sendStart - beginOffset);
                    int bytesToSend = (int)Math.Min((ulong)FixedString64Bytes.UTF8MaxLengthInBytes, endOffset - sendStart);
                    FixedList64Bytes<byte> data = new();
                    data.AddRange(processor.ValueRW.StdOutBuffer.GetUnsafePtr() + offset, bytesToSend);

                    NetcodeUtils.CreateRPC<TerminalDataRpc>(commandBuffer, state.WorldUnmanaged, new()
                    {
                        Entity = ghostInstance.ValueRO,
                        Data = data,
                        Offset = sendStart,
                    }, subscription.Connection);
                    subscription.Offset = sendStart + (ulong)data.Length;
                    subscribedTerminals[i] = subscription;
                }
                break;
            }
        }

        debugLines.Clear();
        worldLabels.Clear();

        new ProcessorJob()
        {
            scopedExternalFunctions = scopedExternalFunctions,

            debugLines = debugLines.AsParallelWriter(),
            worldLabels = worldLabels.AsParallelWriter(),
            uiElements = uiElements.AsParallelWriter(),

            QCoreComputer = SystemAPI.GetComponentLookup<CoreComputer>(true),
            QRadar = SystemAPI.GetComponentLookup<Radar>(true),
            QFacility = SystemAPI.GetComponentLookup<Facility>(true),
        }.ScheduleParallel();
    }
}

[WithAll(typeof(Processor))]
partial struct ProcessorJob : IJobEntity
{
#if UNITY_PROFILER
    public static readonly ProfilerMarker __ProcessorJobOuter = new("ProcessorJobOuter");
    public static readonly ProfilerMarker __ProcessorJobInner = new("ProcessorJobInner");
#endif

    [ReadOnly] public NativeArray<ExternalFunctionScopedSync> scopedExternalFunctions;
    public NativeList<OwnedData<BufferedLine>>.ParallelWriter debugLines;
    public NativeList<OwnedData<BufferedWorldLabel>>.ParallelWriter worldLabels;
    public NativeList<EntityOwnedData<UserUIElement>>.ParallelWriter uiElements;
    [ReadOnly] public ComponentLookup<CoreComputer> QCoreComputer;
    [ReadOnly] public ComponentLookup<Radar> QRadar;
    [ReadOnly] public ComponentLookup<Facility> QFacility;

    unsafe void Execute(
        ref Processor processor,
        in UnitTeam team,
        in LocalToWorld worldTransform,
        in LocalTransform localTransform,
        ref DynamicBuffer<BufferedLogPiece> _log,
        Entity entity)
    {
        using var _1 = __ProcessorJobOuter.Auto();

        if (!processor.Source.Code.IsCreated)
        {
            processor.StatusLED.Status = ProcessorStatus.Off;
            return;
        }

        //NativeArray<byte> memory = new NativeArray<byte>(Processor.TotalMemorySize, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        //memory.CopyFrom(new Span<byte>(Unsafe.AsPointer(ref processor.Memory), Processor.TotalMemorySize).ToArray());
        Span<byte> memory = new(Unsafe.AsPointer(ref processor.Memory), Processor.TotalMemorySize);

        FixedList128Bytes<BufferedLogPiece> log = new();
        ProcessorSystemServer.FunctionScope scope = new()
        {
            DebugLines = debugLines,
            WorldLabels = worldLabels,
            UIElements = uiElements,
            ProcessorRef = new ProcessorSystemServer.ProcessorRef()
            {
                Memory = Unsafe.AsPointer(ref processor.Memory),
                Crash = null,
                Registers = null,
                Signal = null,
            },
            EntityRef = new ProcessorSystemServer.EntityRef()
            {
                Entity = entity,
                Processor = (Processor*)Unsafe.AsPointer(ref processor),
                WorldTransform = worldTransform,
                LocalTransform = localTransform,
                Team = team,
            },
            Log = &log,
        };

        NativeList<ExternalFunctionScopedSync> scopedExternalFunctions = new(this.scopedExternalFunctions.Length + processor.Source.GeneratedFunctions.Length, Allocator.Temp);

        for (int i = 0; i < this.scopedExternalFunctions.Length; i++)
        {
            if ((this.scopedExternalFunctions[i].Id & ProcessorAPI.GlobalPrefix) == ProcessorAPI.GUI.Prefix &&
                !QCoreComputer.HasComponent(entity))
            {
                continue;
            }

            scopedExternalFunctions.Add(this.scopedExternalFunctions[i]);
            scopedExternalFunctions.GetUnsafePtr()[scopedExternalFunctions.Length - 1].Scope = (nint)(void*)&scope;
        }

        int start = scopedExternalFunctions.Length;
        if (processor.Source.GeneratedFunctions.IsCreated)
        {
            scopedExternalFunctions.AddRange(processor.Source.GeneratedFunctions.Ptr, processor.Source.GeneratedFunctions.Length);
        }

        ProcessorState processorState = new(
            ProcessorSystemServer.BytecodeInterpreterSettings,
            processor.Registers,
            memory,
            processor.Source.Code.AsSpan(),
            scopedExternalFunctions.AsArray().AsSpan()
        )
        {
            Signal = processor.Signal,
            Crash = processor.Crash,
            HotFunctions = processor.HotFunctions,
        };

        scope.ProcessorRef.Crash = &processorState.Crash;
        scope.ProcessorRef.Signal = &processorState.Signal;
        scope.ProcessorRef.Registers = &processorState.Registers;

        if (processor.DebugContext.IsBeingDebugged)
        {
            HandleTickDebug(ref processor, ref processorState);
        }
        else
        {
            HandleTick(ref processor, ref processorState);
        }

        if (processor.Source.GeneratedFunctions.IsCreated)
        {
            for (int i = start; i < scopedExternalFunctions.Length; i++)
            {
                processor.Source.GeneratedFunctions.Ptr[i - start].Flags = scopedExternalFunctions[i].Flags;
            }
        }

        if (((ProcessorMemory*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(memory)))->MappedMemory.Leds.CustomLED != 0)
        {
            ((ProcessorMemory*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(memory)))->MappedMemory.Leds.CustomLED = 0;
            processor.CustomLED.Blink();
        }

        //processor.Memory = *(ProcessorMemory*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(memory));
        processor.Registers = processorState.Registers;
        processor.Signal = processorState.Signal;
        processor.Crash = processorState.Crash;
        processor.HotFunctions = processorState.HotFunctions;
#pragma warning disable IDE0072 // Add missing cases
        processor.StatusLED.Status = processorState.Signal switch
        {
            Signal.None => ProcessorStatus.Running,
            Signal.Halt => ProcessorStatus.Halted,
            _ => ProcessorStatus.Error,
        };
#pragma warning restore IDE0072 // Add missing cases
        scopedExternalFunctions.Dispose();

        for (int i = 0; i < log.Length; i++)
        {
            _log.Add(log[i]);
        }
    }

    static void HandleTick(ref Processor processor, ref ProcessorState processorState)
    {
        __ProcessorJobInner.Begin();
        try
        {
            for (int i = 0; i < processor.CyclesPerTick; i++)
            {
                if (processorState.Signal != Signal.None)
                {
                    if (!processor.SignalNotified)
                    {
                        processor.SignalNotified = true;
                        switch (processorState.Signal)
                        {
                            case Signal.UserCrash:
                                Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Crashed ({{0}})", processorState.Crash));
                                break;
                            case Signal.StackOverflow:
                                Debug.LogWarning($"{DebugEx.ServerPrefix} Stack Overflow");
                                break;
                            case Signal.Halt:
                                break;
                            case Signal.UndefinedExternalFunction:
                                Debug.LogWarning(string.Format($"{DebugEx.ServerPrefix} Undefined external function {{0}}", processorState.Crash));
                                break;
                            case Signal.PointerOutOfRange:
                                Debug.LogWarning($"{DebugEx.ServerPrefix} Pointer out of Range");
                                break;
                            case Signal.None:
                                break;
                            default:
                                throw new UnreachableException();
                        }
                    }
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

                processorState.Process();
            }
        }
        catch (RuntimeException ex)
        {
            Debug.LogError(ex.ToString());
        }
        __ProcessorJobInner.End();
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

    static void HandleTickDebug(ref Processor processor, ref ProcessorState processorState)
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
            for (int i = 0; i < processor.CyclesPerTick; i++)
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
