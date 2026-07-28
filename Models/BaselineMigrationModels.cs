namespace Jellyfin.Plugin.WatchStateSync.Models;

/// <summary>
/// Requests a manual Plex-to-Jellyfin baseline preview.
/// </summary>
public sealed class BaselineMigrationPreviewRequest
{
    /// <summary>
    /// Gets or sets an optional Jellyfin user id. When omitted, all enabled mappings are previewed.
    /// </summary>
    public Guid? JellyfinUserId { get; set; }
}

/// <summary>
/// Requests application of a previously generated baseline preview.
/// </summary>
public sealed class ApplyBaselineMigrationRequest
{
    /// <summary>
    /// Gets or sets the preview identifier to apply.
    /// </summary>
    public Guid PreviewId { get; set; }
}

/// <summary>
/// A Jellyfin user available for mapping.
/// </summary>
public sealed class JellyfinUserOptionDto
{
    /// <summary>
    /// Gets or sets the stable user identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// A Plex user discoverable with the configured Plex administrator token.
/// </summary>
public sealed class PlexUserOptionDto
{
    /// <summary>Gets or sets the stable discovery identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the Plex Home user's display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets whether Plex requires a PIN to switch to this profile.</summary>
    public bool IsProtected { get; set; }
}

/// <summary>
/// Summary counts for one or more baseline migration plans.
/// </summary>
public sealed class BaselineMigrationSummaryDto
{
    /// <summary>
    /// Gets or sets the number of user mappings included.
    /// </summary>
    public int UserMappings { get; set; }

    /// <summary>
    /// Gets or sets the number of Plex movies and episodes scanned.
    /// </summary>
    public int PlexItems { get; set; }

    /// <summary>
    /// Gets or sets the number of Jellyfin movies and episodes scanned.
    /// </summary>
    public int JellyfinItems { get; set; }

    /// <summary>
    /// Gets or sets the number of unique exact-path matches.
    /// </summary>
    public int Matched { get; set; }

    /// <summary>
    /// Gets or sets the number of Plex items with no Jellyfin match.
    /// </summary>
    public int UnmatchedPlex { get; set; }

    /// <summary>
    /// Gets or sets the number of Jellyfin items with no Plex match.
    /// </summary>
    public int UnmatchedJellyfin { get; set; }

    /// <summary>
    /// Gets or sets the number of Plex items excluded because the match was ambiguous.
    /// </summary>
    public int Ambiguous { get; set; }

    /// <summary>
    /// Gets or sets the number of Jellyfin items that would be marked watched.
    /// </summary>
    public int MarkWatched { get; set; }

    /// <summary>
    /// Gets or sets the number of Jellyfin items that would be marked unwatched.
    /// </summary>
    public int MarkUnwatched { get; set; }

    /// <summary>
    /// Gets or sets the number of matched items already in the authoritative Plex state.
    /// </summary>
    public int NoChange { get; set; }

    /// <summary>
    /// Adds another summary into this instance.
    /// </summary>
    /// <param name="other">Summary to add.</param>
    public void Add(BaselineMigrationSummaryDto other)
    {
        UserMappings += other.UserMappings;
        PlexItems += other.PlexItems;
        JellyfinItems += other.JellyfinItems;
        Matched += other.Matched;
        UnmatchedPlex += other.UnmatchedPlex;
        UnmatchedJellyfin += other.UnmatchedJellyfin;
        Ambiguous += other.Ambiguous;
        MarkWatched += other.MarkWatched;
        MarkUnwatched += other.MarkUnwatched;
        NoChange += other.NoChange;
    }
}

/// <summary>
/// A compact group of planned baseline updates.
/// </summary>
public sealed class BaselineMigrationUpdateGroupDto
{
    /// <summary>
    /// Gets or sets the movie or show title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of movie items or episodes in this group.
    /// </summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// Gets or sets the number of items that would be marked watched.
    /// </summary>
    public int MarkWatched { get; set; }

    /// <summary>
    /// Gets or sets the number of items that would be marked unwatched.
    /// </summary>
    public int MarkUnwatched { get; set; }
}

/// <summary>
/// Preview details for one user mapping.
/// </summary>
public sealed class BaselineUserPreviewDto
{
    /// <summary>
    /// Gets or sets the Jellyfin user identifier.
    /// </summary>
    public Guid JellyfinUserId { get; set; }

