using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.VFX;

class PauseManager : Singleton<PauseManager>
{
    [SerializeField, NotNull] UIDocument? UI = null;

    public bool IsPaused { get; private set; }

    readonly List<IPausableBehavior> pausableBehaviors = new();

    void Start()
    {
        UI.rootVisualElement.style.display = DisplayStyle.None;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            if (IsPaused) Resume();
            else Pause();
        }
    }

    public void Pause()
    {
        UI.rootVisualElement.style.display = DisplayStyle.Flex;

        IsPaused = true;

        World?[] worlds = new World?[]
        {
            NetcodeBootstrap.ClientWorld,
            NetcodeBootstrap.ServerWorld,
            NetcodeBootstrap.LocalWorld,
            NetcodeBootstrap.StagingWorld,
        };

        foreach (var world in worlds)
        {
            if (world == null) continue;
            world.EntityManager.GetSingletonRW<PausableTime>().IsPaused = true;
        }

        foreach (MonoBehaviour? item in pausableBehaviors.Cast<MonoBehaviour>())
        {
            if (item == null) continue;
            item.enabled = false;
        }

        foreach (VisualEffect item in FindObjectsByType<VisualEffect>(FindObjectsInactive.Include))
        {
            item.pause = true;
        }

        foreach (ParticleSystem item in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include))
        {
            item.Pause();
        }
    }

    public void Resume()
    {
        UI.rootVisualElement.style.display = DisplayStyle.None;

        IsPaused = false;

        World?[] worlds = new World?[]
        {
            NetcodeBootstrap.ClientWorld,
            NetcodeBootstrap.ServerWorld,
            NetcodeBootstrap.LocalWorld,
            NetcodeBootstrap.StagingWorld,
        };

        foreach (var world in worlds)
        {
            if (world == null) continue;
            world.EntityManager.GetSingletonRW<PausableTime>().IsPaused = false;
        }

        foreach (MonoBehaviour? item in pausableBehaviors.Cast<MonoBehaviour>())
        {
            if (item == null) continue;
            item.enabled = true;
        }

        foreach (VisualEffect item in FindObjectsByType<VisualEffect>(FindObjectsInactive.Include))
        {
            item.pause = false;
        }

        foreach (ParticleSystem item in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include))
        {
            item.Play();
        }
    }

    public void Register(IPausableBehavior pausableBehavior)
    {
        pausableBehaviors.Add(pausableBehavior);
    }

    public void Unregister(IPausableBehavior pausableBehavior)
    {
        pausableBehaviors.Remove(pausableBehavior);
    }
}
