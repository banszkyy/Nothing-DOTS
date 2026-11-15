using UnityEngine;
using System.Net.Sockets;
using System.Net;
using System.Threading.Tasks;
using StreamJsonRpc;
using System;
using Newtonsoft.Json;

class ExtensionAuthenticator : MonoBehaviour
{
    TcpListenerManager? Listener;
    JsonRpc? Server;

    public class AuthenticateResponseParams
    {
        [JsonProperty("success")] public required bool Success { get; set; }
        [JsonProperty("token")] public required string? Token { get; set; }
    }

    void Update()
    {
        if (Listener is not null && !Listener.IsListening)
        {
            Debug.Log($"{DebugEx.ClientPrefix} [ExtClient] Disposing old TCP listener");
            Listener?.Dispose();
            Listener = null;
        }

        if (Listener is null)
        {
            Listener = new TcpListenerManager(IPAddress.Parse("127.0.0.1"), 8051);
            Listener.OnClientConnected += OnClientConnected;
            Debug.Log($"{DebugEx.ClientPrefix} [ExtClient] Listening on {Listener.EndPoint}");
        }
    }

    void OnClientConnected(TcpClient client)
    {
        Debug.Log($"{DebugEx.ClientPrefix} [ExtClient] Creating RPC server");

        NetworkStream stream = client.GetStream();
        Server = new JsonRpc(stream);
        Server.AddLocalRpcMethod("authenticate", new Func<Task<AuthenticateResponseParams>>(() => MainThreadManager.Instance.ScheduleAsync(static () =>
        {
            Debug.Log($"{DebugEx.ClientPrefix} [ExtClient] Extension authenticated");
            return new AuthenticateResponseParams()
            {
                Success = true,
                Token = PlayerSystemClient.GetInstance(ConnectionManager.ClientOrDefaultWorld.Unmanaged).PlayerGuid.ToString(),
            };
        })));
        Server.StartListening();
    }

    void OnDisable() => Dispose();
    void OnDestroy() => Dispose();

    void Dispose()
    {
        if (Server is not null)
        {
            Debug.Log($"{DebugEx.ClientPrefix} [ExtClient] Disposing RPC server");
            Server.Dispose();
            Server = null;
        }

        if (Listener is not null)
        {
            Debug.Log($"{DebugEx.ClientPrefix} [ExtClient] Disposing TCP listener");
            Listener.Dispose();
            Listener = null;
        }
    }
}
