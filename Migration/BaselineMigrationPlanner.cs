using Jellyfin.Plugin.WatchStateSync.Matching;
using Jellyfin.Plugin.WatchStateSync.Models;

namespace Jellyfin.Plugin.WatchStateSync.Migration;

/// <summary>
/// Plex movie or episode watch state used by the baseline planner.
/// </summary>
public sealed record PlexWatchStateItem(
    string RatingKey,
    string Title,
    IReadOnlyList<string> Paths,
    bool Played,
    DateTimeOffset? LastViewedAt);

/// <summary>
/// Jellyfin movie or episode watch state used by the baseline planner.
/// </summary>
public sealed record JellyfinWatchStateItem(
    Guid ItemId,
    string Title,
    string Path,
    bool Played);

/// <summary>
/// Match classifications emitted by the baseline planner.
/// </summary>
public enum BaselineMatchStatus
{
    /// <summary>A unique exact-path match.</summary>
    Matched,

    /// <summary>A Plex item has no Jellyfin exact-path match.</summary>
    UnmatchedPlex,

    /// <summary>A Jellyfin item has no Plex exact-path match.</summary>
    UnmatchedJellyfin,

    /// <summary>More than one item could own the same exact path.</summary>
    Ambiguous
}

/// <summary>
/// Proposed state changes emitted by the baseline planner.
/// </summary>
public enum BaselineMigrationAction
{
    /// <summary>No write is required.</summary>
    None,

    /// <summary>Mark the Jellyfin item watched.</summary>
    MarkWatched,

    /// <summary>Mark the Jellyfin item unwatched.</summary>
    MarkUnwatched
}

/// <summary>
/// One internal baseline plan row.
/// </summary>
public sealed record BaselinePlannedItem(
    string? PlexRatingKey,
    Guid? JellyfinItemId,
    string Title,
    string? Path,
    bool? PlexPlayed,
    bool? JellyfinPlayed,
    BaselineMatchStatus MatchStatus,
    BaselineMigrationAction Action,
    string Reason);

/// <summary>
/// Complete exact-path plan for one user mapping.
/// </summary>
public sealed class BaselineMigrationPlan
{
    /// <summary>
    /// Gets or sets plan rows.
    /// </summary>
    public IReadOnlyList<BaselinePlannedItem> Items { get; set; } = [];

    /// <summary>
    /// Gets or sets summary counts.
    /// </summary>
    public BaselineMigrationSummaryDto Summary { get; set; } = new();
}

