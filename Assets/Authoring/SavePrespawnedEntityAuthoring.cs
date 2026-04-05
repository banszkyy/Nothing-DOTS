using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Save Prespawned Entity")]
class SavePrespawnedEntityAuthoring : MonoBehaviour
{
    class Baker : Baker<SavePrespawnedEntityAuthoring>
    {
        public override void Bake(SavePrespawnedEntityAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            SavePrespawnedEntityAuthoring[] all = FindObjectsByType<SavePrespawnedEntityAuthoring>(FindObjectsInactive.Exclude);
            AddComponent<SavePrespawnedEntity>(entity, new()
            {
                Id = $"{all.IndexOf(v => v == authoring)}".ToString(),
            });
        }
    }
}
