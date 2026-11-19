using Unity.Entities;

public struct BuilderTurret : IComponentData
{
    public Entity Turret;
    public float TurretRotationSpeed;
}
