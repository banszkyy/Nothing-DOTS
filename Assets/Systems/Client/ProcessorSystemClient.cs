using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
partial struct ProcessorSystemClient : ISystem
{
    public NativeList<UserUIElement> uiElements;

    [BurstCompile]
    void ISystem.OnCreate(ref SystemState state)
    {
        uiElements = new(256, Allocator.Persistent);
    }

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<UIElementUpdateRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);

            for (int i = 0; i < uiElements.Length; i++)
            {
                if (uiElements[i].Id != command.ValueRO.UIElement.Id) continue;
                uiElements[i] = command.ValueRO.UIElement with { IsDirty = true };
                goto next;
            }

            uiElements.Add(command.ValueRO.UIElement);

        next:;
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<UIElementDestroyRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);

            for (int i = 0; i < uiElements.Length; i++)
            {
                if (uiElements[i].Id != command.ValueRO.Id) continue;
                uiElements.RemoveAt(i);
                break;
            }
        }
    }

    public void OnDisconnect()
    {
        Debug.Log($"{DebugEx.ClientPrefix} Clearing user UI elements");

        uiElements.Clear();
    }
}
