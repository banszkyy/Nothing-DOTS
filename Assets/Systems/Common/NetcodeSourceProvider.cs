using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LanguageCore;
using Unity.Entities;
using UnityEngine;

class NetcodeSourceProvider : ISourceProviderAsync, ISourceQueryProvider
{
    readonly CompiledSourceServer Source;
    readonly List<ProgressRecord<(int, int)>> Progresses;
    readonly bool EnableLogging;

    public NetcodeSourceProvider(CompiledSourceServer source, List<ProgressRecord<(int, int)>> progresses, bool enableLogging)
    {
        Source = source;
        Progresses = progresses;
        EnableLogging = enableLogging;
    }

    public IEnumerable<Uri> GetQuery(string requestedFile, Uri? currentFile)
    {
        if (!requestedFile.EndsWith(LanguageConstants.LanguageExtension, StringComparison.Ordinal))
        {
            requestedFile += LanguageConstants.LanguageExtension;
        }

        if (requestedFile.StartsWith("/~")) requestedFile = requestedFile[1..];
        if (requestedFile.StartsWith('~'))
        {
            requestedFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "." + requestedFile[1..]);
        }

        if (Uri.TryCreate(currentFile, requestedFile, out Uri? result))
        {
            yield return result;
        }
    }

    public SourceProviderResultAsync TryLoad(string requestedFile, Uri? currentFile, CancellationToken cancellationToken)
    {
        Uri? lastUri = null;

        foreach (Uri uri in GetQuery(requestedFile, currentFile))
        {
            lastUri = uri;

            if (!FileId.FromUri(uri, out FileId fileId))
            {
                Debug.LogError($"{DebugEx.AnyPrefix} [{nameof(NetcodeSourceProvider)}] Uri \"{uri}\" isn't a netcode uri");
                return SourceProviderResultAsync.NextHandler();
            }

            if (EnableLogging) Debug.Log($"{DebugEx.AnyPrefix} [{nameof(NetcodeSourceProvider)}] Try load netcode file \"{fileId.Name}\" ...");

            if (fileId.Source.IsServer)
            {
                FileData? localFile = FileChunkManagerSystem.GetFileData(fileId.Name.ToString());
                if (!localFile.HasValue)
                { return SourceProviderResultAsync.NextHandler(); }

                AwaitableCompletionSource<Stream> task = new();
                task.SetResult(new MemoryStream(localFile.Value.Data));
                if (EnableLogging) Debug.Log($"{DebugEx.AnyPrefix} [{nameof(NetcodeSourceProvider)}] Successfully loaded \"{uri}\" from local files");
                return SourceProviderResultAsync.Success(uri, task.Awaitable);
            }

            // if (FileChunkManagerSystem.GetInstance(ConnectionManager.ClientOrDefaultWorld).TryGetRemoteFile(fileId, out RemoteFile remoteFile))
            // {
            //     AwaitableCompletionSource<Stream> task = new();
            //     task.SetResult(new MemoryStream(remoteFile.File.Data));
            //     return  SourceProviderResultAsync.Success(uri, task.Awaitable);
            // }

            FileStatus status = FileChunkManagerSystem.GetInstance(World.DefaultGameObjectInjectionWorld).GetRequestStatus(fileId);

            if (status == FileStatus.NotFound)
            {
                Debug.LogError($"{DebugEx.AnyPrefix} [{nameof(NetcodeSourceProvider)}] Remote file \"{uri}\" not found");
                return SourceProviderResultAsync.NotFound(uri);
            }

            ProgressRecord<(int Current, int Total)> progress = new(v =>
            {
                float total = Progresses.Sum(v => v.Progress.Item2 == 0 ? 0f : (float)v.Progress.Item1 / v.Progress.Item2);
                Source.Progress = total / Progresses.Count;
                Source.Diagnostics.Clear();
                Source.StatusChanged = true;
            });

            Source.SubFiles[fileId] = progress;
            Progresses.Add(progress);

            if (EnableLogging) Debug.Log($"{DebugEx.AnyPrefix} [{nameof(NetcodeSourceProvider)}] Source needs file \"{fileId}\" ...");

            {
                AwaitableCompletionSource<Stream> result = new();
                Awaitable<RemoteFile> task = FileChunkManagerSystem.GetInstance(World.DefaultGameObjectInjectionWorld).RequestFileAsync(fileId, progress, cancellationToken);
                task.GetAwaiter().OnCompleted(() =>
                {
                    progress.Report((1, 1));
                    try
                    {
                        RemoteFile remoteFile = task.GetAwaiter().GetResult();
                        result.SetResult(new MemoryStream(remoteFile.Data.Data));
                        if (EnableLogging) Debug.Log($"{DebugEx.AnyPrefix} [{nameof(NetcodeSourceProvider)}] Source \"{remoteFile.Source}\" downloaded ...");
                    }
                    catch (Exception ex)
                    {
                        result.SetException(ex);
                    }
                });

                if (EnableLogging) Debug.Log($"{DebugEx.AnyPrefix} [{nameof(NetcodeSourceProvider)}]Successfully loaded {uri}");
                return SourceProviderResultAsync.Success(uri, result.Awaitable);
            }

        }

        if (lastUri is not null)
        {
            return SourceProviderResultAsync.NotFound(lastUri);
        }
        else
        {
            return SourceProviderResultAsync.NextHandler();
        }
    }
}
