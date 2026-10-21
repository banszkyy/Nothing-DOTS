using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LanguageCore;

class NetcodeSourceProviderOffline : ISourceProviderSync
{
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

    public SourceProviderResultSync TryLoad(string requestedFile, Uri? currentFile)
    {
        Uri? lastUri = null;

        foreach (Uri uri in GetQuery(requestedFile, currentFile))
        {
            lastUri = uri;

            if (!FileId.FromUri(uri, out FileId fileId))
            {
                Debug.LogError($"[{nameof(CompilerSystemServer)}] Uri \"{uri}\" isn't a netcode uri");
                return SourceProviderResultSync.NextHandler();
            }

            Debug.Log($"{DebugEx.AnyPrefix} Try load netcode file \"{fileId.Name}\" ...");

            if (FileChunkManagerSystem.GetInstance(ConnectionManager.ClientOrDefaultWorld).TryGetRemoteFile(fileId, out RemoteFile remoteFile))
            {
                return SourceProviderResultSync.Success(uri, Encoding.UTF8.GetString(remoteFile.Data.Data));
            }
        }

        if (lastUri is not null)
        {
            return SourceProviderResultSync.NotFound(lastUri);
        }
        else
        {
            return SourceProviderResultSync.NextHandler();
        }
    }
}
