using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchStateSync.Services;

/// <summary>
/// Builds and explicitly applies Plex-authoritative baseline migrations.
/// </summary>
public sealed class BaselineMigrationService
{
    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILibraryManager _libraryManager;
    private readonly PlexClient _plexClient;
    private readonly ILogger<BaselineMigrationService> _logger;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, CachedBaselinePreview> _previews = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="BaselineMigrationService"/> class.
    /// </summary>
    public BaselineMigrationService(
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILibraryManager libraryManager,
        PlexClient plexClient,
        ILogger<BaselineMigrationService> logger)
    {
        _userManager = userManager;
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
        _plexClient = plexClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets Jellyfin users available for mapping.
    /// </summary>
    /// <returns>User options.</returns>
    public IReadOnlyList<JellyfinUserOptionDto> GetJellyfinUsers()
    {
        return _userManager.Users
            .OrderBy(i => i.Username, StringComparer.OrdinalIgnoreCase)
            .Select(i => new JellyfinUserOptionDto
            {
                Id = i.Id,
                Name = i.Username
            })
            .ToArray();
    }

    /// <summary>
    /// Gets Plex Home users available to the configured administrator token.
    /// </summary>
    public Task<IReadOnlyList<PlexUserOptionDto>> GetPlexUsersAsync(CancellationToken cancellationToken)
    {
        PluginConfiguration configuration = Plugin.Instance?.Configuration
            ?? throw new InvalidOperationException("Watch State Sync is not initialized.");
        return string.IsNullOrWhiteSpace(configuration.PlexAdminToken)
            ? Task.FromResult<IReadOnlyList<PlexUserOptionDto>>([])
            : _plexClient.GetAvailableUsersAsync(configuration.PlexServerUrl, configuration.PlexAdminToken, cancellationToken);
    }

    /// <summary>
    /// Builds a dry-run preview and retains the exact source fingerprint for a later explicit apply.
    /// </summary>
    /// <param name="request">Preview selection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dry-run report.</returns>
    public async Task<BaselineMigrationPreviewDto> PreviewAsync(
        BaselineMigrationPreviewRequest request,
        CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PurgeExpiredPreviews();
            CachedBaselinePreview preview = await BuildPreviewAsync(
                request.JellyfinUserId,
                Guid.NewGuid(),
                cancellationToken).ConfigureAwait(false);
            _previews[preview.Dto.PreviewId] = preview;
            _logger.LogInformation(
                "Created baseline migration preview {PreviewId} for {UserMappings} user mapping(s): {Matched} matched, {Changes} proposed changes, {Ambiguous} ambiguous",
                preview.Dto.PreviewId,
                preview.Dto.Summary.UserMappings,
                preview.Dto.Summary.Matched,
                preview.Dto.Summary.MarkWatched + preview.Dto.Summary.MarkUnwatched,
                preview.Dto.Summary.Ambiguous);
            return preview.Dto;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Applies a valid, still-current dry-run preview.
    /// </summary>
    /// <param name="previewId">Preview identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Durable audit result.</returns>
    public async Task<BaselineMigrationApplyResultDto> ApplyAsync(
        Guid previewId,
        CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PurgeExpiredPreviews();
            if (!_previews.TryGetValue(previewId, out CachedBaselinePreview? cached))
            {
                throw new KeyNotFoundException("The baseline preview was not found or has expired. Run a new dry run.");
            }

            Guid? selectedUserId = cached.UserPlans.Count == 1
                ? cached.UserPlans[0].JellyfinUserId
                : null;
            CachedBaselinePreview current = await BuildPreviewAsync(
                selectedUserId,
                previewId,
                cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(cached.Fingerprint),
                    Convert.FromHexString(current.Fingerprint)))
            {
                throw new InvalidOperationException(
                    "Plex, Jellyfin, or plugin configuration changed after the dry run. Review a new preview before applying.");
            }

            var result = new BaselineMigrationApplyResultDto
            {
                AuditId = Guid.NewGuid(),
                PreviewId = previewId,
                StartedAtUtc = DateTimeOffset.UtcNow,
                Summary = cached.Dto.Summary
            };
            var itemResults = new List<BaselineMigrationApplyItemDto>();

            try
            {
                foreach (UserExecutionPlan userPlan in current.UserPlans)
                {
                    User user = _userManager.GetUserById(userPlan.JellyfinUserId)
                        ?? throw new InvalidOperationException(
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"Jellyfin user {userPlan.JellyfinUserId} no longer exists."));
                    foreach (BaselinePlannedItem planItem in userPlan.Plan.Items.Where(i => i.Action != BaselineMigrationAction.None))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        result.Attempted++;
                        BaselineMigrationApplyItemDto itemResult = await ApplyItemAsync(
                            user,
                            planItem,
                            cancellationToken).ConfigureAwait(false);
                        itemResults.Add(itemResult);
                        if (itemResult.Succeeded)
                        {
                            result.Applied++;
                        }
                        else
                        {
                            result.Failed++;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
            }
            finally
            {
                result.CompletedAtUtc = DateTimeOffset.UtcNow;
                result.Items = itemResults;
                try
                {
                    result.LedgerEntriesWritten = await WriteLedgerAsync(
                        current.UserPlans,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result.LedgerError = ex.Message;
                    _logger.LogError(ex, "Failed to persist the baseline migration watermark ledger");
                }

                await WriteAuditAsync(result, CancellationToken.None).ConfigureAwait(false);
                _previews.TryRemove(previewId, out _);
            }

            _logger.LogInformation(
                "Completed baseline migration audit {AuditId}: {Applied} applied, {Failed} failed, cancelled={Cancelled}",
                result.AuditId,
                result.Applied,
                result.Failed,
                result.Cancelled);
            return result;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Reads recent durable baseline apply audits.
    /// </summary>
    /// <param name="limit">Maximum number to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recent audits, newest first.</returns>
    public async Task<IReadOnlyList<BaselineMigrationApplyResultDto>> GetRecentAuditsAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        string auditDirectory = GetAuditDirectory();
        if (!Directory.Exists(auditDirectory))
        {
            return [];
        }

        var audits = new List<BaselineMigrationApplyResultDto>();
        foreach (string path in Directory
                     .EnumerateFiles(auditDirectory, "*.json")
                     .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                     .Take(Math.Clamp(limit, 1, 100)))
        {
            await using FileStream stream = File.OpenRead(path);
            BaselineMigrationApplyResultDto? audit = await JsonSerializer
                .DeserializeAsync<BaselineMigrationApplyResultDto>(
                    stream,
                    AuditJsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            if (audit is not null)
            {
                audits.Add(audit);
            }
        }

        return audits;
    }

    private async Task<CachedBaselinePreview> BuildPreviewAsync(
        Guid? selectedUserId,
        Guid previewId,
        CancellationToken cancellationToken)
    {
        PluginConfiguration configuration = Plugin.Instance?.Configuration
            ?? throw new InvalidOperationException("Watch State Sync is not initialized.");
        if (string.IsNullOrWhiteSpace(configuration.PlexServerUrl))
        {
            throw new InvalidOperationException("Configure the Plex server URL before running a baseline migration.");
        }
        if (string.IsNullOrWhiteSpace(configuration.PlexAdminToken))
        {
            throw new InvalidOperationException("Configure the Plex administrator token before running a baseline migration.");
        }

        UserMappingConfiguration[] mappings = configuration.UserMappings
            .Where(i => i.Enabled)
            .Where(i => !selectedUserId.HasValue || i.JellyfinUserId == selectedUserId.Value)
            .ToArray();
        if (mappings.Length == 0)
        {
            throw new InvalidOperationException(
                selectedUserId.HasValue
                    ? "No enabled mapping exists for that Jellyfin user."
                    : "Configure at least one enabled user mapping before running a baseline migration.");
        }

        if (mappings.GroupBy(i => i.JellyfinUserId).Any(i => i.Count() > 1))
        {
            throw new InvalidOperationException("Each Jellyfin user can have only one enabled Plex mapping.");
        }

        var userPlans = new List<UserExecutionPlan>();
        var userDtos = new List<BaselineUserPreviewDto>();
        var aggregate = new BaselineMigrationSummaryDto();

        foreach (UserMappingConfiguration mapping in mappings)
        {
            User user = ResolveUser(mapping);
            string plexUserToken = await _plexClient
                .GetUserTokenAsync(configuration.PlexServerUrl, configuration.PlexAdminToken, mapping.PlexUserId, cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyList<PlexWatchStateItem> plexItems = await _plexClient
                .GetWatchStateItemsAsync(
                    configuration.PlexServerUrl,
                    plexUserToken,
                    cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyList<JellyfinWatchStateItem> jellyfinItems = GetJellyfinItems(user);
            BaselineMigrationPlan plan = BaselineMigrationPlanner.Build(plexItems, jellyfinItems);
            aggregate.Add(plan.Summary);

            userDtos.Add(new BaselineUserPreviewDto
            {
                JellyfinUserId = user.Id,
                JellyfinUsername = user.Username,
                PlexUsername = string.IsNullOrWhiteSpace(mapping.PlexUsername)
                    ? user.Username
                    : mapping.PlexUsername,
                Summary = plan.Summary,
                MoviesToUpdate = BuildUpdateGroups(plan.Items, BaselineMediaType.Movie),
                ShowsToUpdate = BuildUpdateGroups(plan.Items, BaselineMediaType.Episode)
            });
            userPlans.Add(new UserExecutionPlan(
                user.Id,
                mapping.PlexUserId,
                mapping.PlexUsername,
                plan));
        }

        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        var dto = new BaselineMigrationPreviewDto
        {
            PreviewId = previewId,
            CreatedAtUtc = createdAt,
            ExpiresAtUtc = createdAt.Add(PreviewLifetime),
            Summary = aggregate,
            Users = userDtos
        };
        return new CachedBaselinePreview(
            dto,
            userPlans,
            ComputeFingerprint(configuration, mappings, userPlans));
    }

    private User ResolveUser(UserMappingConfiguration mapping)
    {
        User? user = mapping.JellyfinUserId != Guid.Empty
            ? _userManager.GetUserById(mapping.JellyfinUserId)
            : null;
        user ??= !string.IsNullOrWhiteSpace(mapping.JellyfinUsername)
            ? _userManager.GetUserByName(mapping.JellyfinUsername)
            : null;
        return user
            ?? throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Mapped Jellyfin user '{mapping.JellyfinUsername}' ({mapping.JellyfinUserId}) was not found."));
    }

    private IReadOnlyList<JellyfinWatchStateItem> GetJellyfinItems(User user)
    {
        return _libraryManager
            .GetItemList(new InternalItemsQuery(user)
            {
                Recursive = true
            })
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
                    userData.LastPlayedDate.HasValue
                        ? new DateTimeOffset(userData.LastPlayedDate.Value.ToUniversalTime())
                        : null);
            })
            .ToArray();
    }

    private static IReadOnlyList<BaselineMigrationUpdateGroupDto> BuildUpdateGroups(
        IReadOnlyList<BaselinePlannedItem> items,
        BaselineMediaType mediaType)
    {
        return items
            .Where(i => i.Action != BaselineMigrationAction.None && i.MediaType == mediaType)
            .GroupBy(
                i => mediaType == BaselineMediaType.Episode
                    ? i.SeriesName ?? i.Title
                    : i.Title,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new BaselineMigrationUpdateGroupDto
            {
                Title = group.Key,
                ItemCount = group.Count(),
                MarkWatched = group.Count(i => i.Action == BaselineMigrationAction.MarkWatched),
                MarkUnwatched = group.Count(i => i.Action == BaselineMigrationAction.MarkUnwatched)
            })
            .OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private Task<BaselineMigrationApplyItemDto> ApplyItemAsync(
        User user,
        BaselinePlannedItem planItem,
        CancellationToken cancellationToken)
    {
        var result = new BaselineMigrationApplyItemDto
        {
            JellyfinItemId = planItem.JellyfinItemId!.Value,
            PlexRatingKey = planItem.PlexRatingKey!,
            Title = planItem.Title,
            Action = planItem.Action.ToString()
        };

        try
        {
            BaseItem item = _libraryManager.GetItemById(result.JellyfinItemId)
                ?? throw new InvalidOperationException("The Jellyfin item no longer exists.");
            if (!CanonicalPathMatcher.IsMatch(item.Path, planItem.Path!))
            {
                throw new InvalidOperationException("The Jellyfin media path changed after the dry run.");
            }

            bool desiredPlayed = planItem.Action == BaselineMigrationAction.MarkWatched;
            UserItemData userData = _userDataManager.GetUserData(user, item)
                ?? new UserItemData { Key = item.Id.ToString("N", CultureInfo.InvariantCulture) };
            userData.Played = desiredPlayed;
            userData.PlayCount = desiredPlayed
                ? Math.Max(1, userData.PlayCount)
                : 0;
            _userDataManager.SaveUserData(
                user,
                item,
                userData,
                UserDataSaveReason.Import,
                cancellationToken);

            UserItemData verified = _userDataManager.GetUserData(user, item)
                ?? throw new InvalidOperationException("Jellyfin did not return persisted user data.");
            if (verified.Played != desiredPlayed)
            {
                throw new InvalidOperationException("Jellyfin did not persist the requested watched state.");
            }

            result.Succeeded = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Error = ex.Message;
            _logger.LogError(
                ex,
                "Failed baseline migration write for Jellyfin item {ItemId} and Plex rating key {RatingKey}",
                result.JellyfinItemId,
                result.PlexRatingKey);
        }

        return Task.FromResult(result);
    }

    private async Task WriteAuditAsync(
        BaselineMigrationApplyResultDto result,
        CancellationToken cancellationToken)
    {
        string directory = GetAuditDirectory();
        Directory.CreateDirectory(directory);
        string timestamp = result.StartedAtUtc.UtcDateTime.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture);
        string destination = Path.Combine(directory, $"{timestamp}-{result.AuditId:N}.json");
        string temporary = destination + ".tmp";
        await using (FileStream stream = File.Create(temporary))
        {
            await JsonSerializer
                .SerializeAsync(stream, result, AuditJsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(temporary, destination, true);
    }

    private async Task<int> WriteLedgerAsync(
        IReadOnlyList<UserExecutionPlan> userPlans,
        CancellationToken cancellationToken)
    {
        string dataFolder = Plugin.Instance?.DataFolderPath
            ?? throw new InvalidOperationException("Watch State Sync data directory is unavailable.");
        Directory.CreateDirectory(dataFolder);
        string destination = Path.Combine(dataFolder, "baseline-ledger.json");
        BaselineLedgerDto existing = new();
        if (File.Exists(destination))
        {
            await using FileStream input = File.OpenRead(destination);
            existing = await JsonSerializer
                .DeserializeAsync<BaselineLedgerDto>(
                    input,
                    AuditJsonOptions,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? new BaselineLedgerDto();
        }

        Dictionary<(Guid UserId, Guid ItemId), BaselineLedgerEntryDto> entries = existing.Entries
            .ToDictionary(i => (i.JellyfinUserId, i.JellyfinItemId));
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        int written = 0;
        foreach (UserExecutionPlan userPlan in userPlans)
        {
            User user = _userManager.GetUserById(userPlan.JellyfinUserId)
                ?? throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Jellyfin user {userPlan.JellyfinUserId} no longer exists."));
            foreach (BaselinePlannedItem planItem in userPlan.Plan.Items.Where(i => i.MatchStatus == BaselineMatchStatus.Matched))
            {
                BaseItem item = _libraryManager.GetItemById(planItem.JellyfinItemId!.Value)
                    ?? throw new InvalidOperationException("A matched Jellyfin item disappeared while writing the baseline ledger.");
                UserItemData userData = _userDataManager.GetUserData(user, item)
                    ?? new UserItemData { Key = item.Id.ToString("N", CultureInfo.InvariantCulture) };
                entries[(user.Id, item.Id)] = new BaselineLedgerEntryDto
                {
                    JellyfinUserId = user.Id,
                    PlexUserId = userPlan.PlexUserId,
                    PlexUsername = userPlan.PlexUsername,
                    JellyfinItemId = item.Id,
                    PlexRatingKey = planItem.PlexRatingKey!,
                    Path = CanonicalPathMatcher.Normalize(item.Path),
                    PlexPlayed = planItem.PlexPlayed!.Value,
                    JellyfinPlayed = userData.Played,
                    PlexLastViewedAt = planItem.PlexLastViewedAt,
                    JellyfinLastPlayedDate = userData.LastPlayedDate.HasValue
                        ? new DateTimeOffset(userData.LastPlayedDate.Value.ToUniversalTime())
                        : null,
                    ObservedAtUtc = observedAt
                };
                written++;
            }
        }

        var ledger = new BaselineLedgerDto
        {
            UpdatedAtUtc = observedAt,
            Entries = entries.Values
                .OrderBy(i => i.JellyfinUserId)
                .ThenBy(i => i.JellyfinItemId)
                .ToArray()
        };
        string temporary = destination + ".tmp";
        await using (FileStream output = File.Create(temporary))
        {
            await JsonSerializer
                .SerializeAsync(output, ledger, AuditJsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(temporary, destination, true);
        return written;
    }

    private static string GetAuditDirectory()
    {
        string dataFolder = Plugin.Instance?.DataFolderPath
            ?? throw new InvalidOperationException("Watch State Sync data directory is unavailable.");
        return Path.Combine(dataFolder, "baseline-audits");
    }

    private void PurgeExpiredPreviews()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach ((Guid key, CachedBaselinePreview preview) in _previews)
        {
            if (preview.Dto.ExpiresAtUtc <= now)
            {
                _previews.TryRemove(key, out _);
            }
        }
    }

    private static string ComputeFingerprint(
        PluginConfiguration configuration,
        IReadOnlyList<UserMappingConfiguration> mappings,
        IReadOnlyList<UserExecutionPlan> userPlans)
    {
        var builder = new StringBuilder();
        builder.AppendLine(configuration.PlexServerUrl.Trim());
        foreach (UserMappingConfiguration mapping in mappings.OrderBy(i => i.JellyfinUserId))
        {
            builder
                .Append(mapping.JellyfinUserId)
                .Append('|')
                .Append(mapping.PlexUserId)
                .Append('|')
                .Append(mapping.PlexUsername)
                .Append('|')
                .Append(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configuration.PlexAdminToken))))
                .AppendLine();
        }

        foreach (UserExecutionPlan userPlan in userPlans.OrderBy(i => i.JellyfinUserId))
        {
            foreach (BaselinePlannedItem item in userPlan.Plan.Items
                         .OrderBy(i => i.PlexRatingKey, StringComparer.Ordinal)
                         .ThenBy(i => i.JellyfinItemId))
            {
                builder
                    .Append(userPlan.JellyfinUserId)
                    .Append('|')
                    .Append(item.PlexRatingKey)
                    .Append('|')
                    .Append(item.JellyfinItemId)
                    .Append('|')
                    .Append(item.Path)
                    .Append('|')
                    .Append(item.PlexPlayed)
                    .Append('|')
                    .Append(item.JellyfinPlayed)
                    .Append('|')
                    .Append(item.PlexLastViewedAt)
                    .Append('|')
                    .Append(item.JellyfinLastPlayedDate)
                    .Append('|')
                    .Append(item.MatchStatus)
                    .Append('|')
                    .Append(item.Action)
                    .AppendLine();
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private sealed record UserExecutionPlan(
        Guid JellyfinUserId,
        string PlexUserId,
        string PlexUsername,
        BaselineMigrationPlan Plan);

    private sealed record CachedBaselinePreview(
        BaselineMigrationPreviewDto Dto,
        IReadOnlyList<UserExecutionPlan> UserPlans,
        string Fingerprint);
}
