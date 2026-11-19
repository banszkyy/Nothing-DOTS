#if UNITY_EDITOR && EDITOR_DEBUG
#define _DEBUG_LINES
#endif

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct BuilderSystemServer : ISystem
{
    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        PausableTime t = SystemAPI.GetSingleton<PausableTime>();
        if (t.IsPaused) return;

        var map = QuadrantSystem.GetMap(ref state);

        foreach (var (processor, memory, turret) in
            SystemAPI.Query<RefRW<Processor>, DynamicBuffer<ProcessorMemoryBuffer>, RefRW<BuilderTurret>>())
        {
            ref MappedMemory mapped = ref ProcessorSystemServer.MappedMemory(memory);

            RefRW<LocalTransform> turretTransform = SystemAPI.GetComponentRW<LocalTransform>(turret.ValueRO.Turret);

            turretTransform.ValueRO.Rotation.ToEuler(out float3 turretEuler);

            mapped.BuilderTurret.TurretCurrentRotation = turretEuler.y;
            mapped.BuilderTurret.TurretCurrentAngle = 0f;

            if (float.IsFinite(mapped.BuilderTurret.TurretTargetRotation))
            {
                float y = Utils.MoveTowardsAngle(turretEuler.y, mapped.BuilderTurret.TurretTargetRotation, turret.ValueRO.TurretRotationSpeed * SystemAPI.Time.DeltaTime);
                turretTransform.ValueRW.Rotation = quaternion.EulerXYZ(0, y, 0);
            }

            if (mapped.BuilderTurret.Command != BuilderTurretCommand.None) continue;
            mapped.BuilderTurret.Command = BuilderTurretCommand.None;

            RefRW<LocalToWorld> turretWorldTransform = SystemAPI.GetComponentRW<LocalToWorld>(turret.ValueRO.Turret);

            Ray ray = new(turretWorldTransform.ValueRO.Position, turretWorldTransform.ValueRO.Forward, Builder.BuildRadius, Layers.BuildingPlaceholder, false);

#if DEBUG_LINES
            DebugEx.DrawLine(ray.Start, ray.End, Color.white, 0.2f, false);
#endif

            if (!QuadrantRayCast.Raycast(map, ray, out Hit hit))
            { continue; }

#if DEBUG_LINES
            DebugEx.DrawPoint(ray.GetPoint(hit.Distance), 1f, Color.white, 0.2f, false);
#endif

            if (SystemAPI.HasComponent<BuildingPlaceholder>(hit.Entity.Entity))
            {
                RefRW<BuildingPlaceholder> building = SystemAPI.GetComponentRW<BuildingPlaceholder>(hit.Entity.Entity);
                building.ValueRW.CurrentProgress += Builder.BuildSpeed * SystemAPI.Time.DeltaTime;
                mapped.BuilderTurret.Command = BuilderTurretCommand.OkResponse;
#if DEBUG_LINES
                DebugEx.DrawPoint(ray.GetPoint(hit.Distance), 1f, Color.green, 0.2f, false);
#endif
            }
        }
    }
}
