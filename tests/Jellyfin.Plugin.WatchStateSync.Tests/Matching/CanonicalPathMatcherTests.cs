using Jellyfin.Plugin.WatchStateSync.Matching;
using Xunit;

namespace Jellyfin.Plugin.WatchStateSync.Tests.Matching;

public sealed class CanonicalPathMatcherTests
{
    [Theory]
    [InlineData("/media/Movies/Fixture Movie/movie.mp4", "/media/Movies/Fixture Movie/movie.mp4")]
    [InlineData("/media//Movies/Fixture Movie/movie.mp4/", "/media/Movies/Fixture Movie/movie.mp4")]
    [InlineData(@"C:\media\Movies\Fixture Movie\movie.mp4", "C:/media/Movies/Fixture Movie/movie.mp4")]
    public void IsMatch_NormalizesHarmlessPathDifferences(string jellyfinPath, string plexPath)
    {
        Assert.True(CanonicalPathMatcher.IsMatch(jellyfinPath, plexPath));
    }

    [Fact]
    public void IsMatch_AppliesConfiguredRootMapping()
    {
        var matched = CanonicalPathMatcher.IsMatch(
            "/jellyfin-media/Movies/Fixture Movie/movie.mp4",
            "/plex-media/Movies/Fixture Movie/movie.mp4",
            "/jellyfin-media",
            "/plex-media");

        Assert.True(matched);
    }

    [Fact]
    public void IsMatch_RemainsCaseSensitive()
    {
        Assert.False(
            CanonicalPathMatcher.IsMatch(
                "/media/Movies/Fixture Movie/movie.mp4",
                "/media/movies/Fixture Movie/movie.mp4"));
    }

    [Fact]
    public void ApplyRootMapping_DoesNotReplacePartialDirectoryName()
    {
        var mapped = CanonicalPathMatcher.ApplyRootMapping(
            "/media-other/Movies/movie.mp4",
            "/media",
            "/data");

        Assert.Equal("/media-other/Movies/movie.mp4", mapped);
    }
}
