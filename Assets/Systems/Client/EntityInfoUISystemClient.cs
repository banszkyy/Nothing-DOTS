using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateAfter(typeof(TransformSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial class EntityInfoUISystemClient : SystemBase
{
#if UNITY_EDITOR && ENABLE_PROFILER
    static readonly Unity.Profiling.ProfilerMarker __instantiateUI = new($"{nameof(EntityInfoUISystemClient)}.InstantiateUI");
    static readonly Unity.Profiling.ProfilerMarker __destroyUI = new($"{nameof(EntityInfoUISystemClient)}.DestroyUI");
#endif

    Transform? _canvas;

    protected override void OnUpdate()
    {
        EntityCommandBuffer commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

        if (_canvas == null)
        {
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (canvas.name != "UICanvas") continue;
                _canvas = canvas.transform;
                break;
            }
        }

        foreach (var (transform, selectable, entity) in
            SystemAPI.Query<RefRO<LocalTransform>, RefRO<SelectableUnit>>()
            .WithNone<EntityInfoUIReference>()
            .WithAll<EntityWithInfoUI>()
            .WithEntityAccess())
        {
            if (selectable.ValueRO.Status == SelectionStatus.None) continue;

#if UNITY_EDITOR && ENABLE_PROFILER
            using Unity.Profiling.ProfilerMarker.AutoScope _ = __instantiateUI.Auto();
#endif

            GameObject uiPrefab = SystemAPI.ManagedAPI.GetSingleton<UIPrefabs>().EntityInfo;
            float3 spawnPosition = transform.ValueRO.Position;
            GameObject newUi = Object.Instantiate(uiPrefab, spawnPosition, Quaternion.identity, _canvas);
            EntityInfoUI comp = newUi.GetComponent<EntityInfoUI>();

            if (SystemAPI.HasComponent<MeshBounds>(entity))
            {
                comp.Bounds = SystemAPI.GetComponent<MeshBounds>(entity).Bounds;
            }

            commandBuffer.AddComponent<EntityInfoUIReference>(entity, new()
            {
                Value = comp,
            });

            break;
        }

        foreach (var (uiRef, damageable) in
            SystemAPI.Query<EntityInfoUIReference, RefRO<Damageable>>())
        {
            uiRef.Value.HealthPercent = damageable.ValueRO.Health / damageable.ValueRO.MaxHealth;
        }

        foreach (var (uiRef, turret) in
            SystemAPI.Query<EntityInfoUIReference, RefRO<CombatTurret>>())
        {
            if (turret.ValueRO.MagazineSize == 1 && turret.ValueRO.MagazineReload == 0f)
            {
                uiRef.Value.ReloadProgressPercent = uiRef.Value.MagazineProgressPercent = turret.ValueRO.BulletReload == 0f ? 1f : turret.ValueRO.BulletReloadProgress / turret.ValueRO.BulletReload;
            }
            else if (turret.ValueRO.CurrentMagazineSize == 0)
            {
                uiRef.Value.ReloadProgressPercent = uiRef.Value.MagazineProgressPercent = turret.ValueRO.MagazineReload == 0f ? 1f : turret.ValueRO.MagazineReloadProgress / turret.ValueRO.MagazineReload;
            }
            else
            {
                uiRef.Value.MagazineProgressPercent = turret.ValueRO.MagazineSize == 0 ? 1f : (float)turret.ValueRO.CurrentMagazineSize / (float)turret.ValueRO.MagazineSize;
                uiRef.Value.ReloadProgressPercent = turret.ValueRO.BulletReload == 0f ? 1f : turret.ValueRO.BulletReloadProgress / turret.ValueRO.BulletReload;
            }
        }

        foreach (var (uiRef, buildingPlaceholder) in
            SystemAPI.Query<EntityInfoUIReference, RefRO<BuildingPlaceholder>>())
        {
            uiRef.Value.BuildingProgressPercent = buildingPlaceholder.ValueRO.CurrentProgress / buildingPlaceholder.ValueRO.TotalProgress;
        }

        foreach (var (uiRef, transporter) in
            SystemAPI.Query<EntityInfoUIReference, RefRO<Transporter>>())
        {
            uiRef.Value.TransporterLoadPercent = (float)transporter.ValueRO.CurrentLoad / (float)Transporter.Capacity;
            uiRef.Value.TransporterProgressPercent = transporter.ValueRO.LoadProgress;
        }

        foreach (var (uiRef, extractor) in
            SystemAPI.Query<EntityInfoUIReference, RefRO<Extractor>>())
        {
            uiRef.Value.ExtractorProgressPercent = extractor.ValueRO.ExtractProgress;
        }

        foreach (var (uiRef, transform) in
            SystemAPI.Query<EntityInfoUIReference, RefRO<LocalTransform>>())
        {
            uiRef.Value.Position = transform.ValueRO.Position;
            uiRef.Value.Rotation = transform.ValueRO.Rotation;
        }

        foreach (var (uiRef, unitTeam, selectable) in
            SystemAPI.Query<EntityInfoUIReference, RefRO<UnitTeam>, RefRO<SelectableUnit>>())
        {
            uiRef.Value.SelectionStatus = selectable.ValueRO.Status;
            uiRef.Value.Team = unitTeam.ValueRO.Team;
            uiRef.Value.gameObject.SetActive(selectable.ValueRO.Status != SelectionStatus.None);
        }

        foreach (var (uiRef, entity) in
            SystemAPI.Query<EntityInfoUIReference>()
            .WithNone<EntityWithInfoUI>()
            .WithEntityAccess())
        {
#if UNITY_EDITOR && ENABLE_PROFILER
            using Unity.Profiling.ProfilerMarker.AutoScope _ = __destroyUI.Auto();
#endif

            Object.Destroy(uiRef.Value.gameObject);
            commandBuffer.RemoveComponent<EntityInfoUIReference>(entity);
        }
    }

    public void OnDisconnect()
    {

    }
}
