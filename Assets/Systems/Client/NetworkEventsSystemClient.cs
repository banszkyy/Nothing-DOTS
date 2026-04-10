using System;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct NetworkEventsSystemClient : ISystem
{
    void ISystem.OnUpdate(ref SystemState state)
    {
        NativeArray<NetCodeConnectionEvent>.ReadOnly v = SystemAPI.GetSingleton<NetworkStreamDriver>().ConnectionEventsForTick;
        for (int i = 0; i < v.Length; i++)
        {
            NetCodeConnectionEvent e = v[i];

            ConnectionManager.Instance.OnNetworkEventClient(e);

            ChatManager.Instance.AppendChatMessageElement(-1, (string?)(e.State switch
            {
                ConnectionState.State.Disconnected => $"Disconnected: {e.DisconnectReason}",
                ConnectionState.State.Connecting => $"Connecting ...",
                ConnectionState.State.Handshake => $"Handshaking ...",
                ConnectionState.State.Approval => $"Waiting for approval ...",
                ConnectionState.State.Connected => $"Connected",
                ConnectionState.State.Unknown or _ => e.ToFixedString().ToString(),
            }), DateTimeOffset.UtcNow);

            if (e.State == ConnectionState.State.Disconnected)
            {
                Debug.Log($"{DebugEx.ClientPrefix} Clearing system states ...");

                state.WorldUnmanaged.GetSystem<BuildingsSystemClient>().OnDisconnect();
                state.WorldUnmanaged.GetSystem<DebugLinesSystemClient>().OnDisconnect();
                state.WorldUnmanaged.GetSystem<PlayerPositionSystemClient>().OnDisconnect();
                state.WorldUnmanaged.GetSystem<PlayerSystemClient>().OnDisconnect();
                state.WorldUnmanaged.GetSystem<ProjectileSystemClient>().OnDisconnect();
                state.WorldUnmanaged.GetSystem<ProcessorSystemClient>().OnDisconnect();
                state.WorldUnmanaged.GetSystem<ResearchSystemClient>().OnDisconnect();
                state.WorldUnmanaged.GetSystem<UnitsSystemClient>().OnDisconnect();

                state.World.GetSystem<FileChunkManagerSystem>().OnDisconnect();
                state.World.GetSystem<CompilerSystemClient>().OnDisconnect();
                state.World.GetSystem<EntityInfoUISystemClient>().OnDisconnect();
                state.World.GetSystem<VisualEffectSystemClient>().OnDisconnect();
                state.World.GetSystem<WireRendererSystemClient>().OnDisconnect();
                state.World.GetSystem<DebugLabelSystemClient>().OnDisconnect();
            }
        }
    }
}
