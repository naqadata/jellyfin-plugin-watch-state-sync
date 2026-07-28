using System.Globalization;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.WatchStateSync.Configuration;
using Jellyfin.Plugin.WatchStateSync.Matching;
using Jellyfin.Plugin.WatchStateSync.Migration;
using Jellyfin.Plugin.WatchStateSync.Models;
using Jellyfin.Plugin.WatchStateSync.Plex;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchStateSync.Services;

/// <summary>
/// Performs opt-in, timestamp-only completed-view synchronization between Plex and Jellyfin.
/// </summary>
public sealed class WatchStateSyncService : BackgroundService
{
    private const int MinimumPollIntervalSeconds = 30;
    private const int DisabledConfigurationCheckSeconds = 15;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILibraryManager _libraryManager;
    private readonly PlexClient _plexClient;
    private readonly ILogger<WatchStateSyncService> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchStateSyncService"/> class.
    /// </summary>
    public WatchStateSyncService(
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILibraryManager libraryManager,
        PlexClient plexClient,
        ILogger<WatchStateSyncService> logger)
    {
        _userManager = userManager;
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
        _plexClient = plexClient;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Watch State Sync initialized; live sync is opt-in and timestamp-only");
        while (!stoppingToken.IsCancellationRequested)
        {
            PluginConfiguration? configuration = Plugin.Instance?.Configuration;
            int intervalSeconds = configuration?.EnableLiveSync == true
                ? Math.Max(MinimumPollIntervalSeconds, configuration.PollIntervalSeconds)
                : DisabledConfigurationCheckSeconds;
            if (configuration?.EnableLiveSync == true)
            {
                try
                {
                    await SynchronizeAsync(configuration, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Watch State Sync live polling failed");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task SynchronizeAsync(PluginConfiguration configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.PlexServerUrl)
            || string.IsNullOrWhiteSpace(configuration.PlexAdminToken))
        {
            _logger.LogWarning("Watch State Sync is enabled but Plex server URL or administrator token is missing");
            return;
        }

        if (!await _syncLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            foreach (UserMappingConfiguration mapping in configuration.UserMappings.Where(i => i.Enabled))
            {
                await SynchronizeMappingAsync(configuration, mapping, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task SynchronizeMappingAsync(
        PluginConfiguration configuration,
        UserMappingConfiguration mapping,
        CancellationToken cancellationToken)
    {
        User? user = mapping.JellyfinUserId == Guid.Empty ? null : _userManager.GetUserById(mapping.JellyfinUserId);
        if (user is null)
        {
            _logger.LogWarning("Skipping live sync mapping because Jellyfin user {JellyfinUserId} was not found", mapping.JellyfinUserId);
            return;
        }

        string plexToken = await _plexClient.GetUserTokenAsync(
            configuration.PlexServerUrl,
            configuration.PlexAdminToken,
            mapping.PlexUserId,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PlexWatchStateItem> plexItems = await _plexClient.GetWatchStateItemsAsync(
            configuration.PlexServerUrl,
            plexToken,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<JellyfinWatchStateItem> jellyfinItems = GetJellyfinItems(user);
        BaselineMigrationPlan plan = BaselineMigrationPlanner.Build(plexItems, jellyfinItems);
        int plexToJellyfin = 0;
        int jellyfinToPlex = 0;

        foreach (BaselinePlannedItem item in plan.Items.Where(i => i.MatchStatus == BaselineMatchStatus.Matched))
        {
            LiveSyncDirection direction = LiveSyncPlanner.ChooseDirection(item.PlexLastViewedAt, item.JellyfinLastPlayedDate);
            if (direction == LiveSyncDirection.None)
            {
                continue;
            }

            if (direction == LiveSyncDirection.PlexToJellyfin)
            {
                BaseItem? jellyfinItem = item.JellyfinItemId.HasValue ? _libraryManager.GetItemById(item.JellyfinItemId.Value) : null;
                if (jellyfinItem is null || !item.PlexLastViewedAt.HasValue)
                {
                    continue;
                }

                UserItemData userData = _userDataManager.GetUserData(user, jellyfinItem)
                    ?? new UserItemData { Key = jellyfinItem.Id.ToString("N", CultureInfo.InvariantCulture) };
                userData.Played = true;
                userData.PlayCount = Math.Max(1, userData.PlayCount);
                userData.LastPlayedDate = item.PlexLastViewedAt.Value.UtcDateTime;
                _userDataManager.SaveUserData(user, jellyfinItem, userData, UserDataSaveReason.Import, cancellationToken);
                plexToJellyfin++;
            }
            else if (item.PlexRatingKey is not null)
            {
                await _plexClient.MarkPlayedAsync(
                    configuration.PlexServerUrl,
                    plexToken,
                    item.PlexRatingKey,
                    cancellationToken).ConfigureAwait(false);
                jellyfinToPlex++;
            }
        }

        if (plexToJellyfin > 0 || jellyfinToPlex > 0)
        {
            _logger.LogInformation(
                "Live sync for Jellyfin user {JellyfinUsername} and Plex user {PlexUsername}: {PlexToJellyfin} Plex-to-Jellyfin, {JellyfinToPlex} Jellyfin-to-Plex completed views",
                user.Username,
                mapping.PlexUsername,
                plexToJellyfin,
                jellyfinToPlex);
        }
    }

    private IReadOnlyList<JellyfinWatchStateItem> GetJellyfinItems(User user)
    {
        return _libraryManager
            .GetItemList(new InternalItemsQuery(user) { Recursive = true })
            .Where(i => i is Movie or Episode)
            .Where(i => !i.IsFolder && i.IsVisible(user))
            .Where(i => !string.IsNullOrWhiteSpace(i.Path))
            .Select(i =>
            {
                UserItemData userData = _userDataManager.GetUserData(user, i)
                    ?? new UserItemData { Key = i.Id.ToString("N", CultureInfo.InvariantCulture) };
                return new JellyfinWatchStateItem(
                    i.Id,
                    i.Name,
                    i.Path,
                    i is Movie ? BaselineMediaType.Movie : BaselineMediaType.Episode,
                    i is Episode episode ? episode.SeriesName : null,
                    userData.Played,
                    userData.LastPlayedDate.HasValue ? new DateTimeOffset(userData.LastPlayedDate.Value.ToUniversalTime()) : null);
            })
            .ToArray();
    }
}
