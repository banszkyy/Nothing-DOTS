using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Rendering;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct ProcessorSystemClient : ISystem
{
    const float LedIntensity = 10f;
    ComponentLookup<URPMaterialPropertyEmissionColor> _emissionColorQ;
    public NativeList<UserUIElement> uiElements;

    [BurstCompile]
    void ISystem.OnCreate(ref SystemState state)
    {
        _emissionColorQ = state.GetComponentLookup<URPMaterialPropertyEmissionColor>();
        uiElements = new(256, Allocator.Persistent);
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;

        _emissionColorQ.Update(ref state);

        foreach (RefRW<Processor> processor in
            SystemAPI.Query<RefRW<Processor>>())
        {
            if (processor.ValueRO.StatusLED.Entity != Entity.Null)
            {
                _emissionColorQ.GetRefRW(processor.ValueRO.StatusLED.Entity).ValueRW.Value = processor.ValueRO.StatusLED.Status switch
                {
                    ProcessorStatus.Off => default,
                    ProcessorStatus.Running => new float4(0f, 1f, 0f, 1f) * LedIntensity,
                    ProcessorStatus.Halted => new float4(1f, 1f, 0f, 1f) * LedIntensity,
                    ProcessorStatus.Error => new float4(1f, 0f, 0f, 1f) * LedIntensity,
                    _ => throw new UnreachableException(),
                };
            }

            if (processor.ValueRO.NetworkSendLED.Entity != Entity.Null)
            {
                _emissionColorQ.GetRefRW(processor.ValueRO.NetworkSendLED.Entity).ValueRW.Value =
                    processor.ValueRW.NetworkSendLED.ReceiveBlink() ?
                    new float4(0.1f, 0.2f, 1f, 1f) * LedIntensity :
                    default;
            }

            if (processor.ValueRO.NetworkReceiveLED.Entity != Entity.Null)
            {
                _emissionColorQ.GetRefRW(processor.ValueRO.NetworkReceiveLED.Entity).ValueRW.Value =
                    processor.ValueRW.NetworkReceiveLED.ReceiveBlink() ?
                    new float4(0.1f, 0.2f, 1f, 1f) * LedIntensity :
                    default;
            }

            if (processor.ValueRO.RadarLED.Entity != Entity.Null)
            {
                _emissionColorQ.GetRefRW(processor.ValueRO.RadarLED.Entity).ValueRW.Value =
                    processor.ValueRW.RadarLED.ReceiveBlink() ?
                    new float4(0.1f, 0.2f, 1f, 1f) * LedIntensity :
                    default;
            }

            if (processor.ValueRO.USBLED.Entity != Entity.Null)
            {
                _emissionColorQ.GetRefRW(processor.ValueRO.USBLED.Entity).ValueRW.Value =
                    processor.ValueRW.USBLED.ReceiveBlink() ?
                    new float4(0.1f, 0.2f, 1f, 1f) * LedIntensity :
                    default;
            }

            if (processor.ValueRO.CustomLED.Entity != Entity.Null)
            {
                _emissionColorQ.GetRefRW(processor.ValueRO.CustomLED.Entity).ValueRW.Value =
                    processor.ValueRW.CustomLED.ReceiveBlink() ?
                    new float4(0.1f, 0.2f, 1f, 1f) * LedIntensity :
                    default;
            }
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<UIElementUpdateRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);

            for (int i = 0; i < uiElements.Length; i++)
            {
                if (uiElements[i].Id != command.ValueRO.UIElement.Id) continue;
                uiElements[i] = command.ValueRO.UIElement with { IsDirty = true };
                goto next;
            }

            uiElements.Add(command.ValueRO.UIElement);

        next:;
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<UIElementDestroyRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);

            for (int i = 0; i < uiElements.Length; i++)
            {
                if (uiElements[i].Id != command.ValueRO.Id) continue;
                uiElements.RemoveAt(i);
                break;
            }
        }
    }

    public void OnDisconnect()
    {
        Debug.Log($"{DebugEx.ClientPrefix} Clearing user UI elements");

        uiElements.Clear();
    }
}