/// <summary>
/// Builds a conservative Plex-authoritative baseline plan.
/// </summary>
public static class BaselineMigrationPlanner
{
    /// <summary>
    /// Matches Plex items to Jellyfin items by unique exact canonical path.
    /// </summary>
    /// <param name="plexItems">Plex movies and episodes.</param>
    /// <param name="jellyfinItems">Jellyfin movies and episodes with user state.</param>
    /// <returns>A complete dry-run plan.</returns>
    public static BaselineMigrationPlan Build(
        IReadOnlyList<PlexWatchStateItem> plexItems,
        IReadOnlyList<JellyfinWatchStateItem> jellyfinItems)
    {
        Dictionary<string, List<JellyfinWatchStateItem>> jellyfinByPath = new(StringComparer.Ordinal);
        foreach (JellyfinWatchStateItem item in jellyfinItems)
        {
            string normalizedPath = CanonicalPathMatcher.Normalize(item.Path);
            if (!jellyfinByPath.TryGetValue(normalizedPath, out List<JellyfinWatchStateItem>? items))
            {
                items = [];
                jellyfinByPath[normalizedPath] = items;
            }

            items.Add(item);
        }

        var provisional = new List<(PlexWatchStateItem Plex, JellyfinWatchStateItem Jellyfin, string Path)>();
        var rows = new List<BaselinePlannedItem>();

        foreach (PlexWatchStateItem plexItem in plexItems)
        {
            var candidates = new Dictionary<Guid, (JellyfinWatchStateItem Item, string Path)>();
            foreach (string path in plexItem.Paths.Where(i => !string.IsNullOrWhiteSpace(i)))
            {
                string normalizedPath = CanonicalPathMatcher.Normalize(path);
                if (!jellyfinByPath.TryGetValue(normalizedPath, out List<JellyfinWatchStateItem>? matches))
                {
                    continue;
                }

                foreach (JellyfinWatchStateItem match in matches)
                {
                    candidates[match.ItemId] = (match, normalizedPath);
                }
            }

            if (candidates.Count == 0)
            {
                rows.Add(new BaselinePlannedItem(
                    plexItem.RatingKey,
                    null,
                    plexItem.Title,
                    plexItem.Paths.FirstOrDefault(),
                    plexItem.Played,
                    null,
                    BaselineMatchStatus.UnmatchedPlex,
                    BaselineMigrationAction.None,
                    "No Jellyfin movie or episode has an exact canonical media path."));
                continue;
            }

            if (candidates.Count > 1)
            {
                rows.Add(new BaselinePlannedItem(
                    plexItem.RatingKey,
                    null,
                    plexItem.Title,
                    plexItem.Paths.FirstOrDefault(),
                    plexItem.Played,
                    null,
                    BaselineMatchStatus.Ambiguous,
                    BaselineMigrationAction.None,
                    "The Plex media paths match more than one Jellyfin item."));
                continue;
            }

            (JellyfinWatchStateItem jellyfinItem, string matchedPath) = candidates.Values.Single();
            provisional.Add((plexItem, jellyfinItem, matchedPath));
        }

        HashSet<string> duplicatePlexKeys = provisional
            .GroupBy(i => i.Jellyfin.ItemId)
            .Where(i => i.Count() > 1)
            .SelectMany(i => i.Select(match => match.Plex.RatingKey))
            .ToHashSet(StringComparer.Ordinal);
        var matchedJellyfinIds = new HashSet<Guid>();

        foreach ((PlexWatchStateItem plexItem, JellyfinWatchStateItem jellyfinItem, string matchedPath) in provisional)
        {
            if (duplicatePlexKeys.Contains(plexItem.RatingKey))
            {
                rows.Add(new BaselinePlannedItem(
                    plexItem.RatingKey,
                    jellyfinItem.ItemId,
                    plexItem.Title,
                    matchedPath,
                    plexItem.Played,
                    jellyfinItem.Played,
                    BaselineMatchStatus.Ambiguous,
                    BaselineMigrationAction.None,
                    "Multiple Plex items resolve to the same Jellyfin item."));
                continue;
            }

            matchedJellyfinIds.Add(jellyfinItem.ItemId);
            BaselineMigrationAction action = plexItem.Played == jellyfinItem.Played
                ? BaselineMigrationAction.None
                : plexItem.Played
                    ? BaselineMigrationAction.MarkWatched
                    : BaselineMigrationAction.MarkUnwatched;
            rows.Add(new BaselinePlannedItem(
                plexItem.RatingKey,
                jellyfinItem.ItemId,
                plexItem.Title,
                matchedPath,
                plexItem.Played,
                jellyfinItem.Played,
                BaselineMatchStatus.Matched,
                action,
                action switch
                {
                    BaselineMigrationAction.MarkWatched => "Plex is watched and Jellyfin is unwatched.",
                    BaselineMigrationAction.MarkUnwatched => "Plex is unwatched and Jellyfin is watched.",
                    _ => "Jellyfin already matches the authoritative Plex state."
                }));
        }

        rows.AddRange(
            jellyfinItems
                .Where(i => !matchedJellyfinIds.Contains(i.ItemId))
                .Where(i => !rows.Any(r => r.JellyfinItemId == i.ItemId && r.MatchStatus == BaselineMatchStatus.Ambiguous))
                .Select(i => new BaselinePlannedItem(
                    null,
                    i.ItemId,
                    i.Title,
                    CanonicalPathMatcher.Normalize(i.Path),
                    null,
                    i.Played,
                    BaselineMatchStatus.UnmatchedJellyfin,
                    BaselineMigrationAction.None,
                    "No Plex movie or episode has an exact canonical media path.")));

        BaselineMigrationSummaryDto summary = BuildSummary(plexItems.Count, jellyfinItems.Count, rows);
        return new BaselineMigrationPlan
        {
            Items = rows
                .OrderBy(i => i.MatchStatus)
                .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.PlexRatingKey, StringComparer.Ordinal)
                .ToArray(),
            Summary = summary
        };
    }

    private static BaselineMigrationSummaryDto BuildSummary(
        int plexItemCount,
        int jellyfinItemCount,
        IReadOnlyList<BaselinePlannedItem> items)
    {
        return new BaselineMigrationSummaryDto
        {
            UserMappings = 1,
            PlexItems = plexItemCount,
            JellyfinItems = jellyfinItemCount,
            Matched = items.Count(i => i.MatchStatus == BaselineMatchStatus.Matched),
            UnmatchedPlex = items.Count(i => i.MatchStatus == BaselineMatchStatus.UnmatchedPlex),
            UnmatchedJellyfin = items.Count(i => i.MatchStatus == BaselineMatchStatus.UnmatchedJellyfin),
            Ambiguous = items.Count(i => i.MatchStatus == BaselineMatchStatus.Ambiguous && i.PlexRatingKey is not null),
            MarkWatched = items.Count(i => i.Action == BaselineMigrationAction.MarkWatched),
            MarkUnwatched = items.Count(i => i.Action == BaselineMigrationAction.MarkUnwatched),
            NoChange = items.Count(i => i.MatchStatus == BaselineMatchStatus.Matched && i.Action == BaselineMigrationAction.None)
        };
    }
}
