namespace Jellyfin.Plugin.WatchStateSync.Migration;

/// <summary>
/// The direction of a timestamp-backed completed-view synchronization.
/// </summary>
public enum LiveSyncDirection
{
    /// <summary>No timestamp-backed update is required.</summary>
    None,

    /// <summary>Apply Plex's newer completion to Jellyfin.</summary>
    PlexToJellyfin,

    /// <summary>Apply Jellyfin's newer completion to Plex.</summary>
    JellyfinToPlex
}

/// <summary>
/// Chooses a live-sync direction using only completed-play timestamps.
/// </summary>
public static class LiveSyncPlanner
{
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Selects the platform with the strictly newer completed-play timestamp.
    /// A timestamp on only one side is a newly observed completed play and is propagated. When neither
    /// side has one, no action is taken, which excludes manual watched/unwatched toggles.
    /// </summary>
    public static LiveSyncDirection ChooseDirection(
        DateTimeOffset? plexLastViewedAt,
        DateTimeOffset? jellyfinLastPlayedDate)
    {
        if (plexLastViewedAt.HasValue && !jellyfinLastPlayedDate.HasValue)
        {
            return LiveSyncDirection.PlexToJellyfin;
        }

        if (!plexLastViewedAt.HasValue && jellyfinLastPlayedDate.HasValue)
        {
            return LiveSyncDirection.JellyfinToPlex;
        }

        if (!plexLastViewedAt.HasValue)
        {
            return LiveSyncDirection.None;
        }

        DateTimeOffset plexTimestamp = plexLastViewedAt.Value;
        DateTimeOffset jellyfinTimestamp = jellyfinLastPlayedDate!.Value;
        if (plexTimestamp > jellyfinTimestamp.Add(TimestampTolerance))
        {
            return LiveSyncDirection.PlexToJellyfin;
        }

        if (jellyfinTimestamp > plexTimestamp.Add(TimestampTolerance))
        {
            return LiveSyncDirection.JellyfinToPlex;
        }

        return LiveSyncDirection.None;
    }
}
