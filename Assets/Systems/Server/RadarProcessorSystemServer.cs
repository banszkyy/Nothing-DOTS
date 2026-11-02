using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;

[BurstCompile]
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateBefore(typeof(LocalToWorldSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct RadarProcessorSystemServer : ISystem
{
    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        foreach (var (processor, memory, radar) in
            SystemAPI.Query<RefRW<Processor>, DynamicBuffer<ProcessorMemoryBuffer>, RefRW<Radar>>())
        {
            ref MappedMemory mapped = ref ProcessorSystemServer.MappedMemory(memory);

            if (!float.IsFinite(mapped.Radar.RadarDirection)) continue;

            quaternion target = quaternion.EulerXYZ(
                0f,
                -mapped.Radar.RadarDirection + math.PIHALF,
                0f);
            RefRW<LocalTransform> radarTransform = SystemAPI.GetComponentRW<LocalTransform>(radar.ValueRO.Transform);

            // const float speed = 720f;
            // Utils.RotateTowards(ref radarTransform.ValueRW.Rotation, target, speed * SystemAPI.Time.DeltaTime);
            radarTransform.ValueRW.Rotation = target;

            mapped.Radar.RadarResponse = processor.ValueRO.RadarResponse;
        }
    }
}
