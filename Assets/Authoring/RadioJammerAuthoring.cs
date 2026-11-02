using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Radio Jammer")]
public class RadioJammerAuthoring : MonoBehaviour
{
    [SerializeField, Min(0)] internal float EirpDbm = default;

    class Baker : Baker<RadioJammerAuthoring>
    {
        public override void Bake(RadioJammerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<RadioJammer>(entity, new()
            {
                EirpDbm = authoring.EirpDbm,
            });
        }
    }
}
