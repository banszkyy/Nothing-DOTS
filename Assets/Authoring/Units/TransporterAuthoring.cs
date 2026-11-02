using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Transporter")]
public class TransporterAuthoring : MonoBehaviour
{
    [SerializeField] AllPrefabs? Prefabs;
    [SerializeField] UnitPrefab? Prefab;

    [SerializeField] Transform? LoadPoint = default;

    class Baker : Baker<TransporterAuthoring>
    {
        public override void Bake(TransporterAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Transporter>(entity, new()
            {
                LoadPoint = authoring.LoadPoint != null ? authoring.LoadPoint.localPosition : default,
            });
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
