#define _UNITY_PROFILER
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
#if UNITY_PROFILER
using Unity.Profiling;
#endif
using Unity.Transforms;
using System.Runtime.InteropServices;
using System.Linq;
using Unity.Burst.Intrinsics;

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
    public static readonly int MappedMemoryStart = 1;
    public static readonly int UserMemoryStart = MappedMemoryStart + sizeof(MappedMemory);

    public static ref MappedMemory MappedMemory(DynamicBuffer<ProcessorMemoryBuffer> memory)
    {
        if (MappedMemoryStart + sizeof(MappedMemory) >= memory.Length) throw new InvalidOperationException($"Memory size {memory.Length} is <= than the requierd {MappedMemoryStart + sizeof(MappedMemory)}");
        return ref Unsafe.AsRef<MappedMemory>(((byte*)memory.GetUnsafePtr()) + MappedMemoryStart);
    }

    public ref struct ProcessorRef
    {
        public required void* Memory;
        public required int MemorySize;
        public required int* Crash;
        public required Signal* Signal;
        public required Registers* Registers;

        public readonly Span<byte> MemorySpan => new(Memory, MemorySize);

        public void Push(scoped ReadOnlySpan<byte> data)
        {
            Registers->StackPointer += data.Length * ProcessorState.StackDirection;

            if (Registers->StackPointer + data.Length > MemorySize ||
                Registers->StackPointer < Registers->MEow)
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

        public void DoCrash(in FixedString32Bytes message)
        {
            Push(new Span<byte>(message.GetUnsafePtr(), message.Length));

            *Crash = Registers->StackPointer;
            *Signal = LanguageCore.Runtime.Signal.UserCrash;
        }

        public readonly void GetStringU8(int pointer, out FixedString32Bytes @string)
        {
            @string = new();
            if (pointer == 0) return;
            for (int i = pointer; i < pointer + FixedString32Bytes.UTF8MaxLengthInBytes; i++)
            {
                if (i < 0 || i >= MemorySpan.Length) break;
                byte c = *((byte*)Memory + i);
                if (c == 0) break;
                @string.Append((char)c);
            }
        }

        public readonly void GetStringU8(int pointer, out FixedString64Bytes @string)
        {
            @string = new();
            if (pointer == 0) return;
            for (int i = pointer; i < pointer + FixedString64Bytes.UTF8MaxLengthInBytes; i++)
            {
                if (i < 0 || i >= MemorySpan.Length) break;
                byte c = *((byte*)Memory + i);
                if (c == 0) break;
                @string.Append((char)c);
            }
        }
    }

    public struct WorldRef
    {
        public required EntityCommandBuffer.ParallelWriter CommandBuffer;
        public required int SortIndex;
        public required NativeList<OwnedData<BufferedLine>>.ParallelWriter DebugLines;
        public required NativeList<OwnedData<BufferedWorldLabel>>.ParallelWriter WorldLabels;
        public required NativeList<EntityOwnedData<UserUIElement>>.ParallelWriter UIElements;
    }

    public ref struct EntityRef
    {
        public required Entity Entity;
        public required Processor* Processor;
        public required Radar* Radar;
        public required Radio* Radio;
        public required DynamicBuffer<RadarContact> RadarContacts;
        public required LocalTransform LocalTransform;
        public required UnitTeam Team;
        public required SpawnedGhost Ghost;
    }

    public ref struct FunctionScope
    {
        public required WorldRef WorldRef;
        public required ProcessorRef ProcessorRef;
        public required EntityRef EntityRef;
        public required FixedList128Bytes<BufferedLogPiece>* Log;
    }

    NativeArray<ExternalFunctionScopedSync> scopedExternalFunctions;
    NativeList<OwnedData<BufferedLine>> debugLines;
    NativeList<OwnedData<BufferedWorldLabel>> worldLabels;
    public NativeList<EntityOwnedData<UserUIElement>> uiElements;
    NativeList<TerminalSubscriptionServer> subscribedTerminals;

    ComponentTypeHandle<Processor> _processorHandle;
    BufferTypeHandle<ProcessorMemoryBuffer> _processorMemoryBufferHandle;
    ComponentTypeHandle<UnitTeam> _unitTeamHandle;
    ComponentTypeHandle<GhostInstance> _ghostInstanceHandle;
    ComponentTypeHandle<LocalTransform> _localTransformHandle;
    BufferTypeHandle<BufferedLogPiece> _bufferedLogPieceHandle;
    EntityTypeHandle _entityHandle;

    ComponentTypeHandle<Radio> _radioHandle;
    ComponentTypeHandle<Radar> _radarHandle;
    BufferTypeHandle<RadarContact> _radarContactHandle;
    ComponentTypeHandle<Factory> _factoryHandle;
    ComponentTypeHandle<PlayerInterface> _playerInterfaceHandle;

    EntityQuery _query;

    [BurstCompile]
    void ISystem.OnCreate(ref SystemState state)
    {
        //state.RequireForUpdate<WorldLabelSettings>();

        NativeList<ExternalFunctionScopedSync> _scopedExternalFunctions = new(Allocator.Temp);
        ProcessorAPI.GenerateExternalFunctions(ref _scopedExternalFunctions);
        scopedExternalFunctions = new NativeArray<ExternalFunctionScopedSync>(_scopedExternalFunctions.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        scopedExternalFunctions.CopyFrom(_scopedExternalFunctions.AsArray());
        _scopedExternalFunctions.Dispose();

        _processorHandle = state.GetComponentTypeHandle<Processor>(false);
        _processorMemoryBufferHandle = state.GetBufferTypeHandle<ProcessorMemoryBuffer>(false);
        _unitTeamHandle = state.GetComponentTypeHandle<UnitTeam>(true);
        _ghostInstanceHandle = state.GetComponentTypeHandle<GhostInstance>(true);
        _localTransformHandle = state.GetComponentTypeHandle<LocalTransform>(true);
        _bufferedLogPieceHandle = state.GetBufferTypeHandle<BufferedLogPiece>(false);
        _entityHandle = state.GetEntityTypeHandle();

        _radioHandle = state.GetComponentTypeHandle<Radio>(false);
        _radarHandle = state.GetComponentTypeHandle<Radar>(false);
        _radarContactHandle = state.GetBufferTypeHandle<RadarContact>(false);
        _factoryHandle = state.GetComponentTypeHandle<Factory>(true);
        _playerInterfaceHandle = state.GetComponentTypeHandle<PlayerInterface>(true);

        _query = SystemAPI.QueryBuilder().WithAllRW<Processor>().WithAll<LocalToWorld>().Build();

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
                            Age = 0f
                        });
                        goto next;
                    }
                    lines.Add(debugLines[i].Value with
                    {
                        Age = 0f
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
                            Age = 0f
                        });
                        goto next;
                    }
                    labels.Add(worldLabels[i].Value with
                    {
                        Age = 0f
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

        PausableTime t = SystemAPI.GetSingleton<PausableTime>();
        if (t.IsPaused) return;

        debugLines.Clear();
        worldLabels.Clear();

        _processorHandle.Update(ref state);
        _processorMemoryBufferHandle.Update(ref state);
        _unitTeamHandle.Update(ref state);
        _ghostInstanceHandle.Update(ref state);
        _localTransformHandle.Update(ref state);
        _bufferedLogPieceHandle.Update(ref state);
        _entityHandle.Update(ref state);

        _radioHandle.Update(ref state);
        _radarHandle.Update(ref state);
        _radarContactHandle.Update(ref state);
        _factoryHandle.Update(ref state);
        _playerInterfaceHandle.Update(ref state);

        state.Dependency = new ProcessorChunkJob
        {
            ProcessorHandle = _processorHandle,
            ProcessorMemoryBufferHandle = _processorMemoryBufferHandle,
            UnitTeamHandle = _unitTeamHandle,
            GhostInstanceHandle = _ghostInstanceHandle,
            LocalTransformHandle = _localTransformHandle,
            BufferedLogPieceHandle = _bufferedLogPieceHandle,
            EntityHandle = _entityHandle,

            RadioHandle = _radioHandle,
            RadarHandle = _radarHandle,
            RadarContactHandle = _radarContactHandle,
            FactoryHandle = _factoryHandle,
            PlayerInterfaceHandle = _playerInterfaceHandle,

            scopedExternalFunctions = scopedExternalFunctions.AsReadOnly(),
            worldRef = new()
            {
                CommandBuffer = commandBuffer.AsParallelWriter(),
                SortIndex = -1,
                DebugLines = debugLines.AsParallelWriter(),
                WorldLabels = worldLabels.AsParallelWriter(),
                UIElements = uiElements.AsParallelWriter(),
            },
        }.ScheduleParallel(_query, state.Dependency);
    }

#if UNITY_PROFILER
    public static readonly ProfilerMarker __ProcessorJobInner = new("ProcessorJobInner");
#endif

    // TODO [BurstCompile]
    public static void HandleTick(ref Processor processor, ref ProcessorState processorState)
    {
#if UNITY_PROFILER
        __ProcessorJobInner.Begin();
#endif
        try
        {
            for (int i = 0; i < processor.CyclesPerTick && !processorState.IsDone && processorState.Signal == Signal.None; i++)
            {
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
#if UNITY_PROFILER
        __ProcessorJobInner.End();
#endif
    }
}

[BurstCompile]
unsafe partial struct ProcessorChunkJob : IJobChunk
{
    [ReadOnly] public required NativeArray<ExternalFunctionScopedSync>.ReadOnly scopedExternalFunctions;
    public required ProcessorSystemServer.WorldRef worldRef;

    public required ComponentTypeHandle<Processor> ProcessorHandle;
    public required BufferTypeHandle<ProcessorMemoryBuffer> ProcessorMemoryBufferHandle;
    [ReadOnly] public required ComponentTypeHandle<UnitTeam> UnitTeamHandle;
    [ReadOnly] public required ComponentTypeHandle<GhostInstance> GhostInstanceHandle;
    [ReadOnly] public required ComponentTypeHandle<LocalTransform> LocalTransformHandle;
    public required BufferTypeHandle<BufferedLogPiece> BufferedLogPieceHandle;
    [ReadOnly] public required EntityTypeHandle EntityHandle;

    public required ComponentTypeHandle<Radio> RadioHandle;
    public required ComponentTypeHandle<Radar> RadarHandle;
    public required BufferTypeHandle<RadarContact> RadarContactHandle;
    [ReadOnly] public required ComponentTypeHandle<Factory> FactoryHandle;
    [ReadOnly] public required ComponentTypeHandle<PlayerInterface> PlayerInterfaceHandle;

#if UNITY_PROFILER
    public static readonly ProfilerMarker __ProcessorJobOuter = new("ProcessorJobOuter");
#endif

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        Processor* processors = (Processor*)chunk.GetRequiredComponentDataPtrRW(ref ProcessorHandle);
        Entity* entities = chunk.GetEntityDataPtrRO(EntityHandle);
        BufferAccessor<ProcessorMemoryBuffer> memoryBuffers = chunk.GetBufferAccessorRW(ref ProcessorMemoryBufferHandle);
        UnitTeam* unitTeams = (UnitTeam*)chunk.GetRequiredComponentDataPtrRO(ref UnitTeamHandle);
        GhostInstance* ghostInstances = (GhostInstance*)chunk.GetRequiredComponentDataPtrRO(ref GhostInstanceHandle);
        LocalTransform* localTransforms = (LocalTransform*)chunk.GetRequiredComponentDataPtrRO(ref LocalTransformHandle);
        BufferAccessor<BufferedLogPiece> bufferedLogPieces = chunk.GetBufferAccessorRW(ref BufferedLogPieceHandle);

        Radio* radios = (Radio*)(chunk.Has(ref RadioHandle) ? chunk.GetRequiredComponentDataPtrRW(ref RadioHandle) : null);
        Radar* radars = (Radar*)(chunk.Has(ref RadarHandle) ? chunk.GetRequiredComponentDataPtrRW(ref RadarHandle) : null);
        BufferAccessor<RadarContact> radarContactsAccessor = chunk.Has(ref RadarContactHandle) ? chunk.GetBufferAccessorRW(ref RadarContactHandle) : default;

        ChunkEntityEnumerator en = new(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (en.NextEntityIndex(out int i))
        {
            ref Processor processor = ref processors[i];
            ref readonly Entity entity = ref entities[i];
            DynamicBuffer<ProcessorMemoryBuffer> _memory = memoryBuffers[i];
            ref readonly UnitTeam team = ref unitTeams[i];
            ref readonly GhostInstance ghostInstance = ref ghostInstances[i];
            ref readonly LocalTransform localTransform = ref localTransforms[i];
            DynamicBuffer<BufferedLogPiece> _log = bufferedLogPieces[i];

#if UNITY_PROFILER
            using ProfilerMarker.AutoScope _1 = __ProcessorJobOuter.Auto();
#endif

            if (!processor.Source.Code.IsCreated)
            {
                processor.StatusLED.Status = ProcessorStatus.Off;
                continue;
            }

            Span<byte> memory = new(_memory.GetUnsafePtr(), _memory.Length);

            FixedList128Bytes<BufferedLogPiece> log = new();
            ProcessorSystemServer.FunctionScope scope = new()
            {
                WorldRef = worldRef,
                ProcessorRef = new ProcessorSystemServer.ProcessorRef()
                {
                    Memory = _memory.GetUnsafePtr(),
                    MemorySize = _memory.Length,
                    Crash = null,
                    Registers = null,
                    Signal = null,
                },
                EntityRef = new ProcessorSystemServer.EntityRef()
                {
                    Entity = entity,
                    Processor = (Processor*)Unsafe.AsPointer(ref processor),
                    Radio = radios == null ? null : &radios[i],
                    Radar = radars == null ? null : &radars[i],
                    RadarContacts = radars == null ? default : radarContactsAccessor[i],
                    LocalTransform = localTransform,
                    Team = team,
                    Ghost = ghostInstance,
                },
                Log = &log,
            };

            NativeList<ExternalFunctionScopedSync> scopedExternalFunctions = new(this.scopedExternalFunctions.Length + processor.Source.GeneratedFunctions.Length, Allocator.Temp);

            for (int j = 0; j < this.scopedExternalFunctions.Length; j++)
            {
                if ((this.scopedExternalFunctions[j].Id & ProcessorAPI.GlobalPrefix) == ProcessorAPI.GUI.Prefix &&
                    !chunk.Has(ref PlayerInterfaceHandle))
                {
                    continue;
                }

                if ((this.scopedExternalFunctions[j].Id & ProcessorAPI.GlobalPrefix) == ProcessorAPI.Factory.Prefix &&
                    !chunk.Has(ref FactoryHandle))
                {
                    continue;
                }

                if ((this.scopedExternalFunctions[j].Id & ProcessorAPI.GlobalPrefix) == ProcessorAPI.WirelessTransmission.Prefix &&
                    !chunk.Has(ref RadioHandle))
                {
                    continue;
                }

                if ((this.scopedExternalFunctions[j].Id & ProcessorAPI.GlobalPrefix) == ProcessorAPI.Radar.Prefix &&
                    !chunk.Has(ref RadarHandle))
                {
                    continue;
                }

                scopedExternalFunctions.Add(this.scopedExternalFunctions[j]);
                scopedExternalFunctions.GetUnsafePtr()[scopedExternalFunctions.Length - 1].Scope = (nint)(void*)&scope;
            }

            int start = scopedExternalFunctions.Length;
            if (processor.Source.GeneratedFunctions.IsCreated)
            {
                scopedExternalFunctions.AddRange(processor.Source.GeneratedFunctions.Ptr, processor.Source.GeneratedFunctions.Length);
            }

            ProcessorState processorState = new(
                new BytecodeInterpreterSettings(BytecodeInterpreterSettings.Default)
                {
                    MemorySize = memory.Length,
                },
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
                ProcessorDebugSystemServer.HandleTickDebug(ref processor, ref processorState);
            }
            else
            {
                ProcessorSystemServer.HandleTick(ref processor, ref processorState);
            }

            if (processor.Source.GeneratedFunctions.IsCreated)
            {
                for (int j = start; j < scopedExternalFunctions.Length; j++)
                {
                    processor.Source.GeneratedFunctions.Ptr[j - start].Flags = scopedExternalFunctions[j].Flags;
                }
            }

            if (ProcessorSystemServer.MappedMemory(_memory).Leds.CustomLED != 0)
            {
                ProcessorSystemServer.MappedMemory(_memory).Leds.CustomLED = 0;
                processor.CustomLED.Blink();
            }

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

            for (int j = 0; j < log.Length; j++)
            {
                _log.Add(log[j]);
            }
        }
    }
}
