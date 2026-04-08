using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct BuildingsSystemClient : ISystem
{
    public NativeList<BufferedBuilding> Buildings;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006")] class _k { }
    public static readonly SharedStatic<float> LastSynced = SharedStatic<float>.GetOrCreate<BuildingsSystemClient, _k>();

    void ISystem.OnCreate(ref SystemState state)
    {
        Buildings = new(Allocator.Persistent);
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;

        foreach (var (command, entity) in
            SystemAPI.Query<RefRO<BuildingsResponseRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);

            DynamicBuffer<BufferedBuilding> buildings = SystemAPI.GetBuffer<BufferedBuilding>(SystemAPI.GetSingletonEntity<BuildingDatabase>());

            for (int i = 0; i < Buildings.Length; i++)
            {
                if (Buildings[i].Name != command.ValueRO.Name) continue;
                goto _;
            }

            for (int i = 0; i < buildings.Length; i++)
            {
                if (command.ValueRO.Name != buildings[i].Name) continue;

                Debug.Log(string.Format($"{DebugEx.ClientPrefix} New building \"{{0}}\"", buildings[i].Name));
                Buildings.Add(buildings[i]);
                LastSynced.Data = MonoTime.Now;

                goto _;
            }

            Debug.LogWarning(string.Format($"{DebugEx.ClientPrefix} Unknown building \"{{0}}\" received", command.ValueRO.Name));

        _:;
        }
    }

    public static ref BuildingsSystemClient GetInstance(in WorldUnmanaged world) => ref world.GetSystem<BuildingsSystemClient>();

    public static void Refresh(in WorldUnmanaged world)
    {
        Debug.Log($"{DebugEx.ClientPrefix} Request avaliable buildings");

        GetInstance(world).Buildings.Clear();
        NetcodeUtils.CreateRPC<BuildingsRequestRpc>(world);
    }

    public void OnDisconnect()
    {
        Debug.Log($"{DebugEx.ClientPrefix} Clearing buildings");

        Buildings.Clear();
        LastSynced.Data = default;
    }
}
