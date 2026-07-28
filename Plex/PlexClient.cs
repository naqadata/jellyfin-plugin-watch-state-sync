using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Jellyfin.Plugin.WatchStateSync.Migration;

namespace Jellyfin.Plugin.WatchStateSync.Plex;

/// <summary>
/// Reads token-scoped movie and episode watch state from Plex.
/// </summary>
public sealed class PlexClient : IDisposable
{
    private const int PageSize = 500;
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
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("A Plex token is required for every enabled user mapping.");
        }

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
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativeUrl));
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
