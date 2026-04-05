using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.VFX;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial class VisualEffectSystemClient : SystemBase
{
    ObjectPool<VisualEffectHandlerComponent>[]? Pools;

    protected override void OnCreate()
    {
        RequireForUpdate<VisualEffectDatabase>();
    }

    void SpawnEffect(in VisualEffectSpawn spawn)
    {
        //Vector3 p = MainCamera.Camera.WorldToViewportPoint(spawn.Position);
        //if (p.z < -0.1f || p.x < -0.1f || p.y < -0.1f || p.x > 1.1f || p.y > 1.1f) return;
        //if (math.distancesq(MainCamera.Camera.transform.position, spawn.Position) > 50f * 50f) return;

        VisualEffectHandlerComponent effect = Pools![spawn.Index].Get();
        effect.transform.position = spawn.Position;
        if (effect.VisualEffect.HasVector3("direction")) effect.VisualEffect.SetVector3("direction", (spawn.Rotation * Mathf.Rad2Deg) + new float3(90f, 0f, 0f));
        effect.VisualEffect.Play();
    }

    protected override void OnUpdate()
    {
        if (Pools == null)
        {
            DynamicBuffer<BufferedVisualEffect> database = SystemAPI.GetSingletonBuffer<BufferedVisualEffect>(true);
            Pools = new ObjectPool<VisualEffectHandlerComponent>[database.Length];
            for (int i = 0; i < Pools.Length; i++)
            {
                int _i = i;
                BufferedVisualEffect asset = database[_i];
                Pools[_i] = new ObjectPool<VisualEffectHandlerComponent>(
                    createFunc: () =>
                    {
                        GameObject gameObject = new($"Effect {_i}");

                        VisualEffectHandlerComponent handlerComponent = gameObject.AddComponent<VisualEffectHandlerComponent>();
                        handlerComponent.Lifetime = asset.Duration;
                        handlerComponent.Asset = asset;
                        handlerComponent.Pool = Pools[_i];

                        VisualEffect visualEffect = handlerComponent.VisualEffect = gameObject.AddComponent<VisualEffect>();
                        visualEffect.visualEffectAsset = asset.VisualEffect.Value;

                        if (!asset.LightColor.Equals(default) && asset.LightRange > 0f && asset.LightIntensity > 0f)
                        {
                            Light light = handlerComponent.Light = gameObject.AddComponent<Light>();
                            light.color = new Color(asset.LightColor.x, asset.LightColor.y, asset.LightColor.z);
                            light.intensity = 0f;
                            light.range = asset.LightRange;
                            light.enabled = false;
                        }

                        return handlerComponent;
                    },
                    actionOnGet: static v =>
                    {
                        v.gameObject.SetActive(true);
                        v.GetComponent<VisualEffectHandlerComponent>().Reinit();
                        v.Reinit();
                    },
                    actionOnRelease: static v => v.gameObject.SetActive(false),
                    actionOnDestroy: static v => Object.Destroy(v.gameObject));
            }
        }

        EntityCommandBuffer commandBuffer = default;

        foreach (var (spawn, entity) in
            SystemAPI.Query<RefRO<VisualEffectSpawn>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
            commandBuffer.DestroyEntity(entity);

            SpawnEffect(in spawn.ValueRO);
        }

        foreach (var (command, entity) in
            SystemAPI.Query<RefRO<VisualEffectRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
            commandBuffer.DestroyEntity(entity);

            SpawnEffect(new VisualEffectSpawn()
            {
                Index = command.ValueRO.Index,
                Position = command.ValueRO.Position,
                Rotation = command.ValueRO.Rotation,
            });
        }
    }

    public void OnDisconnect()
    {
        if (Pools == null) return;

        Debug.Log($"{DebugEx.ClientPrefix} Clearing VFX pools ...");

        foreach (ObjectPool<VisualEffectHandlerComponent> item in Pools)
        {
            item.Clear();
        }

        Debug.Log($"{DebugEx.ClientPrefix} Destroying VFX instances ...");

        foreach (VisualEffectHandlerComponent vfx in Object.FindObjectsByType<VisualEffectHandlerComponent>(FindObjectsInactive.Include))
        {
            Object.Destroy(vfx.gameObject);
        }
    }
}
