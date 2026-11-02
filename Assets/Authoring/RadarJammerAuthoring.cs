using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Radar Jammer")]
public class RadarJammerAuthoring : MonoBehaviour
{
    [SerializeField, Min(0)] internal float Radius = default;

    class Baker : Baker<RadarJammerAuthoring>
    {
        public override void Bake(RadarJammerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<RadarJammer>(entity, new()
            {
                Radius = authoring.Radius,
            });
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, Radius);
    }
}
