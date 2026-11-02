using UnityEngine;
using System.Net.Sockets;
using System.Threading;
using OmniSharp.Extensions.LanguageServer.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System;
using Unity.Collections;
using System.Collections.Generic;
using Unity.Entities;
using System.Linq;
using System.Net;
using System.IO;

class ExtensionHost : MonoBehaviour
{
    TcpListenerManager? Listener;
    readonly List<(LanguageServer Server, RpcStream Stream)> RpcHosts = new();
    readonly List<(LanguageServer Server, TcpListenerManager Listener)> TcpHosts = new();
    readonly bool UseRpc = false;
    readonly bool UseTcp = true;

    CancellationTokenSource? CancellationTokenSource;
    EntityQuery ProcessorQ;

    static EntityManager E => ConnectionManager.ServerOrDefaultWorld.EntityManager;
    static World W => ConnectionManager.ServerOrDefaultWorld;

    void OnEnable()
    {
        CancellationTokenSource = new CancellationTokenSource();
    }

    class UnityLogger : Microsoft.Extensions.Logging.ILogger
    {
        readonly string categoryName;

        public UnityLogger(string categoryName)
        {
            this.categoryName = categoryName;
        }

        class DummyScope : IDisposable
        {
            public void Dispose()
            {

            }
        }

        IDisposable Microsoft.Extensions.Logging.ILogger.BeginScope<TState>(TState state) => new DummyScope();

        bool Microsoft.Extensions.Logging.ILogger.IsEnabled(LogLevel logLevel) => true;

