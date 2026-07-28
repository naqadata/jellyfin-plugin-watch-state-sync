using Jellyfin.Plugin.WatchStateSync.Services;
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
        serviceCollection.AddSingleton<WatchStateSyncService>();
        serviceCollection.AddHostedService(provider => provider.GetRequiredService<WatchStateSyncService>());
    }
}
