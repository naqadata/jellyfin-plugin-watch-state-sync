#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
JELLYFIN_URL="${JELLYFIN_URL:-http://127.0.0.1:${JELLYFIN_PORT:-18096}}"
PLEX_URL="${PLEX_URL:-http://127.0.0.1:${PLEX_PORT:-32410}}"
PLEX_TOKEN="${PLEX_TOKEN:-}"
JELLYFIN_USERNAME="${JELLYFIN_USERNAME:-fixture-admin}"
PLUGIN_ID="0df4d4de-503e-4c18-bb5d-b274de7046d3"

if [ -z "$PLEX_TOKEN" ]; then
    echo "PLEX_TOKEN is required for the claimed-server baseline migration test" >&2
    exit 1
fi
if [ ! -f "$SCRIPT_DIR/state/jellyfin-token" ]; then
    echo "Run up.sh before the baseline migration test" >&2
    exit 1
fi

jellyfin_token="$(<"$SCRIPT_DIR/state/jellyfin-token")"
jellyfin_headers=(--header "X-Emby-Token: $jellyfin_token")
plex_headers=(
    --header 'Accept: application/json'
    --header "X-Plex-Token: $PLEX_TOKEN"
)

jellyfin_user_id="$(
    curl --fail --silent --show-error \
        "${jellyfin_headers[@]}" \
        "$JELLYFIN_URL/Users" \
        | jq -er --arg username "$JELLYFIN_USERNAME" \
            '.[] | select((.Name // .name) == $username) | (.Id // .id)'
)"
jellyfin_items="$(
    curl --fail --silent --show-error \
        "${jellyfin_headers[@]}" \
        "$JELLYFIN_URL/Items?Recursive=true&IncludeItemTypes=Movie,Episode&Fields=Path"
)"
jellyfin_movie_id="$(
    jq -er '.Items[] | select(.Path | endswith("/Fixture Movie (2024).mp4")) | .Id' \
        <<<"$jellyfin_items"
)"
jellyfin_episode_id="$(
    jq -er '.Items[] | select(.Path | endswith("S01E01 - Pilot.mp4")) | .Id' \
        <<<"$jellyfin_items"
)"

plex_sections="$(
    curl --fail --silent --show-error \
        "${plex_headers[@]}" \
        "$PLEX_URL/library/sections"
)"
plex_movie_section="$(
    jq -er '.MediaContainer.Directory[] | select(.type == "movie") | .key' \
        <<<"$plex_sections"
)"
plex_show_section="$(
    jq -er '.MediaContainer.Directory[] | select(.type == "show") | .key' \
        <<<"$plex_sections"
)"
plex_movie_key="$(
    curl --fail --silent --show-error \
        "${plex_headers[@]}" \
        "$PLEX_URL/library/sections/$plex_movie_section/all?type=1" \
        | jq -er '.MediaContainer.Metadata[] | select(.Media[].Part[].file | endswith("/Fixture Movie (2024).mp4")) | .ratingKey'
)"
plex_episode_key="$(
    curl --fail --silent --show-error \
        "${plex_headers[@]}" \
        "$PLEX_URL/library/sections/$plex_show_section/all?type=4" \
        | jq -er '.MediaContainer.Metadata[] | select(.Media[].Part[].file | endswith("S01E01 - Pilot.mp4")) | .ratingKey'
)"

# Establish opposite destination states so the baseline must perform one write
# in each direction of the watched boolean.
curl --fail --silent --show-error \
    --request GET \
    "${plex_headers[@]}" \
    --get \
    --data-urlencode "key=$plex_movie_key" \
    --data-urlencode 'identifier=com.plexapp.plugins.library' \
    "$PLEX_URL/:/scrobble" >/dev/null
curl --fail --silent --show-error \
    --request GET \
    "${plex_headers[@]}" \
    --get \
    --data-urlencode "key=$plex_episode_key" \
    --data-urlencode 'identifier=com.plexapp.plugins.library' \
    "$PLEX_URL/:/unscrobble" >/dev/null
curl --fail --silent --show-error \
    --request DELETE \
    "${jellyfin_headers[@]}" \
    "$JELLYFIN_URL/Users/$jellyfin_user_id/PlayedItems/$jellyfin_movie_id" >/dev/null
