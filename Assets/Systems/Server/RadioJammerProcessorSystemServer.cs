using Unity.Entities;
using Unity.Burst;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct RadioJammerProcessorSystemServer : ISystem
{
    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        foreach (var (processor, memory, radioJammer) in
            SystemAPI.Query<RefRW<Processor>, DynamicBuffer<ProcessorMemoryBuffer>, RefRW<RadioJammer>>())
        {
            ref MappedMemory mapped = ref ProcessorSystemServer.MappedMemory(memory);

            radioJammer.ValueRW.IsActive = mapped.RadioJammer.Signal != 0;
        }
    }
}
