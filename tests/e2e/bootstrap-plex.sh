#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PLEX_URL="${PLEX_URL:-http://127.0.0.1:${PLEX_PORT:-32410}}"
PLEX_TOKEN="${PLEX_TOKEN:-}"
CLIENT_ID="watch-state-sync-e2e"

headers=(
    --header 'Accept: application/json'
    --header 'X-Plex-Product: Watch State Sync E2E'
    --header 'X-Plex-Version: 0.1.0'
    --header "X-Plex-Client-Identifier: $CLIENT_ID"
)
if [ -n "$PLEX_TOKEN" ]; then
    headers+=(--header "X-Plex-Token: $PLEX_TOKEN")
fi

get_sections() {
    curl --fail --silent --show-error \
        "${headers[@]}" \
        "$PLEX_URL/library/sections"
}

add_library() {
    local name="$1"
    local type="$2"
    local agent="$3"
    local scanner="$4"
    local location="$5"
    local sections

    sections="$(get_sections)"

    if jq -e --arg name "$name" '.MediaContainer.Directory[]? | select(.title == $name)' <<<"$sections" >/dev/null; then
        return
    fi

    local response_file
    local started_at
    local status
    response_file="$(mktemp)"
    started_at="$(date +%s)"

    while true; do
        status="$(
            curl --silent --show-error \
                --output "$response_file" \
                --write-out '%{http_code}' \
                --request POST \
                --get \
                "${headers[@]}" \
                --data-urlencode "name=$name" \
                --data-urlencode "type=$type" \
                --data-urlencode "agent=$agent" \
                --data-urlencode "scanner=$scanner" \
                --data-urlencode 'language=en-US' \
                --data-urlencode "location=$location" \
                "$PLEX_URL/library/sections"
        )"

        sections="$(get_sections)"
        if jq -e --arg name "$name" '.MediaContainer.Directory[]? | select(.title == $name)' <<<"$sections" >/dev/null; then
            rm -f "$response_file"
            break
        fi

        if [ "$status" = "200" ] || [ "$status" = "201" ]; then
            rm -f "$response_file"
            break
        fi

        if [ "$status" != "400" ] || [ "$(( $(date +%s) - started_at ))" -ge 60 ]; then
            echo "Plex library creation failed with HTTP $status:" >&2
            cat "$response_file" >&2
            rm -f "$response_file"
            exit 1
        fi

        sleep 2
    done
}

add_library "Fixture Movies" "movie" "tv.plex.agents.movie" "Plex Movie" "/media/Movies"
add_library "Fixture Shows" "show" "tv.plex.agents.series" "Plex TV Series" "/media/Shows"

curl --fail --silent --show-error \
    --request GET \
    "${headers[@]}" \
    "$PLEX_URL/library/sections/all/refresh" >/dev/null

touch "$SCRIPT_DIR/state/plex-bootstrap-complete"
echo "Plex fixture libraries are configured"
