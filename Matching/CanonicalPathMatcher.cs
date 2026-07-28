namespace Jellyfin.Plugin.WatchStateSync.Matching;

/// <summary>
/// Normalizes and compares paths exposed by Jellyfin and Plex.
/// </summary>
public static class CanonicalPathMatcher
{
    /// <summary>
    /// Normalizes a server-reported media path without changing Linux case semantics.
    /// </summary>
    /// <param name="path">Server-reported path.</param>
    /// <returns>A slash-normalized path.</returns>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = path.Trim().Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        if (normalized.Length > 1)
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized;
    }

    /// <summary>
    /// Applies an optional source-root to destination-root mapping.
    /// </summary>
    /// <param name="path">Source path.</param>
    /// <param name="sourceRoot">Source root.</param>
    /// <param name="destinationRoot">Destination root.</param>
    /// <returns>The normalized mapped path.</returns>
    public static string ApplyRootMapping(string path, string sourceRoot, string destinationRoot)
    {
        var normalizedPath = Normalize(path);
        var normalizedSource = Normalize(sourceRoot);
        var normalizedDestination = Normalize(destinationRoot);

        if (string.Equals(normalizedPath, normalizedSource, StringComparison.Ordinal))
        {
            return normalizedDestination;
        }

        var sourcePrefix = normalizedSource + "/";
        if (!normalizedPath.StartsWith(sourcePrefix, StringComparison.Ordinal))
        {
            return normalizedPath;
        }

        return normalizedDestination + normalizedPath[normalizedSource.Length..];
    }

    /// <summary>
    /// Determines whether Jellyfin and Plex paths refer to the same configured path.
    /// </summary>
    /// <param name="jellyfinPath">Jellyfin path.</param>
    /// <param name="plexPath">Plex path.</param>
    /// <param name="jellyfinRoot">Optional Jellyfin media root.</param>
    /// <param name="plexRoot">Optional Plex media root.</param>
    /// <returns><see langword="true"/> when the paths match exactly after normalization and root mapping.</returns>
    public static bool IsMatch(
        string jellyfinPath,
        string plexPath,
        string? jellyfinRoot = null,
        string? plexRoot = null)
    {
        var normalizedJellyfin = Normalize(jellyfinPath);
        var normalizedPlex = Normalize(plexPath);

        if (!string.IsNullOrWhiteSpace(jellyfinRoot) && !string.IsNullOrWhiteSpace(plexRoot))
        {
            normalizedJellyfin = ApplyRootMapping(normalizedJellyfin, jellyfinRoot, plexRoot);
        }

        return string.Equals(normalizedJellyfin, normalizedPlex, StringComparison.Ordinal);
    }
}
