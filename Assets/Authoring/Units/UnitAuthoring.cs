using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Unit")]
public class UnitAuthoring : MonoBehaviour
{
    [SerializeField] AllPrefabs? Prefabs;
    [SerializeField] UnitPrefab? Prefab;
    [SerializeField] int Team;

    class Baker : Baker<UnitAuthoring>
    {
        public override void Bake(UnitAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Unit>(entity, new());
            AddComponent<SelectableUnit>(entity);
            AddComponent<EntityWithInfoUI>(entity);
            AddComponent<UnitTeam>(entity, new()
            {
                Team = authoring.Team,
            });
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
