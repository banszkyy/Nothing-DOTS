using Unity.Entities;
using Unity.NetCode;

//partial struct DisposeRpcSystem : ISystem
//{
//    void ISystem.OnUpdate(ref SystemState state)
//    {
//        EntityCommandBuffer commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
//
//        foreach (var (command, entity) in
//            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
//            .WithEntityAccess())
//        {
//            if (command.ValueRO.IsConsumed) commandBuffer.DestroyEntity(entity);
//        }
//    }
//}
