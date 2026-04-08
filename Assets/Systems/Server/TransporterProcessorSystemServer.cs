using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct TransporterProcessorSystemServer : ISystem
{
    Random _random;

    [BurstCompile]
    void ISystem.OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PrefabDatabase>();
        _random = Random.CreateFromIndex(420);
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        PrefabDatabase prefabDatabase = SystemAPI.GetSingleton<PrefabDatabase>();
        EntityCommandBuffer commandBuffer = default;

        foreach (var (processor, transporter, transform, localTransform) in
            SystemAPI.Query<RefRW<Processor>, RefRW<Transporter>, RefRO<LocalToWorld>, RefRO<LocalTransform>>())
        {
            ref MappedMemory mapped = ref processor.ValueRW.Memory.MappedMemory;

            mapped.Transporter.CurrentLoad = transporter.ValueRO.CurrentLoad;

            if (mapped.Transporter.LoadDirection != 0)
            {
                byte direction = mapped.Transporter.LoadDirection;
                mapped.Transporter.LoadDirection = 0;

                if (direction == 1) // out
                {
                    if (transporter.ValueRO.CurrentLoad <= 0) continue;

                    transporter.ValueRW.LoadProgress += Transporter.LoadSpeed * SystemAPI.Time.DeltaTime;
                    if (transporter.ValueRO.LoadProgress < 1f) continue;
                    transporter.ValueRW.LoadProgress--;

                    bool ok = false;

                    foreach (var (coreComputerTransform, coreComputerTeam) in
                        SystemAPI.Query<RefRO<LocalToWorld>, RefRO<UnitTeam>>()
                        .WithAll<CoreComputer>())
                    {
                        const int AdditionalReach = 4;
                        float distanceSq = math.distancesq(coreComputerTransform.ValueRO.Position, transform.ValueRO.Position);
                        if (distanceSq > Transporter.Reach * Transporter.Reach + AdditionalReach * AdditionalReach) continue;

                        foreach (var player in
                            SystemAPI.Query<RefRW<Player>>())
                        {
                            if (player.ValueRO.Team == coreComputerTeam.ValueRO.Team)
                            {
                                player.ValueRW.Resources++;
                                ok = true;
                                break;
                            }
                        }

                        break;
                    }

                    if (!ok)
                    {
                        foreach (var (resource, resourceTransform) in
                            SystemAPI.Query<RefRW<Resource>, RefRO<LocalToWorld>>())
                        {
                            float distanceSq = math.distancesq(resourceTransform.ValueRO.Position, transform.ValueRO.Position);
                            if (distanceSq > Transporter.Reach * Transporter.Reach) continue;
                            if (resource.ValueRO.Amount >= Resource.Capacity) continue;
                            resource.ValueRW.Amount++;
                            ok = true;
                            break;
                        }
                    }

                    if (!ok)
                    {
                        if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
                        Entity newResource = commandBuffer.Instantiate(prefabDatabase.Resource);
                        commandBuffer.SetComponent<Resource>(newResource, new()
                        {
                            Amount = 1,
                        });
                        float3 velocity = _random.NextFloat3Direction() * 0.5f;
                        velocity.y = _random.NextFloat(1f, 3f);
                        commandBuffer.SetComponent<Rigidbody>(newResource, new()
                        {
                            Velocity = velocity,
                        });
                        commandBuffer.SetComponent<LocalTransform>(newResource, LocalTransform.FromPosition(localTransform.ValueRO.TransformPoint(transporter.ValueRO.LoadPoint)));
                    }

                    transporter.ValueRW.CurrentLoad -= 1;
                }
                else if (direction == 2) // in
                {
                    if (transporter.ValueRO.CurrentLoad >= Transporter.Capacity) continue;

                    transporter.ValueRW.LoadProgress += Transporter.LoadSpeed * SystemAPI.Time.DeltaTime;
                    if (transporter.ValueRO.LoadProgress < 1f) continue;
                    transporter.ValueRW.LoadProgress--;

                    foreach (var (resource, resourceTransform, resourceEntity) in
                        SystemAPI.Query<RefRW<Resource>, RefRO<LocalToWorld>>()
                        .WithEntityAccess())
                    {
                        float distanceSq = math.distancesq(resourceTransform.ValueRO.Position, transform.ValueRO.Position);
                        if (distanceSq > Transporter.Reach * Transporter.Reach) continue;
                        int currentAmount = math.min(resource.ValueRO.Amount, 1);
                        resource.ValueRW.Amount -= currentAmount;
                        transporter.ValueRW.CurrentLoad += currentAmount;

                        if (resource.ValueRO.Amount <= 0)
                        {
                            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
                            commandBuffer.DestroyEntity(resourceEntity);
                        }
                        break;
                    }
                }
            }
        }
    }
}
