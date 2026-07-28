using Jellyfin.Plugin.WatchStateSync.Migration;
using Xunit;

namespace Jellyfin.Plugin.WatchStateSync.Tests.Migration;

public sealed class BaselineMigrationPlannerTests
{
    [Fact]
    public void Build_ProposesPlexAuthoritativeChangesAndNoOp()
    {
        JellyfinWatchStateItem[] jellyfin =
        [
            Item("movie", "/media/movie.mp4", false),
            Item("episode", "/media/episode.mp4", true),
            Item("same", "/media/same.mp4", true)
        ];
        PlexWatchStateItem[] plex =
        [
            Plex("1", "/media/movie.mp4", true),
            Plex("2", "/media/episode.mp4", false),
            Plex("3", "/media/same.mp4", true)
        ];

        BaselineMigrationPlan plan = BaselineMigrationPlanner.Build(plex, jellyfin);

        Assert.Equal(3, plan.Summary.Matched);
        Assert.Equal(1, plan.Summary.MarkWatched);
        Assert.Equal(1, plan.Summary.MarkUnwatched);
        Assert.Equal(1, plan.Summary.NoChange);
        Assert.Contains(plan.Items, i => i.PlexRatingKey == "1" && i.Action == BaselineMigrationAction.MarkWatched);
        Assert.Contains(plan.Items, i => i.PlexRatingKey == "2" && i.Action == BaselineMigrationAction.MarkUnwatched);
        Assert.Contains(plan.Items, i => i.PlexRatingKey == "3" && i.Action == BaselineMigrationAction.None);
    }

    [Fact]
    public void Build_ReportsUnmatchedItemsWithoutWriting()
    {
        JellyfinWatchStateItem[] jellyfin = [Item("jellyfin-only", "/media/jellyfin.mp4", true)];
        PlexWatchStateItem[] plex = [Plex("plex-only", "/media/plex.mp4", true)];

        BaselineMigrationPlan plan = BaselineMigrationPlanner.Build(plex, jellyfin);

        Assert.Equal(1, plan.Summary.UnmatchedPlex);
        Assert.Equal(1, plan.Summary.UnmatchedJellyfin);
        Assert.All(plan.Items, i => Assert.Equal(BaselineMigrationAction.None, i.Action));
    }

    [Fact]
    public void Build_RejectsDuplicateJellyfinPathAsAmbiguous()
    {
        JellyfinWatchStateItem[] jellyfin =
        [
            Item("first", "/media/duplicate.mp4", false),
            Item("second", "/media/duplicate.mp4", false)
        ];
        PlexWatchStateItem[] plex = [Plex("1", "/media/duplicate.mp4", true)];

        BaselineMigrationPlan plan = BaselineMigrationPlanner.Build(plex, jellyfin);

        Assert.Equal(1, plan.Summary.Ambiguous);
        Assert.Equal(0, plan.Summary.Matched);
        Assert.DoesNotContain(plan.Items, i => i.Action != BaselineMigrationAction.None);
    }

    [Fact]
    public void Build_RejectsMultiplePlexItemsForOneJellyfinItem()
    {
        JellyfinWatchStateItem[] jellyfin = [Item("movie", "/media/movie.mp4", false)];
        PlexWatchStateItem[] plex =
        [
            Plex("1", "/media/movie.mp4", true),
            Plex("2", "/media/movie.mp4", true)
        ];

        BaselineMigrationPlan plan = BaselineMigrationPlanner.Build(plex, jellyfin);

        Assert.Equal(2, plan.Summary.Ambiguous);
        Assert.Equal(0, plan.Summary.Matched);
        Assert.DoesNotContain(plan.Items, i => i.Action != BaselineMigrationAction.None);
    }

    [Fact]
    public void Build_NormalizesHarmlessPathSyntax()
    {
        JellyfinWatchStateItem[] jellyfin = [Item("movie", "/media/Movies/movie.mp4", false)];
        PlexWatchStateItem[] plex = [Plex("1", @"\media\Movies\movie.mp4", true)];

        BaselineMigrationPlan plan = BaselineMigrationPlanner.Build(plex, jellyfin);

        Assert.Equal(1, plan.Summary.Matched);
        Assert.Equal(1, plan.Summary.MarkWatched);
    }

    private static JellyfinWatchStateItem Item(string title, string path, bool played)
    {
        return new JellyfinWatchStateItem(Guid.NewGuid(), title, path, played);
    }

    private static PlexWatchStateItem Plex(string key, string path, bool played)
    {
        return new PlexWatchStateItem(key, key, [path], played, null);
    }
}
