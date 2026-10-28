using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Player Interface")]
class PlayerInterfaceAuthoring : MonoBehaviour
{
    class Baker : Baker<PlayerInterfaceAuthoring>
    {
        public override void Bake(PlayerInterfaceAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<SelectableUnit>(entity);
            AddComponent<EntityWithInfoUI>(entity);
            AddComponent<PlayerInterface>(entity);
        }
    }
}
