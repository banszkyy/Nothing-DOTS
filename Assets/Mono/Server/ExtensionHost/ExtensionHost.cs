using System.Net.Sockets;
using System;
using System.Collections.Generic;
using System.Net;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Unity.NetCode;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;
using Microsoft.VisualStudio.Threading;
using Unity.Entities;
using Unity.Collections;

class ExtServer : Singleton<ExtServer>
{
    TcpListenerManager? Listener;
    public readonly List<ExtensionClient> ConnectedExtensions = new();
    readonly ConcurrentQueue<(Func<ResponseBody> Task, IRequestResponder RequestResponder)> PendingRequests = new();

    public class UnityLogger : Logger
    {
        public static UnityLogger Instance = new();

        public override void Error(string? value)
        {
            UnityEngine.Debug.LogError(value);
        }

        public override void Warn(string? value)
        {
            UnityEngine.Debug.LogWarning(value);
        }

        public override void WriteLine(string? value)
        {

        }

        public override void Dispose()
        {

        }
    }

    public class ExtensionClient : IDisposable
    {
        public TcpClient Tcp { get; }
        public JsonRpc Rpc { get; }
        public string? Token { get; set; }
        public DebugSessionMultiplexer DebugSessionMultiplexer { get; }

        public ExtensionClient(TcpClient tcp, JsonRpc rpc)
        {
            Tcp = tcp;
            Rpc = rpc;
            DebugSessionMultiplexer = new DebugSessionMultiplexer(rpc);
        }

        public void Dispose()
        {
            DebugSessionMultiplexer.Dispose();
            Rpc.Dispose();
            Tcp.Dispose();
        }
    }

    public void ScheduleRequest<TArgs, TResponse>(Func<TArgs, TResponse> work, IRequestResponder<TArgs> requestResponder)
        where TArgs : class, new()
        where TResponse : ResponseBody
        => PendingRequests.Enqueue(new(() => work(requestResponder.Arguments), requestResponder));

    void Update()
    {
        while (PendingRequests.TryDequeue(out (Func<ResponseBody> Task, IRequestResponder RequestResponder) entry))
        {
            try
            {
                entry.RequestResponder.SetResponse(entry.Task());
            }
            catch (ProtocolException ex)
            {
                entry.RequestResponder.SetError(ex);
            }
            catch (Exception ex)
            {
                entry.RequestResponder.SetError(new ProtocolException($"Unhandled {ex.GetType().Name}", ex));
                Debug.LogException(ex);
            }
        }

        if (Listener is not null && !Listener.IsListening)
        {
            Debug.Log($"{DebugEx.ServerPrefix} [ExtServer] Destroying TCP listener (and will reopen)");
            Listener.Dispose();
            Listener = null;
        }

        if (Listener is null)
        {
            for (int i = 8052; i <= 65535; i++)
            {
                try
                {
                    Listener = new TcpListenerManager(IPAddress.Parse("127.0.0.1"), i);
                    Listener.OnClientConnected += OnClientConnected;
                    Debug.Log($"{DebugEx.ServerPrefix} [ExtServer] Listening on {Listener.EndPoint}");
                    goto ok;
                }
                catch (SocketException)
                {

                }
            }
            throw new UnreachableException();
        ok:;
        }

        for (int i = 0; i < ConnectedExtensions.Count; i++)
        {
            ExtensionClient v = ConnectedExtensions[i];

            if (v.Tcp is null || !v.Tcp.Connected)
            {
                Debug.Log($"{DebugEx.ServerPrefix} [ExtServer] Removing dead extension connection");
                v.Dispose();
                ConnectedExtensions.RemoveAt(i--);
                continue;
            }

            v.DebugSessionMultiplexer.Update();
        }
    }

