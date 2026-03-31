#pragma warning disable CS0162 // Unreachable code detected

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

partial class FileChunkManagerSystem : SystemBase
{
    const bool EnableLogging = true;
    public static string? BasePath => Application.streamingAssetsPath;

    public readonly Dictionary<FileId, RemoteFile> RemoteFiles = new();
    public readonly List<FileRequest> Requests = new();

    public static FileChunkManagerSystem GetInstance(World world)
        => world.GetExistingSystemManaged<FileChunkManagerSystem>();

    protected override void OnCreate()
    {
        RequireForUpdate<BufferedFiles>();
    }

    protected override void OnUpdate()
    {
        if (Requests.Count == 0) return;

        EntityCommandBuffer commandBuffer = default;

        Entity databaseEntity = SystemAPI.GetSingletonEntity<BufferedFiles>();
        DynamicBuffer<BufferedReceivingFile> fileHeaders = World.EntityManager.GetBuffer<BufferedReceivingFile>(databaseEntity);
        DynamicBuffer<BufferedReceivingFileChunk> fileChunks = World.EntityManager.GetBuffer<BufferedReceivingFileChunk>(databaseEntity);

        for (int i = Requests.Count - 1; i >= 0; i--)
        {
            FileRequest request = Requests[i];

            HandleRequest(
                ref commandBuffer,
                fileHeaders,
                fileChunks,
                request,
                out bool shouldDelete,
                out int headerIndex
            );

            //Debug.Assert(shouldDelete == request.Task.Awaitable.GetAwaiter().IsCompleted);

            if (shouldDelete)
            {
                CloseFile(
                    ref commandBuffer,
                    request.File,
                    headerIndex,
                    fileHeaders,
                    fileChunks
                );
                Requests.RemoveAt(i);
            }
        }
    }

    protected override void OnDestroy()
    {
        Reset();
    }

    void Reset()
    {
        if (!SystemAPI.TryGetSingletonEntity<BufferedFiles>(out Entity databaseEntity)) return;

        DynamicBuffer<BufferedReceivingFile> fileHeaders = World.EntityManager.GetBuffer<BufferedReceivingFile>(databaseEntity);
        DynamicBuffer<BufferedReceivingFileChunk> fileChunks = World.EntityManager.GetBuffer<BufferedReceivingFileChunk>(databaseEntity);

        Debug.Log($"{DebugEx.Prefix(World.Unmanaged)} Cancelling remote file requests");
        foreach (FileRequest item in Requests)
        {
            item.Task.SetCanceled();
        }

        Debug.Log($"{DebugEx.Prefix(World.Unmanaged)} Disposing remote files");
        Requests.Clear();
        RemoteFiles.Clear();

        fileHeaders.Clear();
        fileChunks.Clear();
    }

