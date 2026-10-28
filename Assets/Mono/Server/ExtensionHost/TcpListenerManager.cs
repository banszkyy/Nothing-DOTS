using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

class TcpListenerManager : IDisposable
{
    public IPAddress Address { get; }
    public int Port { get; }

    readonly string _category;

    readonly TcpListener _listener;
    Thread? _listenerThread;
    TcpClient? _client;

    public TcpClient? ConnectedClient
    {
        get
        {
            if (_client is null) return null;
            if (_client.Connected) return _client;
            Debug.Log($"{DebugEx.AnyPrefix} [{_category}] Disposing TCP client {_client.Client.RemoteEndPoint}");
            _client.Dispose();
            _client = null;
            return null;
        }
    }
    public bool IsListening => _listenerThread is not null && _listenerThread.IsAlive;

    TcpListenerManager(IPAddress address, int port, string category)
    {
        Address = address;
        Port = port;
        _category = category;
        _listener = new TcpListener(Address, Port);
        _listener.Start();
    }

    public static TcpListenerManager Listen(IPAddress address, int port, string category)
    {
        TcpListenerManager res = new(address, port, category);
        res.StartThread();
        return res;
    }

    void StartThread()
    {
        _listenerThread = new Thread(new ThreadStart(() =>
        {
            try
            {
                Debug.Log($"{DebugEx.AnyPrefix} [{_category}] Accepting next TCP client on {Address}:{Port} ...");
                _client = _listener.AcceptTcpClient();
                Debug.Log($"{DebugEx.AnyPrefix} [{_category}] TCP client connected from {_client.Client.RemoteEndPoint}");
            }
            catch (SocketException socketException)
            {
                if (socketException.Message == "interrupted")
                {
                    Debug.Log($"{DebugEx.AnyPrefix} [{_category}] Listening stopped on {Address}:{Port}");
                }
                else
                {
                    Debug.LogException(socketException);
                }
            }
        }))
        {
            IsBackground = true
        };
        _listenerThread.Start();
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
        _listener.Stop();
    }
}
