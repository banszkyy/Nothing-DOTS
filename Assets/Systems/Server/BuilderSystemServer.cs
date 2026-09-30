#if UNITY_EDITOR && EDITOR_DEBUG
#define _DEBUG_LINES
#endif

using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct BuilderSystemServer : ISystem
{
    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        var map = QuadrantSystem.GetMap(ref state);

        foreach (var turret in
            SystemAPI.Query<RefRW<BuilderTurret>>())
        {
            if (!turret.ValueRO.ShootRequested) continue;
            turret.ValueRW.ShootRequested = false;
            turret.ValueRW.TargetState = 0;

            RefRW<LocalToWorld> turretTransform = SystemAPI.GetComponentRW<LocalToWorld>(turret.ValueRO.Turret);

            Ray ray = new(turretTransform.ValueRO.Position, turretTransform.ValueRO.Forward, Builder.BuildRadius, Layers.BuildingPlaceholder, false);

#if DEBUG_LINES
            DebugEx.DrawLine(ray.Start, ray.End, Color.white, 0.2f, false);
#endif

            if (!QuadrantRayCast.RayCast(map, ray, out Hit hit))
            { continue; }

#if DEBUG_LINES
            DebugEx.DrawPoint(ray.GetPoint(hit.Distance), 1f, Color.white, 0.2f, false);
#endif

            if (SystemAPI.HasComponent<BuildingPlaceholder>(hit.Entity.Entity))
            {
                RefRW<BuildingPlaceholder> building = SystemAPI.GetComponentRW<BuildingPlaceholder>(hit.Entity.Entity);
                building.ValueRW.CurrentProgress += Builder.BuildSpeed * SystemAPI.Time.DeltaTime;
                turret.ValueRW.TargetState = 2;
#if DEBUG_LINES
                DebugEx.DrawPoint(ray.GetPoint(hit.Distance), 1f, Color.green, 0.2f, false);
#endif
            }
        }
    }
}