        void Microsoft.Extensions.Logging.ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            switch (logLevel)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                case LogLevel.Information:
                    Debug.Log($"{DebugEx.ServerPrefix} [LSP/{categoryName}] {formatter(state, exception)}");
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning($"{DebugEx.ServerPrefix} [LSP/{categoryName}] {formatter(state, exception)}");
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    Debug.LogError($"{DebugEx.ServerPrefix} [LSP/{categoryName}] {formatter(state, exception)}");
                    break;
                case LogLevel.None:
                default:
                    break;
            }
            if (exception is not null) Debug.LogException(exception);
        }
    }

    class UnityLoggerProvider : ILoggerProvider
    {
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new UnityLogger(categoryName);

        void IDisposable.Dispose()
        {

        }
    }

    void Update()
    {
        if (UseTcp)
        {
            if (Listener is not null && Listener.ConnectedClient is null && !Listener.IsListening)
            {
                Debug.Log($"{DebugEx.ServerPrefix} [LSP] Destroying TCP listener (and will reopen)");
                Listener.Dispose();
            }

            if (Listener is null)
            {
                Debug.Log($"{DebugEx.ServerPrefix} [LSP] Creating new TCP listener");
                for (int i = 8052; i <= 65535; i++)
                {
                    try
                    {
                        Listener = TcpListenerManager.Listen(IPAddress.Parse("127.0.0.1"), i, "LSP");
                        goto ok;
                    }
                    catch (SocketException)
                    {

                    }
                }
                throw new UnreachableException();
            ok:;
            }

            if (Listener?.ConnectedClient is not null)
            {
                if (ProcessorQ == default)
                {
                    ProcessorQ = E.CreateEntityQuery(typeof(Processor));
                }

                NetworkStream stream = Listener.ConnectedClient.GetStream();
                LanguageServer server = CreateHost(stream, stream);
                server.Exit.Subscribe((v) =>
                {
                    Debug.Log($"{DebugEx.ServerPrefix} [LSP] Server exited {v}");
                    server?.Dispose();
                    Listener?.Dispose();
                });
                TcpHosts.Add((server, Listener));
                Listener = null;
            }
        }

        if (UseRpc)
        {
            foreach (RpcStream stream in RpcStreamManagerSystem.GetInstance(ConnectionManager.ServerWorld ?? throw new NullReferenceException($"ServerWorld is null")).Streams)
            {
                if (RpcHosts.Any(v => v.Stream == stream)) continue;
                if (!stream.RemoteIdentifier.Name.ToString().StartsWith("bbl_ext_lsp_")) continue;

                if (ProcessorQ == default)
                {
                    ProcessorQ = E.CreateEntityQuery(typeof(Processor));
                }

                NetcodeStreamAdapter adapter = new(stream);

                LanguageServer? server = CreateHost(adapter, adapter);
                RpcHosts.Add((server, stream));

                server.Exit.Subscribe((v) =>
                {
                    Debug.Log($"{DebugEx.ServerPrefix} [LSP] Server exited {v}");
                    server?.Dispose();
                    server = null;
                    stream.Complete();
                });
            }
        }
    }

    LanguageServer CreateHost(Stream input, Stream output)
    {
        Debug.Log($"{DebugEx.ServerPrefix} [LSP] Creating server");
        var server = LanguageServer.Create(options =>
        {
            options.WithInput(input);
            options.WithOutput(output);

            options.ConfigureLogging(x => x
                .SetMinimumLevel(LogLevel.Information)
#if UNITY_EDITOR
                .Services.AddSingleton<ILoggerProvider, UnityLoggerProvider>()
#else
                .AddLanguageProtocolLogging()
#endif
            );

            options.WithServices(x => x.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace)));

            options.OnRequest("units", (UnitsRequestParams request) => MainThreadManager.Instance.ScheduleAsync(() =>
            {
                if (!Guid.TryParse(request.Token, out Guid token)) return new();

                if (!PlayerSystemServer.FindPlayer(W.Unmanaged, token, out Player player)) return new();

                using NativeArray<Entity> entities = ProcessorQ.ToEntityArray(Allocator.Temp);
                List<object> res = new(entities.Length);
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];

                    if (E.HasComponent<UnitTeam>(entity))
                    {
                        int team = E.GetComponentData<UnitTeam>(entity).Team;
                        if (team != player.Team) continue;
                    }

                    Processor processor = E.GetComponentData<Processor>(entity);

                    string? source = DebugHost.GetFilePath(processor.SourceFile, token) ?? processor.SourceFile.ToString();

                    res.Add(new
                    {
                        id = $"{entity.Index}:{entity.Version}",
                        signal = !processor.Source.Code.IsCreated ? "off" : processor.DebugContext.IsBeingDebugged ? "debugged" : processor.Signal switch
                        {
                            LanguageCore.Runtime.Signal.None => "running",
                            LanguageCore.Runtime.Signal.UserCrash => "crashed",
                            LanguageCore.Runtime.Signal.StackOverflow => "crashed",
                            LanguageCore.Runtime.Signal.Halt => "halted",
                            LanguageCore.Runtime.Signal.UndefinedExternalFunction => "crashed",
                            LanguageCore.Runtime.Signal.PointerOutOfRange => "crashed",
                            _ => null,
                        },
                        source = source,
                    });
                }
                return res;
            }));

            options.OnStarted((server, cancellationToken) =>
            {
                Debug.Log($"{DebugEx.ServerPrefix} [LSP] Server started");
                return Task.CompletedTask;
            });
        });
        Debug.Log($"{DebugEx.ServerPrefix} [LSP] Initializing server");
        _ = server.Initialize(CancellationTokenSource?.Token ?? CancellationToken.None)
            .ContinueWith(task =>
            {
                if (task.IsCanceled)
                {
                    Debug.LogWarning($"{DebugEx.ServerPrefix} [LSP] Server initialization canceled");
                }
                else if (task.IsFaulted)
                {
                    Debug.LogError($"{DebugEx.ServerPrefix} [LSP] Server initialization failed");
                    Debug.LogException(task.Exception);
                }
                else
                {
                    Debug.Log($"{DebugEx.ServerPrefix} [LSP] Server initialized");
                }
            }, TaskScheduler.Default);

        return server;
    }

    void OnDisable() => Dispose();
    void OnDestroy() => Dispose();

    void Dispose()
    {
        CancellationTokenSource?.Cancel();
        CancellationTokenSource = null;

        if (Listener is not null)
        {
            Debug.Log($"{DebugEx.ServerPrefix} [LSP] Disposing TCP listener");
            Listener.Dispose();
            Listener = null;
        }

        foreach (var (server, listener) in TcpHosts)
        {
            Debug.Log($"{DebugEx.ServerPrefix} [LSP] Disposing server");
            server.Dispose();
            listener.Dispose();
        }
        TcpHosts.Clear();

        foreach (var (server, stream) in RpcHosts)
        {
            Debug.Log($"{DebugEx.ServerPrefix} [LSP] Disposing server");
            server.Dispose();
            stream.Complete();
        }
        RpcHosts.Clear();

        try
        {
            if (ProcessorQ != default) ProcessorQ.Dispose();
        }
        catch (NullReferenceException) { }
        ProcessorQ = default;
    }
}
