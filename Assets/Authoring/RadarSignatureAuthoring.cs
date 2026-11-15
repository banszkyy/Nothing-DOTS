using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Radar Signature")]
class RadarSignatureAuthoring : MonoBehaviour
{
    [SerializeField, Min(0f)] internal float RadarCrossSection;

    class Baker : Baker<RadarSignatureAuthoring>
    {
        public override void Bake(RadarSignatureAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<RadarSignature>(entity, new()
            {
                RadarCrossSection = authoring.RadarCrossSection,
            });
        }
    }
}
