using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

class TcpListenerManager : IDisposable
{
    public EndPoint EndPoint => Listener.LocalEndpoint;
    public bool IsListening { get; private set; }
    readonly TcpListener Listener;
    public readonly ConcurrentBag<TcpClient> Clients;
    readonly CancellationTokenSource CancellationTokenSource;
    public event Action<TcpClient>? OnClientConnected;

    public TcpListenerManager(IPAddress address, int port)
    {
        Clients = new ConcurrentBag<TcpClient>();
        Listener = new TcpListener(address, port);
        CancellationTokenSource = new CancellationTokenSource();

        IsListening = true;
        Listener.Start();
        Listener.AcceptTcpClientAsync().ContinueWith(HandleClient, CancellationTokenSource.Token);
    }

    void HandleClient(System.Threading.Tasks.Task<TcpClient> task)
    {
        if (task.IsCanceled)
        {
            IsListening = false;
            return;
        }

        if (task.IsFaulted) Debug.LogException(task.Exception);

        if (task.IsCompletedSuccessfully)
        {
            Clients.Add(task.Result);
            OnClientConnected?.Invoke(task.Result);
            IsListening = true;
            Listener.AcceptTcpClientAsync().ContinueWith(HandleClient, CancellationTokenSource.Token);
        }
        else
        {
            IsListening = false;
        }
    }

    public void Dispose()
    {
        CancellationTokenSource.Cancel();
        Listener.Stop();
        foreach (TcpClient item in Clients)
        {
            Debug.Log($"{DebugEx.AnyPrefix} [TCP] Disposing client");
            item.Dispose();
        }
        Clients.Clear();
    }
}