    unsafe void HandleRequest(
        ref EntityCommandBuffer commandBuffer,
        DynamicBuffer<BufferedReceivingFile> fileHeaders,
        DynamicBuffer<BufferedReceivingFileChunk> fileChunks,
        FileRequest request,
        out bool shouldDelete,
        out int headerIndex)
    {
        shouldDelete = false;
        headerIndex = -1;

        bool requestCached = true;

        for (int i = fileHeaders.Length - 1; i >= 0; i--)
        {
            BufferedReceivingFile header = fileHeaders[i];
            if (header.FileName != request.File.Name) continue;
            if (header.Source != request.File.Source) continue;

            headerIndex = i;

            switch (header.Kind)
            {
                case FileResponseStatus.NotFound:
                {
                    Debug.LogWarning($"{DebugEx.Prefix(World.Unmanaged)} [{nameof(FileChunkManagerSystem)}] Remote file \"{request.File.ToUri()}\" not found");

                    RemoteFiles.Remove(request.File);
                    shouldDelete = true;
                    request.Task.SetException(new FileNotFoundException("Remote file not found", request.File.Name.ToString()));

                    return;
                }
                case FileResponseStatus.NotChanged:
                {
                    if (RemoteFiles.TryGetValue(request.File, out RemoteFile remoteFile))
                    {
                        if (EnableLogging) Debug.Log($"{DebugEx.Prefix(World.Unmanaged)} [{nameof(FileChunkManagerSystem)}] Remote file \"{request.File.ToUri()}\" was not changed");

                        shouldDelete = true;
                        request.Task.SetResult(remoteFile);

                        return;
                    }

                    Debug.LogWarning($"{DebugEx.Prefix(World.Unmanaged)} [{nameof(FileChunkManagerSystem)}] Remote file \"{request.File.ToUri()}\" was not changed but not found locally, requesting without cache ...");
                    requestCached = false;
                    break;
                }
                case FileResponseStatus.OK:
                {
                    if (request.CancellationToken.IsCancellationRequested)
                    {
                        Debug.LogWarning($"{DebugEx.Prefix(World.Unmanaged)} [{nameof(FileChunkManagerSystem)}] Request for remote file \"{request.File.ToUri()}\" was cancelled");

                        shouldDelete = true;
                        request.Task.SetException(new OperationCanceledException("Request was cancelled"));

                        return;
                    }

                    int totalLength = GetChunkLength(header.TotalLength);

                    FileChunk[] chunks = new FileChunk[totalLength];
                    bool[] received = new bool[totalLength];

                    for (int j = 0; j < fileChunks.Length; j++)
                    {
                        if (fileChunks[j].TransactionId != header.TransactionId) continue;
                        chunks[fileChunks[j].ChunkIndex] = fileChunks[j].Data;
                        received[fileChunks[j].ChunkIndex] = true;
                    }

                    int receivedLength = received.Count(v => v);

                    request.Progress?.Report((receivedLength, totalLength));

                    if (receivedLength < totalLength) return;

                    byte[] data = new byte[header.TotalLength];
                    for (int j = 0; j < chunks.Length; j++)
                    {
                        int chunkSize = GetChunkSize(header.TotalLength, j);
                        Span<byte> chunk = new(Unsafe.AsPointer(ref chunks[j]), chunkSize);
                        chunk.CopyTo(data.AsSpan(j * FileChunkResponseRpc.ChunkSize));
                    }

                    RemoteFile remoteFile = new(
                        header.Kind,
                        new FileData(data, header.Version),
                        new FileId(header.FileName, header.Source)
                    );

                    RemoteFiles[request.File] = remoteFile;
                    shouldDelete = true;
                    request.Task.SetResult(remoteFile);

                    return;
                }
                case FileResponseStatus.ErrorDisconnected:
                {
                    Debug.LogWarning($"{DebugEx.Prefix(World.Unmanaged)} [{nameof(FileChunkManagerSystem)}] Failed to receive remote file \"{request.File.ToUri()}\": remote disconnected");

                    RemoteFiles.Remove(request.File);
                    shouldDelete = true;
                    request.Task.SetException(new NotImplementedException("Remote disconnected"));

                    return;
                }
                case FileResponseStatus.Unknown: throw new NotImplementedException();
                default: throw new UnreachableException();
            }
        }

        if (SystemAPI.Time.ElapsedTime - request.RequestSentAt > 5d)
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

            if (request.CancellationToken.IsCancellationRequested)
            {
                Debug.LogWarning($"{DebugEx.Prefix(World.Unmanaged)} [{nameof(FileChunkManagerSystem)}] Request for remote file \"{request.File.ToUri()}\" was cancelled");

                shouldDelete = true;
                request.Task.SetException(new OperationCanceledException("Request was cancelled"));

                return;
            }

            Entity connection = request.File.Source.GetEntity(World.EntityManager);

            if (connection != Entity.Null && !SystemAPI.Exists(connection))
            {
                Debug.LogWarning($"{DebugEx.Prefix(World.Unmanaged)} [{nameof(FileChunkManagerSystem)}] Cannot send request for file \"{request.File.ToUri()}\": remote disconnected");
                shouldDelete = true;
                request.Task.SetException(new NotImplementedException("Remote disconnected"));
                return;
            }

            NetcodeUtils.CreateRPC(commandBuffer, World.Unmanaged, new FileHeaderRequestRpc()
            {
                FileName = request.File.Name,
                Version = requestCached && RemoteFiles.TryGetValue(request.File, out RemoteFile v) ? v.File.Version : 0,
            }, connection);

            request.RequestSentAt = SystemAPI.Time.ElapsedTime;
            if (EnableLogging) Debug.Log($"{DebugEx.Prefix(World.Unmanaged)} [{nameof(FileChunkManagerSystem)}] Sending request for file \"{request.File.ToUri()}\"");
        }
    }

    void CloseFile(
        ref EntityCommandBuffer commandBuffer,
        FileId fileId,
        int headerIndex,
        DynamicBuffer<BufferedReceivingFile> fileHeaders,
        DynamicBuffer<BufferedReceivingFileChunk> fileChunks)
    {
        if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

        NetcodeUtils.CreateRPC(commandBuffer, World.Unmanaged, new CloseFileRpc()
        {
            FileName = fileId.Name,
        }, fileId.Source.GetEntity(World.EntityManager));

        if (headerIndex != -1)
        {
            BufferedReceivingFile header = fileHeaders[headerIndex];
            fileHeaders.RemoveAt(headerIndex);
            for (int i = fileChunks.Length - 1; i >= 0; i--)
            {
                if (fileChunks[i].Source != header.Source) continue;
                if (fileChunks[i].TransactionId != header.TransactionId) continue;
                fileChunks.RemoveAt(i);
            }
        }
    }

    public bool TryGetRemoteFile(FileId fileId, out RemoteFile remoteFile)
    {
        if (!Requests.Any(v => v.File == fileId) &&
            RemoteFiles.TryGetValue(fileId, out RemoteFile cached))
        {
            remoteFile = cached;
            if (cached.Kind == FileResponseStatus.OK)
            {
                return true;
            }
            else if (cached.Kind == FileResponseStatus.NotFound)
            {
                return false;
            }
        }

        remoteFile = default;
        return false;
    }

    public FileStatus GetRequestStatus(FileId fileId)
    {
        if (Requests.Any(v => v.File == fileId))
        {
            return FileStatus.Receiving;
        }

        if (RemoteFiles.TryGetValue(fileId, out RemoteFile status))
        {
            return status.Kind switch
            {
                FileResponseStatus.OK => FileStatus.Received,
                FileResponseStatus.NotFound => FileStatus.NotFound,
                FileResponseStatus.NotChanged => FileStatus.Received,
                FileResponseStatus.Unknown => throw new NotImplementedException(),
                FileResponseStatus.ErrorDisconnected => FileStatus.Error,
                _ => throw new UnreachableException(),
            };
        }

        return FileStatus.NotRequested;
    }

    public Awaitable<RemoteFile> RequestFile(FileId fileId, IProgress<(int Current, int Total)>? progress, CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < Requests.Count; i++)
        {
            if (Requests[i].File != fileId) continue;
            return Requests[i].Task.Awaitable;
        }

        AwaitableCompletionSource<RemoteFile> task = new();
        Requests.Add(new FileRequest(fileId, task, progress, cancellationToken));
        return task.Awaitable;
    }

    public static FileData? GetFileData(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        if (fileName.StartsWith("/i"))
        {
            ReadOnlySpan<char> _fileName = fileName;
            _fileName = _fileName[2..];

            if (_fileName.Consume("/e/"))
            {
                if (!_fileName.ConsumeInt(out int ghostId))
                {
                    Debug.LogError($"{DebugEx.AnyPrefix} [{nameof(FileChunkManagerSystem)}] Can't get ghost id");
                    return null;
                }

                if (!_fileName.Consume('.'))
                {
                    Debug.LogError($"{DebugEx.AnyPrefix} [{nameof(FileChunkManagerSystem)}] Expected separator");
                    return null;
                }

                if (!_fileName.ConsumeUInt(out uint spawnTickValue))
                {
                    Debug.LogError($"{DebugEx.AnyPrefix} [{nameof(FileChunkManagerSystem)}] Can't get ghost spawn tick");
                    return null;
                }

                NetworkTick spawnTick = new() { SerializedData = spawnTickValue };

                GhostInstance ghostInstance = new()
                {
                    ghostId = ghostId,
                    spawnTick = spawnTick,
                };

                EntityQuery q = World.DefaultGameObjectInjectionWorld.EntityManager.CreateEntityQuery(typeof(GhostInstance));
                NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);
                q.Dispose();

                Entity entity = Entity.Null;
                foreach (Entity _entity in entities)
                {
                    GhostInstance ghost = World.DefaultGameObjectInjectionWorld.EntityManager.GetComponentData<GhostInstance>(_entity);
                    if (ghost.ghostId != ghostInstance.ghostId) continue;
                    if (ghost.spawnTick != ghostInstance.spawnTick) continue;
                    entity = _entity;
                    break;
                }
                entities.Dispose();

                if (entity == Entity.Null)
                {
                    Debug.LogError($"{DebugEx.AnyPrefix} [{nameof(FileChunkManagerSystem)}] Ghost {{ id: {ghostInstance.ghostId} spawnTick: {ghostInstance.spawnTick} }} not found");
                    return null;
                }

                if (_fileName.Consume("/m"))
                {
                    unsafe
                    {
                        Processor processor = World.DefaultGameObjectInjectionWorld.EntityManager.GetComponentData<Processor>(entity);
                        byte[] data = new Span<byte>(&processor.Memory, Processor.TotalMemorySize).ToArray();
                        return new FileData(data, MonoTime.Ticks);
                    }
                }
            }

            return null;
        }

        if (fileName[0] == '~')
        { fileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "." + fileName[1..]); }

        if (!string.IsNullOrWhiteSpace(BasePath))
        {
            if (File.Exists(Path.Combine(BasePath, "." + fileName)))
            { return FileData.FromLocal(Path.Combine(BasePath, "." + fileName)); }

            if (File.Exists(Path.Combine(BasePath, fileName)))
            { return FileData.FromLocal(Path.Combine(BasePath, fileName)); }
        }

        if (File.Exists(fileName))
        { return FileData.FromLocal(fileName); }

        Debug.LogWarning($"{DebugEx.AnyPrefix} [{nameof(FileChunkManagerSystem)}] Local file \"{fileName}\" does not exists");
        return null;
    }

    public static int GetChunkLength(int bytes)
    {
        int n = bytes / FileChunkResponseRpc.ChunkSize;
        int rem = bytes % FileChunkResponseRpc.ChunkSize;
        if (rem != 0) n++;
        return n;
    }

    public static int GetChunkSize(int totalLength, int chunkIndex)
    {
        if (chunkIndex == GetChunkLength(totalLength) - 1)
        {
            return totalLength - (GetChunkLength(totalLength) - 1) * FileChunkResponseRpc.ChunkSize;
        }
        return FileChunkResponseRpc.ChunkSize;
    }

    public void OnDisconnect()
    {
        Reset();
    }
}
