using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchStateSync.Services;

/// <summary>
/// Reserves the hosted-service lifecycle for future opt-in completed-view synchronization.
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
        _logger.LogInformation("Watch State Sync initialized; baseline migration is manual-only and live sync is disabled");

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
    }
}
