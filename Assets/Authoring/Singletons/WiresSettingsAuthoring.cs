using System.Diagnostics.CodeAnalysis;
using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Wires Settings")]
class WiresSettingsAuthoring : MonoBehaviour
{
    [SerializeField, NotNull] Material? Material = default;
    [SerializeField, NotNull] Material? PlaceholderMaterial = default;

    class Baker : Baker<WiresSettingsAuthoring>
    {
        public override void Bake(WiresSettingsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponentObject<WiresSettings>(entity, new()
            {
                Material = authoring.Material,
                PlaceholderMaterial = authoring.PlaceholderMaterial,
            });
        }
    }
}
