using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Builder")]
public class BuilderAuthoring : MonoBehaviour
{
    [SerializeField] AllPrefabs? Prefabs;
    [SerializeField] UnitPrefab? Prefab;

    class Baker : Baker<BuilderAuthoring>
    {
        public override void Bake(BuilderAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Builder>(entity);
            AddComponent<SelectableUnit>(entity);
            AddComponent<EntityWithInfoUI>(entity);
            AddComponent<UnitTeam>(entity);
            if (authoring.Prefab != null)
            {
                if (authoring.Prefabs == null)
                {
                    Debug.LogError($"Prefab is not null but the prefab database is", authoring);
                }
                else
                {
                    int index = authoring.Prefabs.Units.IndexOf(v => v == authoring.Prefab);
                    if (index == -1) Debug.LogError($"Invalid prefab instance", authoring);
                    AddComponent<UnitPrefabInstance>(entity, new()
                    {
                        Index = index
                    });
                }
            }
        }
    }
}
