using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchStateSync.Services;

/// <summary>
/// Owns the future baseline and completed-view synchronization loops.
/// </summary>
public sealed class WatchStateSyncService : BackgroundService
{
    private readonly ILogger<WatchStateSyncService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchStateSyncService"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public WatchStateSyncService(ILogger<WatchStateSyncService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Watch State Sync service initialized; synchronization is not implemented yet");

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
    }
}
