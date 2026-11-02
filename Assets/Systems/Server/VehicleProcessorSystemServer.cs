using Unity.Entities;
using Unity.Mathematics;
using Unity.Burst;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct VehicleProcessorSystemServer : ISystem
{
    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        foreach (var (processor, memory, vehicle) in
            SystemAPI.Query<RefRO<Processor>, DynamicBuffer<ProcessorMemoryBuffer>, RefRW<Vehicle>>())
        {
            ref MappedMemory mapped = ref ProcessorSystemServer.MappedMemory(memory);

            vehicle.ValueRW.Input = new float2(
                mapped.Vehicle.InputSteer / 128f,
                mapped.Vehicle.InputForward / 128f
            );
        }
    }
}
