using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct DebugLinesSystemClient : ISystem
{
    public const float Lifetime = 0.5f;

    NativeArray<Entity>.ReadOnly Batches;

    [BurstCompile]
    void ISystem.OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DebugLinesSettings>();
    }

    void ISystem.OnUpdate(ref SystemState state)
    {
        if (!Batches.IsCreated) Batches = CreateBatches(state.EntityManager);

        EntityCommandBuffer commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        NativeArray<Segments.Segment> batches = new(Batches.Length, Allocator.Temp);

        for (int i = 0; i < Batches.Length; i++)
        {
            batches[i] = Segments.Core.GetSegment(state.EntityManager, Batches[i]);
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<DebugLineRpc>>()
            .WithEntityAccess())
        {
            NetcodeEndPoint ep = new(request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO, request.ValueRO.SourceConnection);
            commandBuffer.DestroyEntity(entity);

            foreach (var (player, lines) in
                SystemAPI.Query<RefRO<Player>, DynamicBuffer<BufferedLine>>())
            {
                if (player.ValueRO.ConnectionId != ep.ConnectionId.Value) continue;
                lines.Add(new BufferedLine()
                {
                    Position = command.ValueRO.Position,
                    Color = command.ValueRO.Color,
                    DieAt = (float)SystemAPI.Time.ElapsedTime + Lifetime,
                });
            }
        }

        foreach (var lines in
            SystemAPI.Query<DynamicBuffer<BufferedLine>>())
        {
            for (int i = 0; i < batches.Length; i++)
            {
                batches[i].Buffer.Clear();
            }

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].DieAt <= SystemAPI.Time.ElapsedTime) lines.RemoveAtSwapBack(i--);
                else batches[lines[i].Color - 1].Buffer.Add(lines[i].Position);
            }

            for (int i = 0; i < batches.Length; i++)
            {
                Segments.Core.SetSegmentChanged(state.EntityManager, Batches[i]);
            }
        }

        batches.Dispose();
    }

    NativeArray<Entity>.ReadOnly CreateBatches(EntityManager entityManager)
    {
        DebugLinesSettings settings = SystemAPI.ManagedAPI.GetSingleton<DebugLinesSettings>();
        NativeArray<Entity> _batches = new(settings.Materials.Length, Allocator.Persistent);
        for (int i = 0; i < settings.Materials.Length; i++)
        {
            Segments.Core.Create(entityManager, out Entity v, settings.Materials[i]);
            _batches[i] = v;
        }
        return _batches.AsReadOnly();
    }

    public void OnDisconnect()
    {
        Debug.Log($"{DebugEx.ClientPrefix} Clearing debug lines");

        NativeArray<Segments.Segment> batches = new(Batches.Length, Allocator.Temp);

        for (int i = 0; i < Batches.Length; i++)
        {
            batches[i] = Segments.Core.GetSegment(ConnectionManager.ClientOrDefaultWorld.EntityManager, Batches[i]);
        }

        for (int i = 0; i < batches.Length; i++)
        {
            batches[i].Buffer.Clear();
        }

        for (int i = 0; i < batches.Length; i++)
        {
            Segments.Core.SetSegmentChanged(ConnectionManager.ClientOrDefaultWorld.EntityManager, Batches[i]);
        }

        batches.Dispose();
    }
}