    void OnClientConnected(TcpClient client)
    {
        Debug.Log($"{DebugEx.ServerPrefix} [ExtServer] Creating extension host");

        NetworkStream stream = client.GetStream();
        JsonRpc rpc = new(stream)
        {
            SynchronizationContext = new NonConcurrentSynchronizationContext(sticky: false)
        };
        ExtensionClient newClient = new(client, rpc);
        rpc.AddLocalRpcMethod("authenticate", (string token) =>
        {
            Debug.Log($"{DebugEx.ServerPrefix} [ExtServer] Received token \"{token}\" from {client.Client.RemoteEndPoint}");
            newClient.Token = token;
            return true;
        });
        rpc.AddLocalRpcMethod("units", (string token) => MainThreadManager.Instance.ScheduleAsync(() =>
        {
            List<object> res = new();

            EntityManager e = ConnectionManager.ServerOrDefaultWorld.EntityManager;

            using EntityQuery playersQ = e.CreateEntityQuery(typeof(Player), typeof(RealPlayer));
            using NativeArray<Entity> players = playersQ.ToEntityArray(Allocator.Temp);

            int team = -1;
            int connectionId = -1;
            foreach (Entity playerE in players)
            {
                Player player = e.GetComponentData<Player>(playerE);
                if (!string.Equals(player.Guid.ToString(), token, StringComparison.InvariantCultureIgnoreCase)) continue;
                team = player.Team;
                connectionId = e.GetComponentData<RealPlayer>(playerE).ConnectionId;
                break;
            }

            if (team == -1) return res;

            using EntityQuery unitsQ = e.CreateEntityQuery(typeof(UnitTeam), typeof(Processor), typeof(GhostInstance));
            using NativeArray<Entity> entities = unitsQ.ToEntityArray(Allocator.Temp);

            foreach (Entity entity in entities)
            {
                GhostInstance ghost = e.GetComponentData<GhostInstance>(entity);
                Processor processor = e.GetComponentData<Processor>(entity);

                res.Add(new
                {
                    name = ProcessorDebugSystemServer.GetEntityName(e, entity, ghost),
                    id = $"{ghost.ghostId}:{ghost.spawnTick}",
                    signal = processor.Source.Code.IsCreated ? processor.Signal switch
                    {
                        LanguageCore.Runtime.Signal.Halt => "halted",
                        LanguageCore.Runtime.Signal.None => processor.DebugContext.IsBeingDebugged ? "debugged" : "running",
                        _ => "crashed",
                    } : "off",
                    source = processor.SourceFile == default ? null : processor.SourceFile.ToUri().ToString(),
                });
            }

            return res;
        }));

        rpc.StartListening();

        ConnectedExtensions.Add(newClient);
    }

    void OnDisable() => Dispose();
    void OnDestroy() => Dispose();

    void Dispose()
    {
        if (Listener is not null)
        {
            Debug.Log($"{DebugEx.ServerPrefix} [ExtServer] Disposing TCP listener");
            Listener.Dispose();
            Listener = null;
        }

        foreach (ExtensionClient v in ConnectedExtensions)
        {
            Debug.Log($"{DebugEx.ServerPrefix} [ExtServer] Disposing server");
            v.Dispose();
        }
        ConnectedExtensions.Clear();
    }

    public System.Threading.Tasks.Task StartDebuggingUnit(SpawnedGhost ghost, string name, ExtensionClient ext)
    {
        string sessionId = $"{ghost.ghostId}:{ghost.spawnTick}";
        Debug.Log($"{DebugEx.ServerPrefix} [ExtServer] Starting debug session on ghost {ghost.ghostId}:{ghost.spawnTick}");
        ext.DebugSessionMultiplexer.AddSession(sessionId, (to, from) => new DebugHost(this, to.Reader.AsStream(), from.Writer.AsStream(), UnityLogger.Instance));
        return ext.DebugSessionMultiplexer.StartSession(sessionId, name, JObject.FromObject(new
        {
            token = ext.Token,
            ghost = $"{ghost.ghostId}:{ghost.spawnTick}",
        }));
    }
}
