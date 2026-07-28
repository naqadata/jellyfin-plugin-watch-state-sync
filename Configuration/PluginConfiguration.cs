using Jellyfin.Plugin.WatchStateSync.Models;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.WatchStateSync.Configuration;

/// <summary>
/// Stores Watch State Sync settings.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Plex server base URL.
    /// </summary>
    public string PlexServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Plex polling interval in seconds.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets a value indicating whether completed-view live sync is enabled.
    /// </summary>
    public bool EnableLiveSync { get; set; }

    /// <summary>
    /// Gets or sets configured Jellyfin-to-Plex user mappings.
    /// </summary>
    public UserMappingConfiguration[] UserMappings { get; set; } = [];
}
