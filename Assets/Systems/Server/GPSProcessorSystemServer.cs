using Unity.Entities;
using Unity.Transforms;
using Unity.Burst;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct GPSProcessorSystemServer : ISystem
{
    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        foreach (var (processor, memory, transform) in
            SystemAPI.Query<RefRW<Processor>, DynamicBuffer<ProcessorMemoryBuffer>, RefRW<LocalToWorld>>())
        {
            ref MappedMemory mapped = ref ProcessorSystemServer.MappedMemory(memory);

            mapped.GPS.Position = transform.ValueRO.Position;
            mapped.GPS.Forward = transform.ValueRO.Forward;
        }
    }
}
