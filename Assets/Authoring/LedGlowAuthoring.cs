using Unity.Entities;
using UnityEngine;

public class LedGlowAuthoring : MonoBehaviour
{
    class Baker : Baker<LedGlowAuthoring>
    {
        public override void Bake(LedGlowAuthoring a)
        {
            var e = GetEntity(TransformUsageFlags.Renderable);
            AddComponent<LedBlinkProp>(e);
            AddComponent<LedStatusProp>(e);
        }
    }
}
