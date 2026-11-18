using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

#pragma warning disable CS0162 // Unreachable code detected

partial struct BufferedFileReceiverSystem : ISystem
{
    const bool DebugLog = true;
    const int ChunkRequestsLimit = 1;
    const double ChunkRequestsCooldown = 1d;

    void ISystem.OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BufferedFiles>();
    }

    void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;

        DynamicBuffer<BufferedReceivingFileChunk> fileChunks = SystemAPI.GetSingletonBuffer<BufferedReceivingFileChunk>();
        DynamicBuffer<BufferedReceivingFile> receivingFiles = SystemAPI.GetSingletonBuffer<BufferedReceivingFile>();

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<FileHeaderResponseRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);
            NetcodeEndPoint ep = new(request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO, request.ValueRO.SourceConnection);
            if (!state.WorldUnmanaged.IsServer()) ep = NetcodeEndPoint.Server;

            bool added = false;
            BufferedReceivingFile fileHeader = new()
            {
                Status = command.ValueRO.Status,
                Source = ep,
                TransactionId = command.ValueRO.TransactionId,
                FileName = command.ValueRO.FileName,
                TotalLength = command.ValueRO.TotalLength,
                LastReceivedAt = SystemAPI.Time.ElapsedTime,
                Version = command.ValueRO.Version,
                RemotePath = command.ValueRO.RemotePath,
            };

            for (int i = 0; i < receivingFiles.Length; i++)
            {
                if (receivingFiles[i].Source != ep) continue;
                if (receivingFiles[i].FileName != command.ValueRO.FileName) continue;
                if (receivingFiles[i].TransactionId != command.ValueRO.TransactionId) continue;

                receivingFiles[i] = fileHeader;
                added = true;
                Debug.LogWarning($"{DebugEx.Prefix(state.WorldUnmanaged)} [T#{command.ValueRO.TransactionId}] Received file header \"{fileHeader.FileName}\" from {fileHeader.Source} (again)");

                break;
            }

            if (!added)
            {
                if (DebugLog) Debug.Log($"{DebugEx.Prefix(state.WorldUnmanaged)} [T#{command.ValueRO.TransactionId}] Received file header \"{fileHeader.FileName}\" from {fileHeader.Source}");
                receivingFiles.Add(fileHeader);
            }
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<FileChunkResponseRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);
            NetcodeEndPoint ep = new(request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO, request.ValueRO.SourceConnection);
            if (!state.WorldUnmanaged.IsServer()) ep = NetcodeEndPoint.Server;

            int fileIndex = -1;
            for (int i = 0; i < receivingFiles.Length; i++)
            {
                if (receivingFiles[i].Source != ep) continue;
                if (receivingFiles[i].TransactionId != command.ValueRO.TransactionId) continue;

                receivingFiles[i] = receivingFiles[i] with
                {
                    LastReceivedAt = SystemAPI.Time.ElapsedTime
                };
                fileIndex = i;
                if (DebugLog) Debug.Log($"{DebugEx.Prefix(state.WorldUnmanaged)} [T#{command.ValueRO.TransactionId}] {receivingFiles[i].FileName} {command.ValueRO.ChunkIndex}/{FileChunkManagerSystem.GetChunkLength(receivingFiles[i].TotalLength)}");

                break;
            }

            if (command.ValueRO.Status == FileChunkStatus.InvalidTransaction)
            {
                if (fileIndex == -1)
                {
                    Debug.LogError($"{DebugEx.Prefix(state.WorldUnmanaged)} [T#{command.ValueRO.TransactionId}] Failed to request file chunk: Invalid transaction and also the transaction doesn't exists on the receiver");
                }
                else
                {
                    Debug.LogError($"{DebugEx.Prefix(state.WorldUnmanaged)} [T#{command.ValueRO.TransactionId}] Failed to request file chunk: Invalid transaction. Closing file");
                    receivingFiles[fileIndex] = receivingFiles[fileIndex] with
                    {
                        Status = FileResponseStatus.ErrorInvalidTransaction,
                    };
                }
                continue;
            }

            if (fileIndex == -1)
            {
                Debug.LogWarning($"{DebugEx.Prefix(state.WorldUnmanaged)} [T#{command.ValueRO.TransactionId}] Unexpected file chunk, creating file placeholder ...");
                receivingFiles.Add(new BufferedReceivingFile()
                {
                    Status = FileResponseStatus.HoldOn,
                    Source = ep,
                    TransactionId = command.ValueRO.TransactionId,
                    FileName = default,
                    TotalLength = default,
                    LastReceivedAt = SystemAPI.Time.ElapsedTime,
                    Version = -1,
                    RemotePath = default,
                });
            }

            bool added = false;
            BufferedReceivingFileChunk fileChunk = new()
            {
                Source = ep,
                TransactionId = command.ValueRO.TransactionId,
                ChunkIndex = command.ValueRO.ChunkIndex,
                Data = command.ValueRO.Data,
            };

            for (int i = 0; i < fileChunks.Length; i++)
            {
                if (fileChunks[i].Source != fileChunk.Source) continue;
                if (fileChunks[i].TransactionId != fileChunk.TransactionId) continue;
                if (fileChunks[i].ChunkIndex != fileChunk.ChunkIndex) continue;

                fileChunks[i] = fileChunk;
                added = true;
                Debug.LogWarning($"{DebugEx.Prefix(state.WorldUnmanaged)} [T#{command.ValueRO.TransactionId}] Received chunk {fileChunk.ChunkIndex} (again)");
                break;
            }

            if (!added)
            {
                fileChunks.Add(fileChunk);
                if (DebugLog) Debug.Log($"{DebugEx.Prefix(state.WorldUnmanaged)} [T#{command.ValueRO.TransactionId}] Received chunk {fileChunk.ChunkIndex}");
            }
        }

        int requestedTotal = 0;
        for (int i = 0; i < receivingFiles.Length; i++)
        {
            if (SystemAPI.Time.ElapsedTime - receivingFiles[i].LastReceivedAt < ChunkRequestsCooldown) continue;
            if (receivingFiles[i].Status != FileResponseStatus.OK) continue;

            if (receivingFiles[i].Status == FileResponseStatus.HoldOn)
            {
                Entity connection = receivingFiles[i].Source.GetEntity(ref state);

                if (connection != Entity.Null && !SystemAPI.Exists(connection))
                {
                    Debug.LogError($"{DebugEx.Prefix(state.WorldUnmanaged)} [T#{receivingFiles[i].TransactionId}] Cannot request chunk for file \"{receivingFiles[i].FileName}\": remote disconnected");
                    receivingFiles[i] = receivingFiles[i] with
                    {
                        Status = FileResponseStatus.ErrorDisconnected,
                    };
                    continue;
                }

                continue;
            }

            NativeArray<bool> receivedChunks = new(FileChunkManagerSystem.GetChunkLength(receivingFiles[i].TotalLength), Allocator.Temp);

            for (int j = 0; j < fileChunks.Length; j++)
            {
                if (fileChunks[j].Source != receivingFiles[i].Source) continue;
                if (fileChunks[j].TransactionId != receivingFiles[i].TransactionId) continue;

                receivedChunks[fileChunks[j].ChunkIndex] = true;
            }

            int requested = 0;

            for (int j = 0; j < receivedChunks.Length; j++)
            {
                if (receivedChunks[j]) continue;

                Entity connection = receivingFiles[i].Source.GetEntity(ref state);

                if (connection != Entity.Null && !SystemAPI.Exists(connection))
                {
                    Debug.LogError($"{DebugEx.Prefix(state.WorldUnmanaged)} [T#{receivingFiles[i].TransactionId}] Cannot request chunk `{j}` for file \"{receivingFiles[i].FileName}\": remote disconnected");
                    receivingFiles[i] = receivingFiles[i] with
                    {
                        Status = FileResponseStatus.ErrorDisconnected,
                    };
                    continue;
                }

                if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new FileChunkRequestRpc()
                {
                    TransactionId = receivingFiles[i].TransactionId,
                    ChunkIndex = j,
                }, connection);
                Debug.LogWarning($"{DebugEx.Prefix(state.WorldUnmanaged)} [T#{receivingFiles[i].TransactionId}] Requesting chunk `{j}` for file \"{receivingFiles[i].FileName}\"");
                if (++requested >= ChunkRequestsLimit) break;
            }

            receivedChunks.Dispose();

            if (requested == 0) continue;
            requestedTotal += requested;

            receivingFiles[i] = receivingFiles[i] with
            {
                LastReceivedAt = SystemAPI.Time.ElapsedTime
            };
        }
    }
}
