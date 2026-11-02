using Unity.Entities;
using Unity.Burst;
using LanguageCore.Runtime;
using Unity.Collections;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct FacilityProcessorSystemServer : ISystem
{
    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        foreach (var (processor, memory, facility, techIn, techOut) in
            SystemAPI.Query<RefRW<Processor>, DynamicBuffer<ProcessorMemoryBuffer>, RefRW<Facility>, DynamicBuffer<BufferedTechnologyHashIn>, DynamicBuffer<BufferedTechnologyHashOut>>())
        {
            ref MappedMemory mapped = ref ProcessorSystemServer.MappedMemory(memory);

            switch (mapped.Facility.Signal)
            {
                case MappedMemory_Facility.SignalEnqueueHash:
                {
                    mapped.Facility.Signal = 0;
                    if (mapped.Facility.HashLocation != 0)
                    {
                        FixedBytes30 hash = memory.AsReadOnlySpan().Get<FixedBytes30>(mapped.Facility.HashLocation);
                        techIn.Add(new() { Hash = hash });
                    }
                    break;
                }
                case MappedMemory_Facility.SignalDequeueHash:
                {
                    if (mapped.Facility.HashLocation != 0)
                    {
                        if (techOut.IsEmpty)
                        {
                            mapped.Facility.Signal = MappedMemory_Facility.SignalDequeueFailure;
                        }
                        else
                        {
                            mapped.Facility.Signal = MappedMemory_Facility.SignalDequeueSuccess;
                            BufferedTechnologyHashOut tech = techOut[0];
                            techOut.RemoveAt(0);
                            memory.AsSpan().Set(mapped.Facility.HashLocation, tech.Hash);
                        }
                    }
                    break;
                }
            }
        }
    }
}
