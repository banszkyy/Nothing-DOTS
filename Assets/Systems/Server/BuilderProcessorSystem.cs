using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;

[BurstCompile]
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateBefore(typeof(LocalToWorldSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct BuilderProcessorSystem : ISystem
{
    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        foreach (var (processor, memory, builderTurret) in
            SystemAPI.Query<RefRW<Processor>, DynamicBuffer<ProcessorMemoryBuffer>, RefRW<BuilderTurret>>()
            .WithAll<Builder>())
        {
            ref MappedMemory mapped = ref ProcessorSystemServer.MappedMemory(memory);

            if (mapped.CombatTurret.InputShoot is not 0 and not 2)
            {
                builderTurret.ValueRW.ShootRequested = true;
            }
            mapped.CombatTurret.InputShoot = builderTurret.ValueRO.TargetState;

            RefRW<LocalTransform> turretTransform = SystemAPI.GetComponentRW<LocalTransform>(builderTurret.ValueRO.Turret);

            turretTransform.ValueRO.Rotation.ToEuler(out float3 turretEuler);

            mapped.CombatTurret.TurretCurrentRotation = turretEuler.y;
            mapped.CombatTurret.TurretCurrentAngle = 0f;

            if (float.IsFinite(mapped.CombatTurret.TurretTargetRotation))
            {
                float y = Utils.MoveTowardsAngle(turretEuler.y, mapped.CombatTurret.TurretTargetRotation, builderTurret.ValueRO.TurretRotationSpeed * SystemAPI.Time.DeltaTime);
                turretTransform.ValueRW.Rotation = quaternion.EulerXYZ(0, y, 0);
            }
        }
    }
}
