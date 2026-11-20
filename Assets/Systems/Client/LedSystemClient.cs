using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct LedSystemClient : ISystem
{
    ComponentLookup<URPMaterialPropertyEmissionColor> _emissionColorQ;

    const float LedIntensity = 4f;
    const float LedFlashTime = 0.03f;

    static float4 Off => default;
    static float4 Green => new float4(0.1f, 1f, 0.2f, 1f) * LedIntensity;
    static float4 Amber => new float4(1f, 0.8f, 0f, 1f) * LedIntensity;
    static float4 Red => new float4(1f, 0.1f, 0f, 1f) * LedIntensity;
    static float4 Blue => new float4(0.1f, 0.2f, 1f, 1f) * LedIntensity;

    [BurstCompile]
    void ISystem.OnCreate(ref SystemState state)
    {
        _emissionColorQ = state.GetComponentLookup<URPMaterialPropertyEmissionColor>();
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        _emissionColorQ.Update(ref state);
        PausableTime t = SystemAPI.GetSingleton<PausableTime>();

        foreach (RefRW<Processor> processor in
            SystemAPI.Query<RefRW<Processor>>())
        {
            if (processor.ValueRO.StatusLED.Entity != Entity.Null)
            {
                _emissionColorQ.GetRefRW(processor.ValueRO.StatusLED.Entity).ValueRW.Value = processor.ValueRO.StatusLED.Status switch
                {
                    ProcessorStatus.Off => Off,
                    ProcessorStatus.Running => Green,
                    ProcessorStatus.Halted => Amber,
                    ProcessorStatus.Error => Red,
                    _ => throw new UnreachableException(),
                };
            }

            if (processor.ValueRO.NetworkSendLED.Entity != Entity.Null)
            {
                _emissionColorQ.GetRefRW(processor.ValueRO.NetworkSendLED.Entity).ValueRW.Value =
                    processor.ValueRW.NetworkSendLED.ReceiveBlink((float)t.ElapsedTime, LedFlashTime) ? Blue : Off;
            }

            if (processor.ValueRO.NetworkReceiveLED.Entity != Entity.Null)
            {
                _emissionColorQ.GetRefRW(processor.ValueRO.NetworkReceiveLED.Entity).ValueRW.Value =
                    processor.ValueRW.NetworkReceiveLED.ReceiveBlink((float)t.ElapsedTime, LedFlashTime, out RadioLedStatus status) ?
                    (status switch
                    {
                        RadioLedStatus.Success => Blue,
                        RadioLedStatus.Jammed => Red,
                        RadioLedStatus.Overflow => Amber,
                        _ => throw new UnreachableException(),
                    }) :
                    Off;
            }

            if (processor.ValueRO.CustomLED.Entity != Entity.Null)
            {
                _emissionColorQ.GetRefRW(processor.ValueRO.CustomLED.Entity).ValueRW.Value =
                    processor.ValueRW.CustomLED.ReceiveBlink((float)t.ElapsedTime, LedFlashTime) ? Green : Off;
            }
        }

        foreach (RefRW<Radar> processor in
            SystemAPI.Query<RefRW<Radar>>())
        {
            if (processor.ValueRO.RequestLED.Entity != Entity.Null)
            {
                _emissionColorQ.GetRefRW(processor.ValueRO.RequestLED.Entity).ValueRW.Value =
                    processor.ValueRW.RequestLED.ReceiveBlink((float)t.ElapsedTime, LedFlashTime) ? Blue : Off;
            }

            if (processor.ValueRO.ResponseLED.Entity != Entity.Null)
            {
                _emissionColorQ.GetRefRW(processor.ValueRO.ResponseLED.Entity).ValueRW.Value =
                    processor.ValueRW.ResponseLED.ReceiveBlink((float)t.ElapsedTime, LedFlashTime) ? Blue : Off;
            }
        }
    }
}
