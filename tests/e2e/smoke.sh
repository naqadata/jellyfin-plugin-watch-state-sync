#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
JELLYFIN_URL="${JELLYFIN_URL:-http://127.0.0.1:${JELLYFIN_PORT:-18096}}"
PLEX_URL="${PLEX_URL:-http://127.0.0.1:${PLEX_PORT:-32410}}"
JELLYFIN_MOVIE_PATH="/media/Movies/Fixture Movie (2024)/Fixture Movie (2024).mp4"
JELLYFIN_EPISODE_PATH="/media/Shows/Fixture Show (2024)/Season 01/Fixture Show (2024) - S01E01 - Pilot.mp4"

"$SCRIPT_DIR/wait-for-servers.sh"

jellyfin_version="$(
    curl --fail --silent --show-error "$JELLYFIN_URL/System/Info/Public" \
        | jq -er '.Version'
)"
plex_identity="$(
    curl --fail --silent --show-error \
        --header 'Accept: application/json' \
        "$PLEX_URL/identity"
)"
plex_version="$(jq -er '.MediaContainer.version' <<<"$plex_identity")"
jellyfin_token="$(<"$SCRIPT_DIR/state/jellyfin-token")"
plex_sections="$(
    curl --fail --silent --show-error \
        --header 'Accept: application/json' \
        "$PLEX_URL/library/sections"
)"
plex_movie_section="$(
    jq -er '[.MediaContainer.Directory[] | select(.title == "Fixture Movies" and .type == "movie")][0].key' \
        <<<"$plex_sections"
)"
plex_show_section="$(
    jq -er '[.MediaContainer.Directory[] | select(.title == "Fixture Shows" and .type == "show")][0].key' \
        <<<"$plex_sections"
)"

if ! docker compose --env-file "$SCRIPT_DIR/.env.example" \
    --file "$SCRIPT_DIR/compose.yml" \
    logs jellyfin \
    | grep -F 'Loaded plugin: Watch State Sync' >/dev/null; then
    echo "Jellyfin started, but Watch State Sync was not loaded" >&2
    exit 1
fi

catalog_started_at="$(date +%s)"
while true; do
    jellyfin_paths="$(
        curl --fail --silent --show-error \
            --header "X-Emby-Token: $jellyfin_token" \
            "$JELLYFIN_URL/Items?Recursive=true&IncludeItemTypes=Movie,Episode&Fields=Path" \
            | jq -r '.Items[]?.Path'
    )"
    plex_paths="$(
        {
            curl --fail --silent --show-error \
                --header 'Accept: application/json' \
                "$PLEX_URL/library/sections/$plex_movie_section/all"
            curl --fail --silent --show-error \
                --header 'Accept: application/json' \
                "$PLEX_URL/library/sections/$plex_show_section/all?type=4"
        } | jq -r '.MediaContainer.Metadata[]?.Media[]?.Part[]?.file'
    )"

    if grep -Fx "$JELLYFIN_MOVIE_PATH" <<<"$jellyfin_paths" >/dev/null \
        && grep -Fx "$JELLYFIN_EPISODE_PATH" <<<"$jellyfin_paths" >/dev/null \
        && grep -Fx "$JELLYFIN_MOVIE_PATH" <<<"$plex_paths" >/dev/null \
        && grep -Fx "$JELLYFIN_EPISODE_PATH" <<<"$plex_paths" >/dev/null; then
        break
    fi

    if [ "$(( $(date +%s) - catalog_started_at ))" -ge 90 ]; then
        echo "Timed out waiting for both fixture catalogs to index the shared paths" >&2
        exit 1
    fi
    sleep 2
done

echo "Jellyfin $jellyfin_version loaded Watch State Sync"
echo "Plex $plex_version is reachable"
echo "Both servers indexed the same fixture movie and episode paths"
