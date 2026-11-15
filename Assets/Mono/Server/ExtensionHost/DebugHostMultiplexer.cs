using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

public sealed class SessionEnvelope
{
    [JsonProperty("sessionId")] public string SessionId { get; set; } = "";
    [JsonProperty("message")] public JToken? Message { get; set; }
}

public sealed class SessionRef
{
    [JsonProperty("sessionId")] public string SessionId { get; set; } = "";
}

public sealed class SessionStart
{
    [JsonProperty("sessionId")] public string SessionId { get; set; } = "";
    [JsonProperty("name")] public string? Name { get; set; }
    [JsonProperty("configuration")] public JObject? Configuration { get; set; }
}

sealed class DebugSessionMultiplexer : IDisposable
{
    public const string MessageMethod = "dap/message";
    public const string CloseMethod = "dap/close";
    public const string ClosedMethod = "dap/closed";
    public const string StartMethod = "dap/start";

    readonly ConcurrentDictionary<string, Session> Sessions = new();
    readonly JsonRpc Rpc;

    public DebugSessionMultiplexer(JsonRpc rpc)
    {
        rpc.AddLocalRpcMethod(MessageMethod, OnMessage);
        rpc.AddLocalRpcMethod(CloseMethod, OnClose);
        Rpc = rpc;
    }

    public Session AddSession(string sessionId, Func<Pipe, Pipe, DebugHost> configureAdapter)
    {
        Session session = new(sessionId, configureAdapter, SendToClient, OnSessionEnded);
        if (!Sessions.TryAdd(sessionId, session))
        {
            throw new InvalidOperationException($"Session {sessionId} already exists.");
        }
        Debug.Log($"{DebugEx.AnyPrefix} [DebugSessionMultiplexer] Session {sessionId} added");
        session.Start();
        return session;
    }

    public async Task StartSession(string sessionId, string? name = null, JObject? configuration = null)
    {
        await Rpc.NotifyAsync(StartMethod, new SessionStart { SessionId = sessionId, Name = name, Configuration = configuration });
        Debug.Log($"{DebugEx.AnyPrefix} [DebugSessionMultiplexer] Session {sessionId} started");
    }

    public void Update()
    {
        foreach (Session session in Sessions.Values)
        {
            session.Update();
        }
    }

    void OnMessage(string sessionId, JToken message)
    {
        if (message is null) return;

        if (Sessions.TryGetValue(sessionId, out Session? session))
        {
            session.Post(message);
        }
        else if (message["command"]?.Value<string>() == "initialize")
        {
            Debug.Log($"{DebugEx.AnyPrefix} [DebugSessionMultiplexer] Session {sessionId} not registered, registering");
            session = AddSession(sessionId, (to, from) => new DebugHost(ExtServer.Instance, to.Reader.AsStream(), from.Writer.AsStream(), ExtServer.UnityLogger.Instance));
            session.Post(message);
        }
        else
        {
            Debug.Log($"{DebugEx.AnyPrefix} [DebugSessionMultiplexer] Session {sessionId} not registered, closing\n{message}");
            Rpc.NotifyAsync(ClosedMethod, new SessionRef { SessionId = sessionId });
        }
    }

    void OnClose(string sessionId)
    {
        if (Sessions.TryRemove(sessionId, out Session? session))
        {
            Debug.Log($"{DebugEx.AnyPrefix} [DebugSessionMultiplexer] Session {sessionId} removed");
            session.CloseFromPeer();
        }
    }

    Task SendToClient(string id, JToken message)
    {
        return Rpc.NotifyWithParameterObjectAsync(MessageMethod, new SessionEnvelope { SessionId = id, Message = message });
    }

    async Task OnSessionEnded(Session s, bool notifyClient)
    {
        Sessions.TryRemove(s.Id, out _);
        Debug.Log($"{DebugEx.AnyPrefix} [DebugSessionMultiplexer] Session {s.Id} removed");
        if (notifyClient)
        {
            await TryNotifyAsync(ClosedMethod, new SessionRef { SessionId = s.Id });
        }
    }

