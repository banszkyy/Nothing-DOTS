using System;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.VFX;

class VisualEffectHandlerComponent : MonoBehaviour, IPausableBehavior
{
    internal float Lifetime;
    internal float Age;
    internal BufferedVisualEffect Asset;
    [NotNull] internal VisualEffect? VisualEffect = null;
    internal Light? Light;
    internal ObjectPool<VisualEffectHandlerComponent>? Pool;

    void Start()
    {
        PauseManager.Instance.Register(this);
    }

    void OnDestroy()
    {
        if (PauseManager.InstanceOrNull != null)
        {
            PauseManager.InstanceOrNull.Unregister(this);
        }
    }

    public void Reinit()
    {
        Age = 0f;

        if (Light != null)
        {
            Light.range = Asset.LightRange;
            Light.intensity = 0f;
            Light.enabled = true;
        }
    }

    void Update()
    {
        Age += Time.deltaTime;

        if (Age >= Lifetime && (VisualEffect.aliveParticleCount == 0 || VisualEffect.culled))
        {
            if (Light != null)
            {
                Light.range = Asset.LightRange;
                Light.intensity = 0f;
                Light.enabled = false;
            }
            Pool!.Release(this);
        }
        else
        {
            if (Light != null)
            {
                float t = 1f - (Math.Min(Age, Lifetime) / Lifetime);
                Light.intensity = Asset.LightIntensity * t;
                Light.range = Asset.LightRange;
            }
        }
    }
}
