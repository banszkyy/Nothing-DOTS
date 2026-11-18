using Unity.Entities;

[UpdateInGroup(typeof(InitializationSystemGroup))]
partial struct TimeSystem : ISystem
{
    void ISystem.OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonRW(out RefRW<PausableTime> t))
        {
            Entity e = state.EntityManager.CreateEntity(ComponentType.ReadWrite<PausableTime>());
            state.EntityManager.SetComponentData(e, new PausableTime()
            {
                ElapsedTime = SystemAPI.Time.ElapsedTime,
                DeltaTime = SystemAPI.Time.DeltaTime,
            });
            return;
        }

        if (t.ValueRW.IsPaused)
        {
            t.ValueRW.DeltaTime = 0f;
        }
        else
        {
            t.ValueRW.ElapsedTime += SystemAPI.Time.DeltaTime;
            t.ValueRW.DeltaTime = SystemAPI.Time.DeltaTime;
        }
    }
}
