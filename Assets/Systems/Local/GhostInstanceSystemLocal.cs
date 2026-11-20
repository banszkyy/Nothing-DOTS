using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
partial struct GhostInstanceSystemLocal : ISystem
{
    int IdCounter;

    [BurstCompile]
    void ISystem.OnUpdate(ref SystemState state)
    {
        foreach (RefRW<GhostInstance> ghost in SystemAPI.Query<RefRW<GhostInstance>>())
        {
            if (!ghost.ValueRO.IsEquals(default)) continue;
            ghost.ValueRW.ghostId = ++IdCounter;
            ghost.ValueRW.spawnTick = new NetworkTick((uint)Time.frameCount);
        }
    }
}
