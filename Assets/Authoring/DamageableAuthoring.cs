using Unity.Entities;
using UnityEngine;
using UnityEngine.VFX;

[AddComponentMenu("Authoring/Damageable")]
public class DamageableAuthoring : MonoBehaviour
{
    [SerializeField] float MaxHealth = default;
    [SerializeField] VisualEffectAsset? DestroyEffect = default;

    class Baker : Baker<DamageableAuthoring>
    {
        public override void Bake(DamageableAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Damageable>(entity, new()
            {
                MaxHealth = authoring.MaxHealth,
                Health = authoring.MaxHealth,
                DestroyEffect = FindAnyObjectByType<VisualEffectDatabaseAuthoring>().Find(authoring.DestroyEffect),
            });
            AddBuffer<BufferedDamage>(entity);
        }
    }
}
