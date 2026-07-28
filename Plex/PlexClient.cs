using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;
using Jellyfin.Plugin.WatchStateSync.Migration;
using Jellyfin.Plugin.WatchStateSync.Models;

namespace Jellyfin.Plugin.WatchStateSync.Plex;

/// <summary>
/// Reads token-scoped movie and episode watch state from Plex.
/// </summary>
public sealed class PlexClient : IDisposable
{
    private const int PageSize = 500;
    private static readonly Uri PlexTvBaseUri = new("https://plex.tv/");
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexClient"/> class.
    /// </summary>
    public PlexClient()
        : this(new HttpClient(), true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexClient"/> class with a supplied client.
    /// </summary>
    /// <param name="httpClient">HTTP client.</param>
    public PlexClient(HttpClient httpClient)
        : this(httpClient, false)
    {
    }

    private PlexClient(HttpClient httpClient, bool ownsClient)
    {
        _httpClient = httpClient;
        _ownsClient = ownsClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(2);
    }

    /// <summary>
    /// Reads all movie and episode items visible to a Plex token.
    /// </summary>
    /// <param name="serverUrl">Plex server base URL.</param>
    /// <param name="token">Plex user token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Token-scoped Plex watch state.</returns>
    public async Task<IReadOnlyList<PlexWatchStateItem>> GetWatchStateItemsAsync(
        string serverUrl,
        string token,
        CancellationToken cancellationToken)
    {
        Uri baseUri = ValidateBaseUri(serverUrl);
        EnsureToken(token, "Plex token");

        using JsonDocument sectionsDocument = await GetJsonAsync(
            baseUri,
            "library/sections",
            token,
            null,
            cancellationToken).ConfigureAwait(false);
        JsonElement[] sections = GetArray(sectionsDocument.RootElement, "MediaContainer", "Directory");
        var items = new Dictionary<string, PlexWatchStateItem>(StringComparer.Ordinal);

        foreach (JsonElement section in sections)
        {
            string type = GetString(section, "type");
            string itemType = type switch
            {
                "movie" => "1",
                "show" => "4",
                _ => string.Empty
            };
            if (itemType.Length == 0)
            {
                continue;
            }

            string sectionKey = GetString(section, "key");
            if (sectionKey.Length == 0)
            {
                continue;
            }

            int offset = 0;
            while (true)
            {
                Dictionary<string, string> headers = new(StringComparer.Ordinal)
                {
                    ["X-Plex-Container-Start"] = offset.ToString(CultureInfo.InvariantCulture),
                    ["X-Plex-Container-Size"] = PageSize.ToString(CultureInfo.InvariantCulture)
                };
                string relativeUrl = string.Create(
                    CultureInfo.InvariantCulture,
                    $"library/sections/{Uri.EscapeDataString(sectionKey)}/all?type={itemType}");
                using JsonDocument page = await GetJsonAsync(
                    baseUri,
                    relativeUrl,
                    token,
                    headers,
                    cancellationToken).ConfigureAwait(false);
                JsonElement mediaContainer = GetObject(page.RootElement, "MediaContainer");
                JsonElement[] metadata = GetArray(mediaContainer, "Metadata");

                foreach (JsonElement item in metadata)
                {
                    string ratingKey = GetString(item, "ratingKey");
                    if (ratingKey.Length == 0)
                    {
                        continue;
                    }

                    string[] paths = GetArray(item, "Media")
                        .SelectMany(media => GetArray(media, "Part"))
                        .Select(part => GetString(part, "file"))
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    if (paths.Length == 0)
                    {
                        continue;
                    }

                    int viewCount = GetInt32(item, "viewCount");
                    long lastViewedAt = GetInt64(item, "lastViewedAt");
                    items[ratingKey] = new PlexWatchStateItem(
                        ratingKey,
                        GetString(item, "title"),
                        paths,
                        itemType == "1" ? BaselineMediaType.Movie : BaselineMediaType.Episode,
                        viewCount > 0,
                        lastViewedAt > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(lastViewedAt)
                            : null);
                }

                int totalSize = GetInt32(mediaContainer, "totalSize");
                offset += metadata.Length;
                if (metadata.Length == 0
                    || metadata.Length < PageSize
                    || (totalSize > 0 && offset >= totalSize))
                {
                    break;
                }
            }
        }

        return items.Values
            .OrderBy(i => i.RatingKey, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Lists Plex Home users and separately shared Plex accounts available to the administrator token.
    /// </summary>
    public async Task<IReadOnlyList<PlexUserOptionDto>> GetAvailableUsersAsync(
        string serverUrl,
        string adminToken,
        CancellationToken cancellationToken)
    {
        EnsureToken(adminToken, "Plex administrator token");
        Uri serverUri = ValidateBaseUri(serverUrl);
        using JsonDocument document = await GetJsonAsync(
            PlexTvBaseUri,
            "api/v2/home/users",
            adminToken,
            null,
            cancellationToken).ConfigureAwait(false);
        PlexUserOptionDto[] homeUsers = GetArray(document.RootElement, "users")
            .Select(user => new PlexUserOptionDto
            {
                Id = "home:" + GetString(user, "uuid"),
                Name = GetString(user, "title"),
                IsProtected = GetBoolean(user, "protected")
            })
            .Where(user => !string.IsNullOrWhiteSpace(user.Id) && !string.IsNullOrWhiteSpace(user.Name))
            .ToArray();
        PlexUserOptionDto[] sharedUsers = await GetSharedServerUsersAsync(serverUri, adminToken, cancellationToken).ConfigureAwait(false);
        return homeUsers
            .Concat(sharedUsers)
            .OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(user => user.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Resolves a token scoped to a discovered Plex user.
    /// </summary>
    public async Task<string> GetUserTokenAsync(
        string serverUrl,
        string adminToken,
        string plexUserId,
        CancellationToken cancellationToken)
    {
        EnsureToken(adminToken, "Plex administrator token");
        if (string.IsNullOrWhiteSpace(plexUserId))
        {
            throw new InvalidOperationException("Choose a Plex Home user for every enabled mapping.");
        }

        if (plexUserId.StartsWith("shared:", StringComparison.Ordinal))
        {
            string sharedUserId = plexUserId["shared:".Length..];
            PlexSharedUserToken[] sharedUsers = await GetSharedServerUserTokensAsync(
                ValidateBaseUri(serverUrl),
                adminToken,
                cancellationToken).ConfigureAwait(false);
            string token = sharedUsers.FirstOrDefault(user => user.UserId == sharedUserId)?.AccessToken ?? string.Empty;
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("The selected shared Plex user no longer has access to this server.");
            }

            return token;
        }

        if (!plexUserId.StartsWith("home:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Choose a Plex user from the discovered user list for every enabled mapping.");
        }

        string homeUserId = plexUserId["home:".Length..];
        using JsonDocument document = await SendJsonAsync(
            HttpMethod.Post,
            PlexTvBaseUri,
            string.Create(CultureInfo.InvariantCulture, $"api/v2/home/users/{Uri.EscapeDataString(homeUserId)}/switch"),
            adminToken,
            null,
            cancellationToken).ConfigureAwait(false);
        string userToken = GetString(document.RootElement, "authToken");
        if (string.IsNullOrWhiteSpace(userToken))
        {
            throw new InvalidOperationException("Plex did not return a user token. PIN-protected Plex profiles are not supported yet.");
        }

        return userToken;
    }

    private async Task<PlexUserOptionDto[]> GetSharedServerUsersAsync(
        Uri serverUri,
        string adminToken,
        CancellationToken cancellationToken)
    {
        return (await GetSharedServerUserTokensAsync(serverUri, adminToken, cancellationToken).ConfigureAwait(false))
            .Select(user => new PlexUserOptionDto
            {
                Id = "shared:" + user.UserId,
                Name = user.Name,
                IsProtected = false
            })
            .ToArray();
    }

    private async Task<PlexSharedUserToken[]> GetSharedServerUserTokensAsync(
        Uri serverUri,
        string adminToken,
        CancellationToken cancellationToken)
    {
        string identityXml = await GetTextAsync(serverUri, "identity", adminToken, cancellationToken).ConfigureAwait(false);
        string machineIdentifier = XDocument.Parse(identityXml).Root?.Attribute("machineIdentifier")?.Value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(machineIdentifier))
        {
            throw new InvalidOperationException("Plex did not return a server machine identifier.");
        }

        string sharedXml = await GetTextAsync(
            PlexTvBaseUri,
            string.Create(CultureInfo.InvariantCulture, $"api/servers/{Uri.EscapeDataString(machineIdentifier)}/shared_servers"),
            adminToken,
            cancellationToken).ConfigureAwait(false);
        XDocument document = XDocument.Parse(sharedXml);
        return document.Descendants("SharedServer")
            .Select(element => new PlexSharedUserToken(
                element.Attribute("userID")?.Value ?? string.Empty,
                element.Attribute("username")?.Value ?? string.Empty,
                element.Attribute("accessToken")?.Value ?? string.Empty))
            .Where(user => !string.IsNullOrWhiteSpace(user.UserId)
                && !string.IsNullOrWhiteSpace(user.Name)
                && !string.IsNullOrWhiteSpace(user.AccessToken))
            .ToArray();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<JsonDocument> GetJsonAsync(
        Uri baseUri,
        string relativeUrl,
        string token,
        IReadOnlyDictionary<string, string>? additionalHeaders,
        CancellationToken cancellationToken)
    {
        return await SendJsonAsync(HttpMethod.Get, baseUri, relativeUrl, token, additionalHeaders, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        Uri baseUri,
        string relativeUrl,
        string token,
        IReadOnlyDictionary<string, string>? additionalHeaders,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseUri, relativeUrl));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Plex-Token", token);
        request.Headers.Add("X-Plex-Product", "Jellyfin Watch State Sync");
        request.Headers.Add("X-Plex-Version", "0.1.0");
        request.Headers.Add("X-Plex-Client-Identifier", "jellyfin-watch-state-sync");
        if (additionalHeaders is not null)
        {
            foreach ((string name, string value) in additionalHeaders)
            {
                request.Headers.Add(name, value);
            }
        }

        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Plex returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) for {request.RequestUri?.AbsolutePath}."));
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetTextAsync(
        Uri baseUri,
        string relativeUrl,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativeUrl));
        request.Headers.Add("X-Plex-Token", token);
        request.Headers.Add("X-Plex-Product", "Jellyfin Watch State Sync");
        request.Headers.Add("X-Plex-Version", "0.1.0");
        request.Headers.Add("X-Plex-Client-Identifier", "jellyfin-watch-state-sync");
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Plex returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) for {request.RequestUri?.AbsolutePath}."));
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureToken(string token, string description)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"A {description.ToLowerInvariant()} is required."));
        }
    }

    private static Uri ValidateBaseUri(string serverUrl)
    {
        if (!Uri.TryCreate(serverUrl?.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Configure a valid HTTP or HTTPS Plex server URL.");
        }

        string normalized = uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri.AbsoluteUri
            : uri.AbsoluteUri + "/";
        return new Uri(normalized, UriKind.Absolute);
    }

    private static JsonElement GetObject(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.Object
                ? value
                : default;
    }

    private static JsonElement[] GetArray(JsonElement element, params string[] properties)
    {
        foreach (string property in properties)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(property, out element))
            {
                return [];
            }
        }

        return element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().ToArray()
            : [];
    }

    private static string GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out JsonElement value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    private static bool GetBoolean(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.True;
    }

    private sealed record PlexSharedUserToken(string UserId, string Name, string AccessToken);

    private static int GetInt32(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out JsonElement value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int result) => result,
            JsonValueKind.String when int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result) => result,
            _ => 0
        };
    }

    private static long GetInt64(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out JsonElement value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out long result) => result,
            JsonValueKind.String when long.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long result) => result,
            _ => 0
        };
    }
}
