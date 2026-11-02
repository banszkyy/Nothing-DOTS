using UnityEngine;
using System.Threading;
using System.Net;
using System;
using System.Threading.Tasks;

class DebugBridge : MonoBehaviour
{
    TcpListenerManager? Listener;
    RpcStream? Stream;
    TcpNetcodeBridge? Bridge;
    CancellationTokenSource? CancellationTokenSource;

    void OnEnable()
    {
        CancellationTokenSource = new CancellationTokenSource();
    }

    void Update()
    {
        if (Listener is null || (Listener.ConnectedClient is null && !Listener.IsListening))
        {
            Debug.Log($"{DebugEx.ClientPrefix} [ExtDebugBridge] Disposing old TCP listener, creating new one");
            Listener?.Dispose();
            Listener = TcpListenerManager.Listen(IPAddress.Parse("127.0.0.1"), 8053, "ExtDebugBridge");

            if (Bridge is not null)
            {
                Debug.Log($"{DebugEx.ClientPrefix} [ExtDebugBridge] Disposing bridge ...");
                _ = Bridge.DisposeAsync().AsTask().ContinueWith((task) =>
                {
                    Debug.Log($"{DebugEx.ClientPrefix} [ExtDebugBridge] Bridge disposed");
                }, TaskScheduler.Default);
                Bridge = null;
            }
        }

        if (Listener.ConnectedClient is null && Stream is not null)
        {
            Debug.Log($"{DebugEx.ClientPrefix} [ExtDebugBridge] Disposing server");
            Stream?.Complete();
            Stream = null;

            if (Bridge is not null)
            {
                Debug.Log($"{DebugEx.ClientPrefix} [ExtDebugBridge] Disposing bridge ...");
                _ = Bridge.DisposeAsync().AsTask().ContinueWith((task) =>
                {
                    Debug.Log($"{DebugEx.ClientPrefix} [ExtDebugBridge] Bridge disposed");
                }, TaskScheduler.Default);
                Bridge = null;
            }
        }

        if (Listener.ConnectedClient is not null && Stream is null)
        {
            Debug.Log($"{DebugEx.ClientPrefix} [ExtDebugBridge] Creating server");

            Stream = RpcStreamManagerSystem.GetInstance(ConnectionManager.ClientWorld ?? throw new NullReferenceException()).CreateStream(new FileId("bbl_ext_dap_meow", NetcodeEndPoint.Server));
        }

        if (Listener.ConnectedClient is not null && Stream is not null && Bridge is null)
        {
            Bridge = new TcpNetcodeBridge(Listener.ConnectedClient.GetStream(), Stream);
        }
    }

    void OnDisable() => Dispose();
    void OnDestroy() => Dispose();

    void Dispose()
    {
        CancellationTokenSource?.Cancel();
        CancellationTokenSource = null;

        if (Bridge is not null)
        {
            Debug.Log($"{DebugEx.ClientPrefix} [ExtDebugBridge] Disposing bridge ...");
            _ = Bridge.DisposeAsync().AsTask().ContinueWith((task) =>
            {
                Debug.Log($"{DebugEx.ClientPrefix} [ExtDebugBridge] Bridge disposed");
            }, TaskScheduler.Default);
            Bridge = null;
        }

        if (Stream is not null)
        {
            Debug.Log($"{DebugEx.ClientPrefix} [ExtDebugBridge] Disposing server");
            Stream.Complete();
            Stream = null;
        }

        if (Listener is not null)
        {
            Debug.Log($"{DebugEx.ClientPrefix} [ExtDebugBridge] Disposing TCP listener");
            Listener.Dispose();
            Listener = null;
        }
    }
}
