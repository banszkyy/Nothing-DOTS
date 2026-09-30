using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Virtual Player")]
class VirtualPlayerAuthoring : MonoBehaviour
{
    class Baker : Baker<VirtualPlayerAuthoring>
    {
        public override void Bake(VirtualPlayerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Player>(entity, new()
            {
                Team = Player.UnassignedTeam,
            });
            AddComponent<VirtualPlayer>(entity, new());
            AddBuffer<BufferedAcquiredResearch>(entity);
        }
    }
}
