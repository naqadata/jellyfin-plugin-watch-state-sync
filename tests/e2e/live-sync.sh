#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
JELLYFIN_URL="${JELLYFIN_URL:-http://127.0.0.1:${JELLYFIN_PORT:-18096}}"
PLEX_URL="${PLEX_URL:-http://127.0.0.1:${PLEX_PORT:-32410}}"
PLEX_TOKEN="${PLEX_TOKEN:-}"
JELLYFIN_USERNAME="${JELLYFIN_USERNAME:-fixture-admin}"
PLUGIN_ID="0df4d4de-503e-4c18-bb5d-b274de7046d3"

if [ -z "$PLEX_TOKEN" ]; then
    echo "PLEX_TOKEN is required for the claimed-server live sync test" >&2
    exit 1
fi

# Reuse the baseline test's verified mapping and media setup, then exercise the
# timestamp-only worker in both directions.
"$SCRIPT_DIR/baseline-migration.sh"

jellyfin_token="$(<"$SCRIPT_DIR/state/jellyfin-token")"
jellyfin_headers=(--header "X-Emby-Token: $jellyfin_token")
plex_headers=(
    --header 'Accept: application/json'
    --header 'X-Plex-Product: Watch State Sync E2E'
    --header 'X-Plex-Client-Identifier: jellyfin-watch-state-sync-e2e'
    --header "X-Plex-Token: $PLEX_TOKEN"
)
jellyfin_user_id="$(curl --fail --silent --show-error "${jellyfin_headers[@]}" "$JELLYFIN_URL/Users" | jq -er --arg username "$JELLYFIN_USERNAME" '.[] | select((.Name // .name) == $username) | (.Id // .id)')"
jellyfin_items="$(curl --fail --silent --show-error "${jellyfin_headers[@]}" "$JELLYFIN_URL/Items?Recursive=true&IncludeItemTypes=Movie,Episode&Fields=Path")"
jellyfin_movie_id="$(jq -er '.Items[] | select(.Path | endswith("/Fixture Movie (2024).mp4")) | .Id' <<<"$jellyfin_items")"
jellyfin_episode_id="$(jq -er '.Items[] | select(.Path | endswith("S01E01 - Pilot.mp4")) | .Id' <<<"$jellyfin_items")"
plex_sections="$(curl --fail --silent --show-error "${plex_headers[@]}" "$PLEX_URL/library/sections")"
plex_movie_section="$(jq -er '.MediaContainer.Directory[] | select(.type == "movie") | .key' <<<"$plex_sections")"
plex_show_section="$(jq -er '.MediaContainer.Directory[] | select(.type == "show") | .key' <<<"$plex_sections")"
plex_movie_key="$(curl --fail --silent --show-error "${plex_headers[@]}" "$PLEX_URL/library/sections/$plex_movie_section/all?type=1" | jq -er '.MediaContainer.Metadata[] | select(.Media[].Part[].file | endswith("/Fixture Movie (2024).mp4")) | .ratingKey')"
plex_episode_key="$(curl --fail --silent --show-error "${plex_headers[@]}" "$PLEX_URL/library/sections/$plex_show_section/all?type=4" | jq -er '.MediaContainer.Metadata[] | select(.Media[].Part[].file | endswith("S01E01 - Pilot.mp4")) | .ratingKey')"

config="$(curl --fail --silent --show-error "${jellyfin_headers[@]}" "$JELLYFIN_URL/Plugins/$PLUGIN_ID/Configuration")"
updated_config="$(jq '.EnableLiveSync = true | .PollIntervalSeconds = 30' <<<"$config")"
curl --fail --silent --show-error --request POST --header 'Content-Type: application/json' "${jellyfin_headers[@]}" --data "$updated_config" "$JELLYFIN_URL/Plugins/$PLUGIN_ID/Configuration" >/dev/null

episode_is_played_in_jellyfin() {
    curl --fail --silent --show-error "${jellyfin_headers[@]}" "$JELLYFIN_URL/Users/$jellyfin_user_id/Items/$jellyfin_episode_id" \
        | jq -e '.UserData.Played == true and .UserData.LastPlayedDate != null' >/dev/null
}

movie_is_played_in_plex() {
    curl --fail --silent --show-error "${plex_headers[@]}" "$PLEX_URL/library/metadata/$plex_movie_key" \
        | jq -e '.MediaContainer.Metadata[0].viewCount > 0 and .MediaContainer.Metadata[0].lastViewedAt != null' >/dev/null
}

wait_for() {
    local description="$1"
    local check="$2"
    local deadline=$((SECONDS + 75))
    while [ "$SECONDS" -lt "$deadline" ]; do
        if "$check"; then
            echo "$description"
            return 0
        fi
        sleep 2
    done
    echo "Timed out waiting for $description" >&2
    return 1
}

# Plex records a new completion; Jellyfin must receive the completed state and
# the Plex timestamp through the worker.
curl --fail --silent --show-error --request GET "${plex_headers[@]}" --get \
    --data-urlencode "key=$plex_episode_key" --data-urlencode 'identifier=com.plexapp.plugins.library' \
    "$PLEX_URL/:/scrobble" >/dev/null
wait_for "Plex completion synchronized to Jellyfin" episode_is_played_in_jellyfin

# Clear Plex and replay only on Jellyfin. A new Jellyfin completion must create
# the Plex watched record; deliberate unwatching itself is intentionally ignored.
curl --fail --silent --show-error --request GET "${plex_headers[@]}" --get \
    --data-urlencode "key=$plex_movie_key" --data-urlencode 'identifier=com.plexapp.plugins.library' \
    "$PLEX_URL/:/unscrobble" >/dev/null
curl --fail --silent --show-error --request DELETE "${jellyfin_headers[@]}" \
    "$JELLYFIN_URL/Users/$jellyfin_user_id/PlayedItems/$jellyfin_movie_id" >/dev/null
curl --fail --silent --show-error --request POST "${jellyfin_headers[@]}" \
    "$JELLYFIN_URL/Users/$jellyfin_user_id/PlayedItems/$jellyfin_movie_id" >/dev/null
wait_for "Jellyfin completion synchronized to Plex" movie_is_played_in_plex

echo "Live sync propagated timestamp-backed completed views in both directions"
