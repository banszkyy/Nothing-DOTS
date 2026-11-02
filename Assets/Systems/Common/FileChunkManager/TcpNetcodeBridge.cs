using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public sealed class TcpNetcodeBridge : IAsyncDisposable
{
    readonly NetworkStream _networkStream;
    readonly RpcStream _rpcStream;
    readonly NetcodeStreamAdapter _adapter;
    readonly CancellationTokenSource _cts = new();
    readonly Task _pumpTask;

    public TcpNetcodeBridge(NetworkStream networkStream, RpcStream rpcStream)
    {
        _networkStream = networkStream;
        _rpcStream = rpcStream;
        _adapter = new NetcodeStreamAdapter(rpcStream);
        _pumpTask = RunBothDirectionsAsync(_cts.Token);
    }

    async Task RunBothDirectionsAsync(CancellationToken cancellationToken)
    {
        Task tcpToNetcode = _networkStream.CopyToAsync(_adapter, bufferSize: StreamChunkRpc.MaxChunkSize, cancellationToken);
        Task netcodeToTcp = _adapter.CopyToAsync(_networkStream, bufferSize: StreamChunkRpc.MaxChunkSize, cancellationToken);

        try
        {
            await Task.WhenAll(tcpToNetcode, netcodeToTcp);
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {

        }
        finally
        {
            _rpcStream.Complete();

            try { _networkStream.Close(); } catch { }

            _cts.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _rpcStream.Complete();

        _cts.Cancel();
        if (_pumpTask != null)
        {
#pragma warning disable VSTHRD003
            try { await _pumpTask; }
#pragma warning restore VSTHRD003
            catch { }
        }
        _cts.Dispose();
    }
}
