using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
using System;
using Unity.NetCode;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct CombatTurretProcessorSystem : ISystem
{
    [BurstCompile]
    void ISystem.OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ProjectileDatabase>();
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        DynamicBuffer<BufferedProjectile> projectiles = SystemAPI.GetSingletonBuffer<BufferedProjectile>(true);
        EntityCommandBuffer commandBuffer = default;
        Unity.Mathematics.Random random;
        unsafe
        {
            float v = (float)SystemAPI.Time.ElapsedTime;
            random = Unity.Mathematics.Random.CreateFromIndex(*(uint*)&v);
        }

        foreach (var (processor, turret, entity) in
            SystemAPI.Query<RefRW<Processor>, RefRW<CombatTurret>>()
            .WithEntityAccess())
        {
            ref MappedMemory mapped = ref processor.ValueRW.Memory.MappedMemory;

            RefRW<LocalTransform> turretTransform = SystemAPI.GetComponentRW<LocalTransform>(turret.ValueRO.Turret);
            RefRW<LocalTransform> cannonTransform = SystemAPI.GetComponentRW<LocalTransform>(turret.ValueRO.Cannon);

            turretTransform.ValueRO.Rotation.ToEuler(out float3 turretEuler);
            cannonTransform.ValueRO.Rotation.ToEuler(out float3 cannonEuler);

            mapped.CombatTurret.TurretCurrentRotation = turretEuler.y;
            mapped.CombatTurret.TurretCurrentAngle = cannonEuler.x;

            float targetRotation = mapped.CombatTurret.TurretTargetRotation;
            float targetAngle = Math.Clamp(mapped.CombatTurret.TurretTargetAngle, turret.ValueRO.MinAngle, turret.ValueRO.MaxAngle);

            if (float.IsFinite(mapped.CombatTurret.TurretTargetRotation) && turretEuler.y != targetRotation)
            {
                float y = Utils.MoveTowardsAngle(turretEuler.y, targetRotation, turret.ValueRO.TurretRotationSpeed * SystemAPI.Time.DeltaTime);
                turretTransform.ValueRW.Rotation = quaternion.EulerXYZ(0, y, 0);
            }

            if (float.IsFinite(mapped.CombatTurret.TurretTargetAngle) && cannonEuler.x != targetAngle)
            {
                float x = Utils.MoveTowardsAngle(cannonEuler.x, targetAngle, turret.ValueRO.CannonRotationSpeed * SystemAPI.Time.DeltaTime);
                cannonTransform.ValueRW.Rotation = quaternion.EulerXYZ(x, 0, 0);
            }

            if (turret.ValueRO.MagazineReloadProgress < turret.ValueRO.MagazineReload || turret.ValueRW.CurrentMagazineSize == 0)
            {
                turret.ValueRW.MagazineReloadProgress += SystemAPI.Time.DeltaTime;
                if (turret.ValueRO.MagazineReloadProgress >= turret.ValueRO.MagazineReload)
                {
                    turret.ValueRW.CurrentMagazineSize = turret.ValueRO.MagazineSize;
                }
            }

            if (turret.ValueRO.BulletReloadProgress < turret.ValueRO.BulletReload)
            {
                turret.ValueRW.BulletReloadProgress += SystemAPI.Time.DeltaTime;
            }

            if (mapped.CombatTurret.InputShoot != 0)
            {
                int projectileIndex = turret.ValueRO.Projectile;
                if (projectileIndex == -1) continue;

                if (turret.ValueRO.CurrentMagazineSize <= 0 || turret.ValueRO.BulletReloadProgress < turret.ValueRO.BulletReload)
                { continue; }

                turret.ValueRW.CurrentMagazineSize--;
                turret.ValueRW.BulletReloadProgress = 0f;

                if (turret.ValueRW.CurrentMagazineSize <= 0)
                {
                    turret.ValueRW.MagazineReloadProgress = 0f;
                }

                mapped.CombatTurret.InputShoot = 0;

                RefRO<LocalToWorld> shootPosition = SystemAPI.GetComponentRO<LocalToWorld>(turret.ValueRO.ShootPosition);

                float3 velocity = math.normalize((random.NextFloat3Direction() * turret.ValueRO.Spread) + shootPosition.ValueRO.Forward) * projectiles[projectileIndex].Speed;

                if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

                Entity instance = commandBuffer.Instantiate(projectiles[turret.ValueRO.Projectile].Prefab);
                commandBuffer.SetComponent(instance, LocalTransform.FromPosition(SystemAPI.GetComponent<LocalToWorld>(turret.ValueRO.ShootPosition).Position));
                commandBuffer.SetComponent<Projectile>(instance, new()
                {
                    Velocity = velocity,
                    Damage = projectiles[projectileIndex].Damage,
                    MetalImpactEffect = projectiles[projectileIndex].MetalImpactEffect,
                    DustImpactEffect = projectiles[projectileIndex].DustImpactEffect,
                });

                if (turret.ValueRO.ShootEffect != -1)
                {
                    NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new ShootRpc()
                    {
                        Position = SystemAPI.GetComponent<LocalToWorld>(turret.ValueRO.ShootPosition).Position,
                        Velocity = velocity,
                        ProjectileIndex = projectileIndex,
                        VisualEffectIndex = turret.ValueRO.ShootEffect,
                        Source = SystemAPI.GetComponentRO<GhostInstance>(entity).ValueRO,
                    });
                }
            }
        }
    }
}
