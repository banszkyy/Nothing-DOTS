using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

[AddComponentMenu("Authoring/Combat Turret")]
public class CombatTurretAuthoring : MonoBehaviour
{
    [SerializeField] Transform? Turret = default;
    [SerializeField] Transform? Cannon = default;
    [SerializeField] ProjectilePrefab? Projectile = default;
    [SerializeField] Transform? ShootPosition = default;
    [SerializeField] VisualEffectAsset? ShootEffect = default;

    [SerializeField, SaintsField.MinMaxSlider(-90f, 90f)] Vector2 AngleConstraint = new(-90f, 90f);

    [SerializeField] float TurretRotationSpeed = default;
    [SerializeField] float CannonRotationSpeed = default;

    [SerializeField, Range(0f, 0.99f)] float Spread = default;
    [SerializeField, Min(0f)] float BulletReload = default;
    [SerializeField, Min(0f)] float MagazineReload = default;
    [SerializeField, Min(1)] int MagazineSize = default;

    class Baker : Baker<CombatTurretAuthoring>
    {
        public override void Bake(CombatTurretAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<CombatTurret>(entity, new()
            {
                MinAngle = authoring.AngleConstraint.x * Mathf.Deg2Rad,
                MaxAngle = authoring.AngleConstraint.y * Mathf.Deg2Rad,
                TurretRotationSpeed = authoring.TurretRotationSpeed,
                CannonRotationSpeed = authoring.CannonRotationSpeed,
                Turret = authoring.Turret != null ? GetEntity(authoring.Turret, TransformUsageFlags.Dynamic) : Entity.Null,
                Cannon = authoring.Cannon != null ? GetEntity(authoring.Cannon, TransformUsageFlags.Dynamic) : Entity.Null,
                Projectile = FindAnyObjectByType<ProjectileDatabaseAuthoring>().Find(authoring.Projectile),
                ShootPosition = authoring.ShootPosition != null ? GetEntity(authoring.ShootPosition, TransformUsageFlags.Dynamic) : Entity.Null,
                ShootEffect = FindAnyObjectByType<VisualEffectDatabaseAuthoring>().Find(authoring.ShootEffect),
                Spread = authoring.Spread,
                BulletReload = authoring.BulletReload,
                MagazineReload = authoring.MagazineReload,
                MagazineSize = authoring.MagazineSize,
            });
        }
    }

    void OnDrawGizmosSelected()
    {
        Unity.Mathematics.Random random = Unity.Mathematics.Random.CreateFromIndex(42);
        if (ShootPosition != null)
        {
            Gizmos.color = Color.red;
            for (int i = 0; i < 50; i++)
            {
                float3 direction = math.normalize((random.NextFloat3Direction() * Spread) + (float3)ShootPosition.forward);
                Gizmos.DrawRay(ShootPosition.position, direction);
            }
        }
    }
}
