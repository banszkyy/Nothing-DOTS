using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial struct UnitsSystemClient : ISystem
{
    public NativeList<BufferedUnit> Units;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006")] class _k { }
    public static readonly SharedStatic<float> LastSynced = SharedStatic<float>.GetOrCreate<BuildingsSystemClient, _k>();

    void ISystem.OnCreate(ref SystemState state)
    {
        Units = new(Allocator.Persistent);
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;

        foreach (var (command, entity) in
            SystemAPI.Query<RefRO<UnitsResponseRpc>>()
            .WithAll<ReceiveRpcCommandRequest>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);

            DynamicBuffer<BufferedUnit> units = SystemAPI.GetBuffer<BufferedUnit>(SystemAPI.GetSingletonEntity<UnitDatabase>());

            bool alreadyAdded = false;
            for (int i = 0; i < Units.Length; i++)
            {
                if (Units[i].Name != command.ValueRO.Name) continue;
                alreadyAdded = true;
                break;
            }

            if (!alreadyAdded)
            {
                for (int i = 0; i < units.Length; i++)
                {
                    if (command.ValueRO.Name != units[i].Name) continue;
                    Units.Add(units[i]);
                    LastSynced.Data = MonoTime.Now;
                    break;
                }
            }
        }
    }

    public static ref UnitsSystemClient GetInstance(in WorldUnmanaged world) => ref world.GetSystem<UnitsSystemClient>();

    public static void Refresh(in WorldUnmanaged world)
    {
        Debug.Log($"{DebugEx.ClientPrefix} Request avaliable units");

        GetInstance(world).Units.Clear();
        NetcodeUtils.CreateRPC<UnitsRequestRpc>(world);
    }

    public void OnDisconnect()
    {
        Debug.Log($"{DebugEx.ClientPrefix} Clearing units");

        Units.Clear();
    }
}
