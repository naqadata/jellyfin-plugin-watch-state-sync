using System.Net;
using Jellyfin.Plugin.WatchStateSync.Migration;
using Jellyfin.Plugin.WatchStateSync.Models;
using Jellyfin.Plugin.WatchStateSync.Plex;
using Xunit;

namespace Jellyfin.Plugin.WatchStateSync.Tests.Plex;

public sealed class PlexClientTests
{
    [Fact]
    public async Task GetWatchStateItemsAsync_ParsesTokenScopedMovieState()
    {
        var handler = new QueueHandler(
            """
            {"MediaContainer":{"Directory":[{"key":"1","type":"movie","title":"Movies"}]}}
            """,
            """
            {"MediaContainer":{"size":1,"totalSize":1,"Metadata":[{
              "ratingKey":"42",
              "title":"Movie",
              "viewCount":2,
              "lastViewedAt":1700000000,
              "Media":[{"Part":[{"file":"/media/Movies/Movie.mp4"}]}]
            }]}}
            """);
        using var client = new PlexClient(new HttpClient(handler));

        IReadOnlyList<PlexWatchStateItem> items = await client.GetWatchStateItemsAsync(
            "http://plex:32400",
            "secret-token",
            CancellationToken.None);

        PlexWatchStateItem item = Assert.Single(items);
        Assert.Equal("42", item.RatingKey);
        Assert.True(item.Played);
        Assert.Equal("/media/Movies/Movie.mp4", Assert.Single(item.Paths));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), item.LastViewedAt);
        Assert.All(handler.Requests, request => Assert.Equal("secret-token", request.Token));
    }

    [Fact]
    public async Task GetWatchStateItemsAsync_RejectsMissingToken()
    {
        using var client = new PlexClient(new HttpClient(new QueueHandler()));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetWatchStateItemsAsync(
                "http://plex:32400",
                string.Empty,
                CancellationToken.None));

        Assert.Contains("token", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHomeUsersAsync_ParsesPlexHomeProfiles()
    {
        var handler = new QueueHandler(
            """
            {"users":[
              {"uuid":"owner","title":"Owner","protected":false},
              {"uuid":"parent","title":"Parent","protected":true}
            ]}
            """,
            """<MediaContainer machineIdentifier="machine" />""",
            """<MediaContainer />""");
        using var client = new PlexClient(new HttpClient(handler));

        IReadOnlyList<PlexUserOptionDto> users = await client.GetAvailableUsersAsync(
            "http://plex:32400",
            "admin-token",
            CancellationToken.None);

        Assert.Collection(
            users,
            user => { Assert.Equal("home:owner", user.Id); Assert.False(user.IsProtected); },
            user => { Assert.Equal("home:parent", user.Id); Assert.True(user.IsProtected); });
        Assert.Equal("/api/v2/home/users", handler.Requests[0].Uri?.AbsolutePath);
        Assert.Equal("home:owner", users[0].Id);
    }

    [Fact]
    public async Task GetHomeUserTokenAsync_SwitchesToSelectedProfile()
    {
        var handler = new QueueHandler("""{"authToken":"parent-token"}""");
        using var client = new PlexClient(new HttpClient(handler));

        string token = await client.GetUserTokenAsync("http://plex:32400", "admin-token", "home:parent", CancellationToken.None);

        Assert.Equal("parent-token", token);
        (Uri? uri, string? requestToken, HttpMethod method) = Assert.Single(handler.Requests);
        Assert.Equal("admin-token", requestToken);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("/api/v2/home/users/parent/switch", uri?.AbsolutePath);
    }

    [Fact]
    public async Task GetUserTokenAsync_UsesServerScopedTokenForSharedAccount()
    {
        var handler = new QueueHandler(
            """<MediaContainer machineIdentifier="machine" />""",
            """<MediaContainer><SharedServer userID="42" username="dlwi4" accessToken="shared-token" /></MediaContainer>""");
        using var client = new PlexClient(new HttpClient(handler));

        string token = await client.GetUserTokenAsync("http://plex:32400", "admin-token", "shared:42", CancellationToken.None);

        Assert.Equal("shared-token", token);
        Assert.Equal("/identity", handler.Requests[0].Uri?.AbsolutePath);
        Assert.Equal("/api/servers/machine/shared_servers", handler.Requests[1].Uri?.AbsolutePath);
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public QueueHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public List<(Uri? Uri, string? Token, HttpMethod Method)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.TryGetValues("X-Plex-Token", out IEnumerable<string>? values);
            Requests.Add((request.RequestUri, values?.SingleOrDefault(), request.Method));
            if (_responses.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue())
            });
        }
    }
}
