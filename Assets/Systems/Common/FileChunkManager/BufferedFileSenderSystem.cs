using System;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

#pragma warning disable CS0162 // Unreachable code detected

partial struct BufferedFileSenderSystem : ISystem
{
    const bool DebugLog = true;
    const int ChunkSendingLimit = 16;

    void ISystem.OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BufferedFiles>();
    }

    unsafe void ISystem.OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = default;

        DynamicBuffer<BufferedSentFileChunk> sentChunks = SystemAPI.GetSingletonBuffer<BufferedSentFileChunk>();
        DynamicBuffer<BufferedSendingFile> sendingFiles = SystemAPI.GetSingletonBuffer<BufferedSendingFile>();

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<FileHeaderRequestRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);
            NetcodeEndPoint ep = new(request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO, request.ValueRO.SourceConnection);
            if (!state.World.IsServer()) ep = NetcodeEndPoint.Server;

            FileData? localFile = FileChunkManagerSystem.GetFileData(command.ValueRO.FileName.ToString());

            if (!localFile.HasValue)
            {
                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new FileHeaderResponseRpc()
                {
                    Status = FileResponseStatus.NotFound,
                    FileName = command.ValueRO.FileName,
                    TransactionId = default,
                    TotalLength = default,
                    Version = MonoTime.Ticks,
                });

                if (DebugLog) Debug.Log($"{DebugEx.Prefix(state.WorldUnmanaged)} [{nameof(BufferedFileSenderSystem)}] File \"{command.ValueRO.FileName}\" doesn't exists");
                continue;
            }

            if (command.ValueRO.Version != 0 &&
                command.ValueRO.Version == localFile.Value.Version)
            {
                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new FileHeaderResponseRpc()
                {
                    Status = FileResponseStatus.NotChanged,
                    FileName = command.ValueRO.FileName,
                    TransactionId = default,
                    TotalLength = localFile.Value.Data.Length,
                    Version = localFile.Value.Version,
                });
                continue;
            }

            {
                int transactionId = RandomManaged.Shared.Next();
                int totalLength = localFile.Value.Data.Length;

                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new FileHeaderResponseRpc()
                {
                    Status = FileResponseStatus.OK,
                    FileName = command.ValueRO.FileName,
                    TransactionId = transactionId,
                    TotalLength = totalLength,
                    Version = localFile.Value.Version,
                });

                sendingFiles.Add(new()
                {
                    Destination = ep,
                    TransactionId = transactionId,
                    FileName = command.ValueRO.FileName,
                    AutoSendEverything = true,
                    TotalLength = totalLength,
                });
                if (DebugLog) Debug.Log($"{DebugEx.Prefix(state.WorldUnmanaged)} [{nameof(BufferedFileSenderSystem)}] Sending file header \"{command.ValueRO.FileName}\": {{ id: `{command.ValueRO.FileName.GetHashCode()}` length: `{totalLength}` }}");
            }
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<FileChunkRequestRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);
            NetcodeEndPoint ep = new(request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO, request.ValueRO.SourceConnection);
            if (!state.World.IsServer()) ep = NetcodeEndPoint.Server;

            bool found = false;

            for (int i = 0; i < sendingFiles.Length; i++)
            {
                if (sendingFiles[i].Destination != ep) continue;
                if (sendingFiles[i].TransactionId != command.ValueRO.TransactionId) continue;

                FileData? file = FileChunkManagerSystem.GetFileData(sendingFiles[i].FileName.ToString());
                int chunkSize = FileChunkManagerSystem.GetChunkSize(file!.Value.Data.Length, command.ValueRO.ChunkIndex);
                Span<byte> buffer = file!.Value.Data.AsSpan().Slice(command.ValueRO.ChunkIndex * FileChunkResponseRpc.ChunkSize, chunkSize);
                fixed (byte* bufferPtr = buffer)
                {
                    NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new FileChunkResponseRpc
                    {
                        Status = FileChunkStatus.OK,
                        TransactionId = sendingFiles[i].TransactionId,
                        ChunkIndex = command.ValueRO.ChunkIndex,
                        Data = *(FileChunk*)bufferPtr,
                    });
                }
                found = true;
                if (DebugLog) Debug.Log($"{DebugEx.Prefix(state.WorldUnmanaged)} [{nameof(BufferedFileSenderSystem)}] Sending chunk `{command.ValueRO.ChunkIndex}` of file \"{sendingFiles[i].FileName}\"");

                break;
            }

            if (!found)
            {
                NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new FileChunkResponseRpc
                {
                    Status = FileChunkStatus.InvalidFile,
                    TransactionId = default,
                    ChunkIndex = command.ValueRO.ChunkIndex,
                    Data = default,
                });

                Debug.LogWarning($"{DebugEx.Prefix(state.WorldUnmanaged)} [{nameof(BufferedFileSenderSystem)}] Chunk requested for transaction `{command.ValueRO.TransactionId}` but it doesn't exists");
            }
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<CloseFileRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);
            NetcodeEndPoint ep = new(request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO, request.ValueRO.SourceConnection);
            if (!state.World.IsServer()) ep = NetcodeEndPoint.Server;

            if (DebugLog) Debug.Log($"{DebugEx.Prefix(state.WorldUnmanaged)} [{nameof(BufferedFileSenderSystem)}] Closing file \"{command.ValueRO.FileName}\"");

            for (int i = sendingFiles.Length - 1; i >= 0; i--)
            {
                if (sendingFiles[i].Destination != ep) continue;
                if (sendingFiles[i].FileName != command.ValueRO.FileName) continue;

                for (int j = sentChunks.Length - 1; j >= 0; j--)
                {
                    if (sentChunks[j].Destination != ep) continue;
                    if (sentChunks[j].TransactionId != sendingFiles[i].TransactionId) continue;

                    sentChunks.RemoveAt(j);
                }

                sendingFiles.RemoveAt(i);
            }
        }

        foreach (var (request, command, entity) in
            SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<CloseTransactionRpc>>()
            .WithEntityAccess())
        {
            if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            commandBuffer.DestroyEntity(entity);
            NetcodeEndPoint ep = new(request.ValueRO.SourceConnection == default ? default : SystemAPI.GetComponentRO<NetworkId>(request.ValueRO.SourceConnection).ValueRO, request.ValueRO.SourceConnection);
            if (!state.World.IsServer()) ep = NetcodeEndPoint.Server;

            if (DebugLog) Debug.Log($"{DebugEx.Prefix(state.WorldUnmanaged)} [{nameof(BufferedFileSenderSystem)}] Closing transaction `{command.ValueRO.TransactionId}`");

            for (int i = sendingFiles.Length - 1; i >= 0; i--)
            {
                if (sendingFiles[i].Destination != ep) continue;
                if (sendingFiles[i].TransactionId != command.ValueRO.TransactionId) continue;

                for (int j = sentChunks.Length - 1; j >= 0; j--)
                {
                    if (sentChunks[j].Destination != ep) continue;
                    if (sentChunks[j].TransactionId != sendingFiles[i].TransactionId) continue;

                    sentChunks.RemoveAt(j);
                }

                sendingFiles.RemoveAt(i);
            }
        }

        int sent = 0;
        NativeList<bool> currentSentChunks = default;
        for (int i = 0; i < sendingFiles.Length; i++)
        {
            if (!sendingFiles[i].AutoSendEverything) continue;
            if (!currentSentChunks.IsCreated) currentSentChunks = new(Allocator.Temp);
            currentSentChunks.Resize(FileChunkManagerSystem.GetChunkLength(sendingFiles[i].TotalLength), NativeArrayOptions.ClearMemory);

            for (int j = 0; j < sentChunks.Length; j++)
            {
                if (sentChunks[j].Destination != sendingFiles[i].Destination) continue;
                if (sentChunks[j].TransactionId != sendingFiles[i].TransactionId) continue;

                currentSentChunks[sentChunks[j].ChunkIndex] = true;
            }

            for (int j = 0; j < currentSentChunks.Length; j++)
            {
                if (currentSentChunks[j]) continue;

                FileData? file = FileChunkManagerSystem.GetFileData(sendingFiles[i].FileName.ToString());
                int chunkSize = FileChunkManagerSystem.GetChunkSize(file!.Value.Data.Length, j);
                Span<byte> buffer = file!.Value.Data.AsSpan().Slice(j * FileChunkResponseRpc.ChunkSize, chunkSize);
                fixed (byte* bufferPtr = buffer)
                {
                    if (!commandBuffer.IsCreated) commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
                    NetcodeUtils.CreateRPC(commandBuffer, state.WorldUnmanaged, new FileChunkResponseRpc()
                    {
                        Status = FileChunkStatus.OK,
                        TransactionId = sendingFiles[i].TransactionId,
                        ChunkIndex = j,
                        Data = *(FileChunk*)bufferPtr,
                    });
                }

                sentChunks.Add(new()
                {
                    Destination = sendingFiles[i].Destination,
                    TransactionId = sendingFiles[i].TransactionId,
                    ChunkIndex = j,
                });

                if (DebugLog) Debug.Log($"{DebugEx.Prefix(state.WorldUnmanaged)} [{nameof(BufferedFileSenderSystem)}] Sending chunk `{j}` for file \"{sendingFiles[i].FileName}\"");

                if (++sent >= ChunkSendingLimit) break;
            }
        }
        currentSentChunks.Dispose();
    }
}
