using Unity.NetCode;

static class ExtensionHostUtils
{
    public static string Stringify(SpawnedGhost ghost) => $"{ghost.ghostId}:{ghost.spawnTick}";

    public static bool TryParseGhost(string value, out SpawnedGhost ghost)
    {
        ghost = default;

        string[] parts = value.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out int ghostId)
            || !uint.TryParse(parts[1], out uint spawnTick))
        {
            return false;
        }

        ghost = new SpawnedGhost(ghostId, new NetworkTick(spawnTick));
        return true;
    }
}
