using Unity.Burst;
using Unity.NetCode;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using Unity.Jobs;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct RelevancySphereISystemServer : ISystem
{
    struct ConnectionRelevancy
    {
        public int ConnectionId;
        public float3 Position;
    }

    NativeList<ConnectionRelevancy> Connections;
    EntityQuery GhostQuery;
    EntityQuery ConnectionQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        EntityQueryBuilder builder = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<GhostInstance>();
        GhostQuery = state.GetEntityQuery(builder);

        builder.Reset();
        builder.WithAll<NetworkId>();
        ConnectionQuery = state.GetEntityQuery(builder);

        Connections = new NativeList<ConnectionRelevancy>(16, Allocator.Persistent);

        state.RequireForUpdate(ConnectionQuery);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        Connections.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ref GhostRelevancy ghostRelevancy = ref SystemAPI.GetSingletonRW<GhostRelevancy>().ValueRW;
        ghostRelevancy.GhostRelevancyMode = GhostRelevancyMode.SetIsIrrelevant;

        var relevantSet = ghostRelevancy.GhostRelevancySet;
        var parallelRelevantSet = relevantSet.AsParallelWriter();
        int maxRelevantSize = GhostQuery.CalculateEntityCount() * ConnectionQuery.CalculateEntityCount();

        JobHandle clearHandle = new ClearRelevancySet()
        {
            MaxRelevantSize = maxRelevantSize,
            RelevantSet = relevantSet
        }.Schedule(state.Dependency);

        Connections.Clear();

        foreach (var player in
            SystemAPI.Query<RefRO<RealPlayer>>())
        {
            if (player.ValueRO.ConnectionState is not PlayerConnectionState.Connected and not PlayerConnectionState.Local) continue;
            Connections.Add(new ConnectionRelevancy()
            {
                ConnectionId = player.ValueRO.ConnectionId,
                Position = player.ValueRO.Position,
            });
        }

        state.Dependency = new UpdateConnectionRelevancyJob()
        {
            Connections = Connections,
            ParallelRelevantSet = parallelRelevantSet
        }.ScheduleParallel(JobHandle.CombineDependencies(state.Dependency, clearHandle));
    }

    [BurstCompile]
    [WithAll(typeof(GhostInstance), typeof(LocalTransform), typeof(PossiblyIrrelevant))]
    partial struct UpdateConnectionRelevancyJob : IJobEntity
    {
        [ReadOnly] public NativeList<ConnectionRelevancy> Connections;
        public NativeParallelHashMap<RelevantGhostForConnection, int>.ParallelWriter ParallelRelevantSet;

        public void Execute(in GhostInstance ghost, in LocalTransform transform, in PossiblyIrrelevant possiblyIrrelevant)
        {
            for (int i = 0; i < Connections.Length; i++)
            {
                if (math.distance(transform.Position, Connections[i].Position) > possiblyIrrelevant.RelevancyRadius)
                { ParallelRelevantSet.TryAdd(new RelevantGhostForConnection(Connections[i].ConnectionId, ghost.ghostId), 1); }
            }
        }

    }

    [BurstCompile]
    struct ClearRelevancySet : IJob
    {
        public int MaxRelevantSize;
        public NativeParallelHashMap<RelevantGhostForConnection, int> RelevantSet;

        public void Execute()
        {
            RelevantSet.Clear();
            if (RelevantSet.Capacity < MaxRelevantSize)
            { RelevantSet.Capacity = MaxRelevantSize; }
        }
    }
}
