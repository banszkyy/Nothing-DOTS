using System.Diagnostics.CodeAnalysis;
using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Prefab Database")]
class PrefabDatabaseAuthoring : MonoBehaviour
{
    [SerializeField, NotNull] AllPrefabs? Prefabs = default;

    class Baker : Baker<PrefabDatabaseAuthoring>
    {
        public override void Bake(PrefabDatabaseAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<PrefabDatabase>(entity, new()
            {
                Player = GetEntity(authoring.Prefabs.PlayerPrefab, TransformUsageFlags.Dynamic),
                VirtualPlayer = GetEntity(authoring.Prefabs.VirtualPlayerPrefab, TransformUsageFlags.Dynamic),
                CoreComputer = GetEntity(authoring.Prefabs.CoreComputerPrefab, TransformUsageFlags.Dynamic),
                Builder = GetEntity(authoring.Prefabs.Builder.Prefab, TransformUsageFlags.Dynamic),
                Resource = GetEntity(authoring.Prefabs.Resource, TransformUsageFlags.Dynamic),
            });
        }
    }
}
