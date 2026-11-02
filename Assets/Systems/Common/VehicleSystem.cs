using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateBefore(typeof(LocalToWorldSystem))]
[UpdateBefore(typeof(TerrainCollisionSystemServer))]
partial struct VehicleSystem : ISystem
{
    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        foreach (var (transform, vehicle) in
            SystemAPI.Query<RefRW<LocalTransform>, RefRW<Vehicle>>())
        {
            vehicle.ValueRW.Speed = math.lerp(vehicle.ValueRO.Speed, vehicle.ValueRO.Input.y * vehicle.ValueRO.MaxSpeed, vehicle.ValueRO.Acceleration * SystemAPI.Time.DeltaTime);
            if (math.abs(vehicle.ValueRO.Speed) <= 0.001f && math.abs(vehicle.ValueRO.Input.x) <= 0.001f) continue;

            transform.ValueRW.Position += transform.ValueRO.Forward() * (vehicle.ValueRO.Speed * SystemAPI.Time.DeltaTime);

            float steer = math.radians(vehicle.ValueRO.Input.x * vehicle.ValueRO.Steering * SystemAPI.Time.DeltaTime);

            float3 forward = math.rotate(transform.ValueRO.Rotation, new float3(0, 0, 1));
            float3 up = transform.ValueRO.Up();

            float3 newForward = math.rotate(quaternion.AxisAngle(up, steer), forward);

            transform.ValueRW.Rotation = quaternion.LookRotationSafe(newForward, up);
        }
    }
}
