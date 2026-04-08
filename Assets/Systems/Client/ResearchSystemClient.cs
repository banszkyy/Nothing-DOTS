using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct ResearchSystemClient : ISystem
{
    public NativeList<FixedString64Bytes> AvaliableResearches;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006")] class _k { }
    public static readonly SharedStatic<float> LastSynced = SharedStatic<float>.GetOrCreate<BuildingsSystemClient, _k>();

    void ISystem.OnCreate(ref SystemState state)
    {
        AvaliableResearches = new NativeList<FixedString64Bytes>(Allocator.Persistent);
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;

        foreach (var (command, entity) in
            SystemAPI.Query<RefRO<ResearchesResponseRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);

            bool alreadyAdded = false;
            for (int i = 0; i < AvaliableResearches.Length; i++)
            {
                if (AvaliableResearches[i] != command.ValueRO.Name) continue;
                alreadyAdded = true;
                break;
            }

            if (!alreadyAdded)
            {
                AvaliableResearches.Add(command.ValueRO.Name);
                LastSynced.Data = MonoTime.Now;
            }
        }

        foreach (var (command, entity) in
            SystemAPI.Query<RefRO<ResearchDoneRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);

            NetcodeUtils.CreateRPC<ResearchesRequestRpc>(commandBuffer, state.WorldUnmanaged);

            for (int i = 0; i < AvaliableResearches.Length; i++)
            {
                if (AvaliableResearches[i] != command.ValueRO.Name) continue;
                AvaliableResearches.RemoveAt(i);
                LastSynced.Data = MonoTime.Now;
                break;
            }
        }
    }

    public static ref ResearchSystemClient GetInstance(in WorldUnmanaged world) => ref world.GetSystem<ResearchSystemClient>();

    public static void Refresh(in WorldUnmanaged world)
    {
        Debug.Log($"{DebugEx.ClientPrefix} Request avaliable researches");

        GetInstance(world).AvaliableResearches.Clear();
        NetcodeUtils.CreateRPC<ResearchesRequestRpc>(world);
    }

    public void OnDisconnect()
    {
        Debug.Log($"{DebugEx.ClientPrefix} Clearing avaliable researches");

        AvaliableResearches.Clear();
    }
}