    /// <summary>
    /// Gets or sets the Jellyfin username.
    /// </summary>
    public string JellyfinUsername { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the configured Plex username.
    /// </summary>
    public string PlexUsername { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets summary counts.
    /// </summary>
    public BaselineMigrationSummaryDto Summary { get; set; } = new();

    /// <summary>
    /// Gets or sets movies that have planned updates.
    /// </summary>
    public IReadOnlyList<BaselineMigrationUpdateGroupDto> MoviesToUpdate { get; set; } = [];

    /// <summary>
    /// Gets or sets shows that have one or more episode updates.
    /// </summary>
    public IReadOnlyList<BaselineMigrationUpdateGroupDto> ShowsToUpdate { get; set; } = [];
}

/// <summary>
/// Dry-run result required before an apply operation.
/// </summary>
public sealed class BaselineMigrationPreviewDto
{
    /// <summary>
    /// Gets or sets the unique preview identifier.
    /// </summary>
    public Guid PreviewId { get; set; }

    /// <summary>
    /// Gets or sets preview creation time.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets preview expiration time.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>
    /// Gets or sets aggregate summary counts.
    /// </summary>
    public BaselineMigrationSummaryDto Summary { get; set; } = new();

    /// <summary>
    /// Gets or sets per-user preview details.
    /// </summary>
    public IReadOnlyList<BaselineUserPreviewDto> Users { get; set; } = [];
}

/// <summary>
/// Result of applying one previewed item change.
/// </summary>
public sealed class BaselineMigrationApplyItemDto
{
    /// <summary>
    /// Gets or sets the Jellyfin item identifier.
    /// </summary>
    public Guid JellyfinItemId { get; set; }

    /// <summary>
    /// Gets or sets the Plex rating key.
    /// </summary>
    public string PlexRatingKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the requested action.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the write succeeded.
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// Gets or sets an error message when the write failed.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Durable audit result for a baseline apply operation.
/// </summary>
public sealed class BaselineMigrationApplyResultDto
{
    /// <summary>
    /// Gets or sets the audit identifier.
    /// </summary>
    public Guid AuditId { get; set; }

    /// <summary>
    /// Gets or sets the preview identifier.
    /// </summary>
    public Guid PreviewId { get; set; }

    /// <summary>
    /// Gets or sets start time.
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets completion time.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether cancellation interrupted the operation.
    /// </summary>
    public bool Cancelled { get; set; }

    /// <summary>
    /// Gets or sets the full dry-run summary that authorized this apply.
    /// </summary>
    public BaselineMigrationSummaryDto Summary { get; set; } = new();

    /// <summary>
    /// Gets or sets the number of proposed writes.
    /// </summary>
    public int Attempted { get; set; }

    /// <summary>
    /// Gets or sets the number of successful writes.
    /// </summary>
    public int Applied { get; set; }

    /// <summary>
    /// Gets or sets the number of failed writes.
    /// </summary>
    public int Failed { get; set; }

    /// <summary>
    /// Gets or sets the number of confident match fingerprints written to the baseline ledger.
    /// </summary>
    public int LedgerEntriesWritten { get; set; }

    /// <summary>
    /// Gets or sets a ledger persistence error. Item writes may still have succeeded.
    /// </summary>
    public string? LedgerError { get; set; }

    /// <summary>
    /// Gets or sets applied item results.
    /// </summary>
    public IReadOnlyList<BaselineMigrationApplyItemDto> Items { get; set; } = [];
}

/// <summary>
/// Durable starting watermark for future completed-view synchronization.
/// </summary>
public sealed class BaselineLedgerDto
{
    /// <summary>
    /// Gets or sets the latest ledger update time.
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets one entry per mapped user and confidently matched media item.
    /// </summary>
    public IReadOnlyList<BaselineLedgerEntryDto> Entries { get; set; } = [];
}

/// <summary>
/// Token-free state fingerprint captured after a baseline apply.
/// </summary>
public sealed class BaselineLedgerEntryDto
{
    /// <summary>
    /// Gets or sets the Jellyfin user identifier.
    /// </summary>
    public Guid JellyfinUserId { get; set; }

    /// <summary>
    /// Gets or sets the configured Plex user identifier.
    /// </summary>
    public string PlexUserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the configured Plex username.
    /// </summary>
    public string PlexUsername { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Jellyfin item identifier.
    /// </summary>
    public Guid JellyfinItemId { get; set; }

    /// <summary>
    /// Gets or sets the Plex rating key.
    /// </summary>
    public string PlexRatingKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the canonical exact-match path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the observed Plex watched state.
    /// </summary>
    public bool PlexPlayed { get; set; }

    /// <summary>
    /// Gets or sets the observed Jellyfin watched state after apply.
    /// </summary>
    public bool JellyfinPlayed { get; set; }

    /// <summary>
    /// Gets or sets Plex's last-viewed evidence.
    /// </summary>
    public DateTimeOffset? PlexLastViewedAt { get; set; }

    /// <summary>
    /// Gets or sets Jellyfin's last-played evidence.
    /// </summary>
    public DateTimeOffset? JellyfinLastPlayedDate { get; set; }

    /// <summary>
    /// Gets or sets observation time.
    /// </summary>
    public DateTimeOffset ObservedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the confidence-ranked match method.
    /// </summary>
    public string MatchMethod { get; set; } = "ExactPath";
}
