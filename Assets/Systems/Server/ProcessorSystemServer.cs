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
using Unity.Collections.LowLevel.Unsafe;
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
    Label,
    Image,
    MAX,
}

[BurstCompile]
[StructLayout(LayoutKind.Explicit)]
struct UserUIElement
{
    [FieldOffset(0)] public bool IsDirty;
    [FieldOffset(1)] public int Id;
    [FieldOffset(5)] public UserUIElementType Type;
    [FieldOffset(6)] public int2 Position;
    [FieldOffset(14)] public int2 Size;
    [FieldOffset(22)] public UserUIElementLabel Label;
    [FieldOffset(22)] public UserUIElementImage Image;
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
        public required NativeList<OwnedData<UserUIElement>>.ParallelWriter UIElements;
        public required ProcessorRef ProcessorRef;
        public required EntityRef EntityRef;
    }

    NativeArray<ExternalFunctionScopedSync> scopedExternalFunctions;
    NativeList<OwnedData<BufferedLine>> debugLines;
    NativeList<OwnedData<BufferedWorldLabel>> worldLabels;
    NativeList<OwnedData<UserUIElement>> uiElements;
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

        foreach (var (player, lines, labels) in
            SystemAPI.Query<RefRO<Player>, DynamicBuffer<BufferedLine>, DynamicBuffer<BufferedWorldLabel>>())
        {
            Entity connection = Entity.Null;
            foreach (var (_connection, _connectionEntity) in
                SystemAPI.Query<RefRO<NetworkId>>()
                .WithEntityAccess())
            {
                if (_connection.ValueRO.Value != player.ValueRO.ConnectionId) continue;
                connection = _connectionEntity;
                break;
            }

            if (connection != Entity.Null && player.ValueRO.ConnectionState == PlayerConnectionState.Connected)
            {
                for (int i = 0; i < debugLines.Length; i++)
                {
                    if (debugLines[i].Owner != player.ValueRO.Team) continue;

                    if (Utils.Distance(player.ValueRO.Position, debugLines[i].Value.Value) < 50f)
                    {
                        NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new DebugLineRpc()
                        {
                            Position = debugLines[i].Value.Value,
                            Color = debugLines[i].Value.Color,
                        }, connection);
                    }

                    // for (int j = 0; j < lines.Length; j++)
                    // {
                    //     if (debugLines[i].Value.Value.Equals(lines[j].Value))
                    //     {
                    //         lines.Set(j, debugLines[i].Value);
                    //         goto next;
                    //     }
                    // }
                    // lines.Add(debugLines[i].Value);
                    // next:;
                }

                for (int i = 0; i < worldLabels.Length; i++)
                {
                    if (worldLabels[i].Owner != player.ValueRO.Team) continue;

                    //if (math.distancesq(player.ValueRO.Position, worldLabels[i].Value.Position) < 50f * 50f)
                    //{
                    //    Entity rpc = commandBuffer.CreateEntity(debugLabelRpcArchetype);
                    //    commandBuffer.SetComponent<SendRpcCommandRequest>(rpc, new()
                    //    {
                    //        TargetConnection = connection,
                    //    });
                    //    commandBuffer.SetComponent<DebugLabelRpc>(rpc, new()
                    //    {
                    //        Text = worldLabels[i].Value.Text,
                    //        Position = worldLabels[i].Value.Position,
                    //        Color = worldLabels[i].Value.Color,
                    //    });
                    //}

                    for (int j = 0; j < labels.Length; j++)
                    {
                        if (math.distancesq(worldLabels[i].Value.Position, labels[j].Position) < 1f)
                        {
                            labels.Set(j, worldLabels[i].Value);
                            goto next;
                        }
                    }
                    labels.Add(worldLabels[i].Value);
                next:;
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

            for (int i = 0; i < uiElements.Length; i++)
            {
                if (uiElements[i].Owner != player.ValueRO.Team) continue;
                if (!uiElements[i].Value.IsDirty && uiElements[i].Value.Id != 0) continue;

                if (connection == Entity.Null) continue;

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
                    FixedString64Bytes data = new(processor.ValueRW.StdOutBuffer.Substring(offset, bytesToSend));

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

[BurstCompile(CompileSynchronously = true)]
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
    public NativeList<OwnedData<UserUIElement>>.ParallelWriter uiElements;
    [ReadOnly] public ComponentLookup<CoreComputer> QCoreComputer;
    [ReadOnly] public ComponentLookup<Radar> QRadar;
    [ReadOnly] public ComponentLookup<Facility> QFacility;

    [BurstCompile(CompileSynchronously = true)]
    unsafe void Execute(
        ref Processor processor,
        in UnitTeam team,
        in LocalToWorld worldTransform,
        in LocalTransform localTransform,
        Entity entity)
    {
        //using var _1 = __ProcessorJobOuter.Auto();

        if (!processor.Source.Code.IsCreated)
        {
            processor.StatusLED.Status = ProcessorStatus.Off;
            return;
        }

        //NativeArray<byte> memory = new NativeArray<byte>(Processor.TotalMemorySize, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        //memory.CopyFrom(new Span<byte>(Unsafe.AsPointer(ref processor.Memory), Processor.TotalMemorySize).ToArray());
        Span<byte> memory = new(Unsafe.AsPointer(ref processor.Memory), Processor.TotalMemorySize);

        processor.Memory.MappedMemory.Pendrive.IsPlugged = processor.PluggedPendrive.Entity != Entity.Null;

        ProcessorSystemServer.FunctionScope scope = new()
        {
            DebugLines = debugLines,
            WorldLabels = worldLabels,
            UIElements = uiElements,
            ProcessorRef = new ProcessorSystemServer.ProcessorRef()
            {
                Memory = Unsafe.AsPointer(ref MemoryMarshal.GetReference(memory)),
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

        //using (var _2 = __ProcessorJobInner.Auto())
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
                                Debug.LogError(string.Format($"{DebugEx.ServerPrefix} Crashed ({{0}})", processorState.Crash));
                                break;
                            case Signal.StackOverflow:
                                Debug.LogError($"{DebugEx.ServerPrefix} Stack Overflow");
                                break;
                            case Signal.Halt:
                                // Debug.LogError($"{DebugEx.ServerPrefix} Halted");
                                break;
                            case Signal.UndefinedExternalFunction:
                                Debug.LogError(string.Format($"{DebugEx.ServerPrefix} Undefined external function {{0}}", processorState.Crash));
                                break;
                            case Signal.PointerOutOfRange:
                                Debug.LogError($"{DebugEx.ServerPrefix} Pointer out of Range");
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
                    char key = processor.InputKey[0];
                    processor.InputKey.RemoveAt(0);
                    processor.IsKeyRequested = false;
                    processorState.Pop(2);
                    processorState.Push(key.AsBytes());
                }

                processorState.Process();
            }
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
#pragma warning restore IDE0072
        scopedExternalFunctions.Dispose();
    }
}
