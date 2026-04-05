using System.Diagnostics.CodeAnalysis;
using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Projectile Database")]
class ProjectileDatabaseAuthoring : MonoBehaviour
{
    [SerializeField, NotNull] AllPrefabs? Prefabs = default;

    public int Find(ProjectilePrefab? projectile)
    {
        if (projectile == null) return -1;
        for (int i = 0; i < Prefabs.Projectiles.Length; i++)
        {
            if (Prefabs.Projectiles[i] != projectile) continue;
            return i;
        }
        Debug.LogError($"Projectile `{projectile}` is not present in the database", projectile);
        return -1;
    }

    class Baker : Baker<ProjectileDatabaseAuthoring>
    {
        public override void Bake(ProjectileDatabaseAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<ProjectileDatabase>(entity, new());
            DynamicBuffer<BufferedProjectile> projectiles = AddBuffer<BufferedProjectile>(entity);
            foreach (ProjectilePrefab projectile in authoring.Prefabs.Projectiles)
            {
                projectiles.Add(new()
                {
                    Prefab = GetEntity(projectile.Prefab, TransformUsageFlags.Dynamic),
                    Damage = projectile.Damage,
                    Speed = projectile.Speed,
                    MetalImpactEffect = FindAnyObjectByType<VisualEffectDatabaseAuthoring>().Find(projectile.MetalImpactEffect),
                    DustImpactEffect = FindAnyObjectByType<VisualEffectDatabaseAuthoring>().Find(projectile.DustImpactEffect),
                });
            }
        }
    }
}
