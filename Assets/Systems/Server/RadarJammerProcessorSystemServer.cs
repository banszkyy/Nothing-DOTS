using Unity.Entities;
using Unity.Transforms;
using Unity.Burst;

[BurstCompile]
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateBefore(typeof(LocalToWorldSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct RadarJammerProcessorSystemServer : ISystem
{
    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        foreach (var (processor, memory, radarJammer) in
            SystemAPI.Query<RefRW<Processor>, DynamicBuffer<ProcessorMemoryBuffer>, RefRW<RadarJammer>>())
        {
            ref MappedMemory mapped = ref ProcessorSystemServer.MappedMemory(memory);
            radarJammer.ValueRW.IsActive = mapped.RadarJammer.Signal != 0;
        }
    }
}
