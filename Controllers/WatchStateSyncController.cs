using Jellyfin.Plugin.WatchStateSync.Models;
using Jellyfin.Plugin.WatchStateSync.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.WatchStateSync.Controllers;

/// <summary>
/// Administrative API for explicit watch-state migration operations.
/// </summary>
[ApiController]
[Route("[controller]")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class WatchStateSyncController : ControllerBase
{
    private readonly BaselineMigrationService _baselineMigrationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchStateSyncController"/> class.
    /// </summary>
    /// <param name="baselineMigrationService">Baseline migration service.</param>
    public WatchStateSyncController(BaselineMigrationService baselineMigrationService)
    {
        _baselineMigrationService = baselineMigrationService;
    }

    /// <summary>
    /// Gets Jellyfin users available for mapping.
    /// </summary>
    /// <returns>Jellyfin users.</returns>
    [HttpGet("Admin/Users")]
    public ActionResult<IReadOnlyList<JellyfinUserOptionDto>> GetUsers()
    {
        return Ok(_baselineMigrationService.GetJellyfinUsers());
    }

    /// <summary>
    /// Creates a dry-run Plex-to-Jellyfin baseline preview.
    /// </summary>
    /// <param name="request">Preview request.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>Dry-run report and preview id.</returns>
    [HttpPost("Admin/Baseline/Preview")]
    public async Task<ActionResult<BaselineMigrationPreviewDto>> PreviewBaseline(
        [FromBody] BaselineMigrationPreviewRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _baselineMigrationService
                .PreviewAsync(request, cancellationToken)
                .ConfigureAwait(false));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Explicitly applies a valid, still-current baseline preview.
    /// </summary>
    /// <param name="request">Apply request.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>Durable apply audit.</returns>
    [HttpPost("Admin/Baseline/Apply")]
    public async Task<ActionResult<BaselineMigrationApplyResultDto>> ApplyBaseline(
        [FromBody] ApplyBaselineMigrationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.PreviewId == Guid.Empty)
        {
            return BadRequest(new { Error = "A preview id is required." });
        }

        try
        {
            return Ok(await _baselineMigrationService
                .ApplyAsync(request.PreviewId, cancellationToken)
                .ConfigureAwait(false));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { Error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Gets recent durable baseline migration audits.
    /// </summary>
    /// <param name="limit">Maximum number of audits.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>Recent audits.</returns>
    [HttpGet("Admin/Baseline/Audits")]
    public async Task<ActionResult<IReadOnlyList<BaselineMigrationApplyResultDto>>> GetBaselineAudits(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _baselineMigrationService
            .GetRecentAuditsAsync(limit, cancellationToken)
            .ConfigureAwait(false));
    }
}
