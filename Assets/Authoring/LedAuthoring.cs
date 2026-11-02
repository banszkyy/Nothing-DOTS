using System.Diagnostics.CodeAnalysis;
using Unity.Entities;
using UnityEngine;

public class LedAuthoring : MonoBehaviour
{
    [SerializeField, NotNull] GameObject? glowChild = default;

    class Baker : Baker<LedAuthoring>
    {
        public override void Bake(LedAuthoring a)
        {
            var e = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<LedClientState>(e);
            AddComponent<LedBlinkProp>(e);
            AddComponent<LedStatusProp>(e);
            AddComponent(e, new LedGlowLink { Glow = GetEntity(a.glowChild, TransformUsageFlags.Renderable) });
        }
    }
}