    async Task TryNotifyAsync(string method, object args)
    {
        try
        {
            await Rpc.NotifyWithParameterObjectAsync(method, args);
        }
        catch (Exception ex) when (ex is ConnectionLostException or ObjectDisposedException)
        {

        }
    }

    public void Dispose()
    {
        foreach (Session s in Sessions.Values) s.Dispose();
        Sessions.Clear();
    }

    public sealed class Session : IDisposable
    {
        static readonly byte[] HeaderEnd = "\r\n\r\n"u8.ToArray();

        readonly DebugHost _adapter;
        readonly Func<string, JToken, Task> _send;
        readonly Func<Session, bool, Task> _ended;
        readonly Pipe _toAdapter = new();
        readonly Pipe _fromAdapter = new();
        readonly Channel<byte[]> _inbound = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
        volatile bool _closedByPeer;

        public string Id { get; }

        public Session(string id, Func<Pipe, Pipe, DebugHost> configureAdapter, Func<string, JToken, Task> send, Func<Session, bool, Task> ended)
        {
            Id = id;
            _adapter = configureAdapter(_toAdapter, _fromAdapter);
            _send = send;
            _ended = ended;
        }

        public void Start()
        {
            _adapter.Protocol.Run();

            _ = PumpInboundAsync();
            _ = PumpOutboundAsync();
        }

        public void Update()
        {
            _adapter.Update();
        }

        public void Post(JToken message)
        {
            byte[] body = Encoding.UTF8.GetBytes(message.ToString(Formatting.None));
            byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            byte[] frame = new byte[header.Length + body.Length];
            header.CopyTo(frame, 0);
            body.CopyTo(frame, header.Length);
            _inbound.Writer.TryWrite(frame);
        }

        public void CloseFromPeer()
        {
            _closedByPeer = true;
            _inbound.Writer.TryComplete();
            _adapter.Dispose();
        }

        async Task PumpInboundAsync()
        {
            PipeWriter writer = _toAdapter.Writer;
            try
            {
                await foreach (byte[] frame in _inbound.Reader.ReadAllAsync())
                {
                    FlushResult r = await writer.WriteAsync(frame);
                    if (r.IsCompleted) break;
                }
            }
            catch { }
            finally { await writer.CompleteAsync(); }
        }

        async Task PumpOutboundAsync()
        {
            PipeReader reader = _fromAdapter.Reader;
            try
            {
                while (true)
                {
                    ReadResult result = await reader.ReadAsync();
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    while (TryReadFrame(ref buffer, out ReadOnlySequence<byte> body))
                    {
                        await _send(Id, JToken.Parse(Encoding.UTF8.GetString(body.ToArray())));
                    }

                    reader.AdvanceTo(buffer.Start, buffer.End);
                    if (result.IsCompleted) break;
                }
            }
            catch { }
            finally
            {
                await reader.CompleteAsync();
                _inbound.Writer.TryComplete();
                _adapter.Dispose();
                await _ended(this, !_closedByPeer);
            }
        }

        static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> body)
        {
            body = default;
            SequenceReader<byte> reader = new(buffer);

            if (!reader.TryReadTo(out ReadOnlySequence<byte> header, HeaderEnd, advancePastDelimiter: true))
            {
                return false;
            }

            int length = ParseContentLength(Encoding.ASCII.GetString(header.ToArray()));
            if (reader.Remaining < length) return false;

            body = buffer.Slice(reader.Position, length);
            buffer = buffer.Slice(buffer.GetPosition(length, reader.Position));
            return true;
        }

        static int ParseContentLength(string header)
        {
            foreach (string line in header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                int i = line.IndexOf(':');
                if (i > 0 && line.AsSpan(0, i).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(line.AsSpan(i + 1).Trim(), out int n))
                {
                    return n;
                }
            }
            throw new InvalidDataException("DAP frame without Content-Length.");
        }

        public void Dispose()
        {
            _closedByPeer = true;
            _inbound.Writer.TryComplete();
            _adapter.Dispose();
        }
    }
}