curl --fail --silent --show-error \
    --request POST \
    "${jellyfin_headers[@]}" \
    "$JELLYFIN_URL/Users/$jellyfin_user_id/PlayedItems/$jellyfin_episode_id" >/dev/null

plugin_configuration="$(
    curl --fail --silent --show-error \
        "${jellyfin_headers[@]}" \
        "$JELLYFIN_URL/Plugins/$PLUGIN_ID/Configuration"
)"
updated_configuration="$(
    jq \
        --arg plexUrl "http://plex:32400" \
        --arg jellyfinUserId "$jellyfin_user_id" \
        --arg jellyfinUsername "$JELLYFIN_USERNAME" \
        --arg plexUsername "${PLEX_USERNAME:-$JELLYFIN_USERNAME}" \
        --arg plexToken "$PLEX_TOKEN" \
        '.PlexServerUrl = $plexUrl
         | .EnableLiveSync = false
         | .UserMappings = [{
             JellyfinUserId: $jellyfinUserId,
             JellyfinUsername: $jellyfinUsername,
             PlexUserId: $plexUsername,
             PlexUsername: $plexUsername,
             PlexToken: $plexToken,
             Enabled: true
         }]' \
        <<<"$plugin_configuration"
)"
curl --fail --silent --show-error \
    --request POST \
    --header 'Content-Type: application/json' \
    "${jellyfin_headers[@]}" \
    --data "$updated_configuration" \
    "$JELLYFIN_URL/Plugins/$PLUGIN_ID/Configuration" >/dev/null

preview="$(
    curl --fail --silent --show-error \
        --request POST \
        --header 'Content-Type: application/json' \
        "${jellyfin_headers[@]}" \
        --data '{}' \
        "$JELLYFIN_URL/WatchStateSync/Admin/Baseline/Preview"
)"
jq -e '
    .Summary.Matched == 2
    and .Summary.MarkWatched == 1
    and .Summary.MarkUnwatched == 1
    and .Summary.Ambiguous == 0
' <<<"$preview" >/dev/null
preview_id="$(jq -er '.PreviewId' <<<"$preview")"

apply_result="$(
    curl --fail --silent --show-error \
        --request POST \
        --header 'Content-Type: application/json' \
        "${jellyfin_headers[@]}" \
        --data "$(jq -n --arg previewId "$preview_id" '{PreviewId:$previewId}')" \
        "$JELLYFIN_URL/WatchStateSync/Admin/Baseline/Apply"
)"
jq -e '.Attempted == 2 and .Applied == 2 and .Failed == 0 and .Cancelled == false' \
    <<<"$apply_result" >/dev/null

movie_after="$(
    curl --fail --silent --show-error \
        "${jellyfin_headers[@]}" \
        "$JELLYFIN_URL/Users/$jellyfin_user_id/Items/$jellyfin_movie_id"
)"
episode_after="$(
    curl --fail --silent --show-error \
        "${jellyfin_headers[@]}" \
        "$JELLYFIN_URL/Users/$jellyfin_user_id/Items/$jellyfin_episode_id"
)"
jq -e '.UserData.Played == true' <<<"$movie_after" >/dev/null
jq -e '.UserData.Played == false' <<<"$episode_after" >/dev/null

idempotency_preview="$(
    curl --fail --silent --show-error \
        --request POST \
        --header 'Content-Type: application/json' \
        "${jellyfin_headers[@]}" \
        --data '{}' \
        "$JELLYFIN_URL/WatchStateSync/Admin/Baseline/Preview"
)"
jq -e '.Summary.MarkWatched == 0 and .Summary.MarkUnwatched == 0 and .Summary.NoChange == 2' \
    <<<"$idempotency_preview" >/dev/null

audits="$(
    curl --fail --silent --show-error \
        "${jellyfin_headers[@]}" \
        "$JELLYFIN_URL/WatchStateSync/Admin/Baseline/Audits?limit=1"
)"
jq -e 'length == 1 and .[0].Applied == 2 and .[0].Failed == 0' <<<"$audits" >/dev/null

echo "Manual baseline migration applied both watched-state directions"
echo "A second dry run proposed no changes"
echo "The durable apply audit is available through the plugin API"
