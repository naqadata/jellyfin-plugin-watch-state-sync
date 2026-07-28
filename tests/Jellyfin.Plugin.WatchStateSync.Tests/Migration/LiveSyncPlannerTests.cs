using Jellyfin.Plugin.WatchStateSync.Migration;
using Xunit;

namespace Jellyfin.Plugin.WatchStateSync.Tests.Migration;

public sealed class LiveSyncPlannerTests
{
    [Fact]
    public void ChooseDirection_OnlyUsesStrictlyNewerCompletionTimestamp()
    {
        DateTimeOffset earlier = DateTimeOffset.Parse("2026-07-28T00:00:00Z");
        DateTimeOffset later = earlier.AddMinutes(1);

        Assert.Equal(LiveSyncDirection.PlexToJellyfin, LiveSyncPlanner.ChooseDirection(later, earlier));
        Assert.Equal(LiveSyncDirection.JellyfinToPlex, LiveSyncPlanner.ChooseDirection(earlier, later));
        Assert.Equal(LiveSyncDirection.None, LiveSyncPlanner.ChooseDirection(earlier, earlier.AddSeconds(1)));
    }

    [Fact]
    public void ChooseDirection_PropagatesACompletionTimestampSeenOnOnlyOneSide()
    {
        DateTimeOffset timestamp = DateTimeOffset.Parse("2026-07-28T00:00:00Z");

        Assert.Equal(LiveSyncDirection.PlexToJellyfin, LiveSyncPlanner.ChooseDirection(timestamp, null));
        Assert.Equal(LiveSyncDirection.JellyfinToPlex, LiveSyncPlanner.ChooseDirection(null, timestamp));
        Assert.Equal(LiveSyncDirection.None, LiveSyncPlanner.ChooseDirection(null, null));
    }
}
