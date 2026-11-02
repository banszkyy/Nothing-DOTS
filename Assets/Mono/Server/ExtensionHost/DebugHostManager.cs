using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using UnityEngine;

class DebugHostManager : MonoBehaviour
{
    TcpListenerManager? Listener;
    readonly List<(DebugHost Host, TcpListenerManager Listener)> TcpHosts = new();
    readonly List<(DebugHost Host, RpcStream Listener)> RpcHosts = new();
    readonly bool UseRpc = false;
    readonly bool UseTcp = true;

    readonly ConcurrentQueue<(Func<Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.ResponseBody> Task, IRequestResponder RequestResponder)> PendingRequests = new();

    class UnityLogger : Logger
    {
        public override void Trace(string? value) => UnityEngine.Debug.Log($"{DebugEx.ServerPrefix} [DAP] {value}");
        public override void Debug(string? value) => UnityEngine.Debug.Log($"{DebugEx.ServerPrefix} [DAP] {value}");
        public override void Info(string? value) => UnityEngine.Debug.Log($"{DebugEx.ServerPrefix} [DAP] {value}");
        public override void Warn(string? value) => UnityEngine.Debug.LogWarning($"{DebugEx.ServerPrefix} [DAP] {value}");
        public override void Error(string? value) => UnityEngine.Debug.LogError($"{DebugEx.ServerPrefix} [DAP] {value}");
        public override void WriteLine(string? value) => UnityEngine.Debug.Log($"{DebugEx.ServerPrefix} [DAP] {value}");

        public override void Dispose() { }
    }

    public void ScheduleRequest<TArgs, TResponse>(Func<TArgs, TResponse> work, IRequestResponder<TArgs> requestResponder)
        where TArgs : class, new()
        where TResponse : Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.ResponseBody
    {
        PendingRequests.Enqueue(new(() => work(requestResponder.Arguments), requestResponder));
    }

    void Update()
    {
        while (PendingRequests.TryDequeue(out var entry))
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

        bool didDisposeSome = false;
        for (int i = 0; i < TcpHosts.Count; i++)
        {
            (DebugHost host, TcpListenerManager listener) = TcpHosts[i];

            if (!host.Protocol.IsRunning || listener.ConnectedClient is null)
            {
                Debug.Log($"{DebugEx.ServerPrefix} [DAP] Disposing dead session");
                host.Dispose();
                listener.Dispose();
                TcpHosts.RemoveAt(i--);
                didDisposeSome = true;
                continue;
            }

            host.Update();
        }

        if (UseTcp)
        {
            if (Listener?.ConnectedClient is not null && Listener.ConnectedClient.Connected)
            {
                Debug.Log($"{DebugEx.ServerPrefix} [DAP] Starting new session");
                NetworkStream stream = Listener.ConnectedClient.GetStream();
                DebugHost host = new(this, stream, stream, new UnityLogger());
                TcpHosts.Add((host, Listener));
                host.Run();

                Listener = null;
            }
            else if (Listener is not null && !Listener.IsListening)
            {
                Debug.Log($"{DebugEx.ServerPrefix} [DAP] Destroying TCP listener");
                Listener?.Dispose();
                Listener = null;
            }
            else if (didDisposeSome)
            {
                Debug.Log($"{DebugEx.ServerPrefix} [DAP] Destroying TCP listener (and will trying to reopen it on a better port)");
                Listener?.Dispose();
                Listener = null;
            }

            if (Listener is null)
            {
                Debug.Log($"{DebugEx.ServerPrefix} [DAP] Creating new TCP listener");
                for (int i = 8053; i <= 65535; i++)
                {
                    try
                    {
                        Listener = TcpListenerManager.Listen(IPAddress.Parse("127.0.0.1"), i, "DAP");
                        goto ok;
                    }
                    catch (SocketException)
                    {

                    }
                }
                throw new UnreachableException();
            ok:;
            }
        }
        else if (Listener is not null)
        {
            Debug.Log($"{DebugEx.ServerPrefix} [DAP] Destroying TCP listener");
            Listener?.Dispose();
            Listener = null;
        }

        for (int i = 0; i < RpcHosts.Count; i++)
        {
            (DebugHost host, RpcStream stream) = RpcHosts[i];

            if (!host.Protocol.IsRunning)
            {
                Debug.Log($"{DebugEx.ServerPrefix} [DAP] Disposing dead session");
                host.Dispose();
                stream.Complete();
                RpcHosts.RemoveAt(i--);
                continue;
            }

            host.Update();
        }

        if (UseRpc)
        {
            foreach (var stream in RpcStreamManagerSystem.GetInstance(ConnectionManager.ServerWorld ?? throw new NullReferenceException($"ServerWorld is null")).Streams)
            {
                if (RpcHosts.Any(v => v.Listener == stream)) continue;
                if (!stream.RemoteIdentifier.Name.ToString().StartsWith("bbl_ext_dap_")) continue;

                Debug.Log($"{DebugEx.ServerPrefix} [DAP] Starting new session");
                NetcodeStreamAdapter streamAdapter = new(stream);
                DebugHost host = new(this, streamAdapter, streamAdapter, new UnityLogger());
                RpcHosts.Add((host, stream));
                host.Run();
            }
        }
    }

    void OnDisable() => Dispose();
    void OnDestroy() => Dispose();

    void Dispose()
    {
        foreach ((_, IRequestResponder requestResponder) in PendingRequests)
        {
            requestResponder.SetError(new ProtocolException("Request canceled"));
        }
        PendingRequests.Clear();

        Debug.Log($"{DebugEx.ServerPrefix} [DAP] Disposing TCP listener");
        Listener?.Dispose();
        Listener = null;

        foreach ((DebugHost host, TcpListenerManager listener) in TcpHosts)
        {
            Debug.Log($"{DebugEx.ServerPrefix} [DAP] Disposing session");
            host.Dispose();
            listener.Dispose();
        }
        TcpHosts.Clear();

        foreach ((DebugHost host, RpcStream stream) in RpcHosts)
        {
            Debug.Log($"{DebugEx.ServerPrefix} [DAP] Disposing session");
            host.Dispose();
            stream.Complete();
        }
        RpcHosts.Clear();
    }
}
