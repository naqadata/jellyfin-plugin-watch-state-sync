namespace Jellyfin.Plugin.WatchStateSync.Models;

/// <summary>
/// Maps one Jellyfin user to one Plex identity.
/// </summary>
public sealed class UserMappingConfiguration
{
    /// <summary>
    /// Gets or sets the stable Jellyfin user identifier.
    /// </summary>
    public Guid JellyfinUserId { get; set; }

    /// <summary>
    /// Gets or sets the Jellyfin username retained for administrator display.
    /// </summary>
    public string JellyfinUsername { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the stable Plex account or profile identifier.
    /// </summary>
    public string PlexUserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Plex username retained for administrator display.
    /// </summary>
    public string PlexUsername { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this mapping participates in synchronization.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
