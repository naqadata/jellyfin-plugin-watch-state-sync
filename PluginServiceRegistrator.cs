using Jellyfin.Plugin.WatchStateSync.Services;
using Jellyfin.Plugin.WatchStateSync.Plex;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.WatchStateSync;

/// <summary>
/// Registers plugin services with Jellyfin.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<PlexClient>();
        serviceCollection.AddSingleton<BaselineMigrationService>();
        serviceCollection.AddSingleton<WatchStateSyncService>();
        serviceCollection.AddHostedService(provider => provider.GetRequiredService<WatchStateSyncService>());
    }
}
