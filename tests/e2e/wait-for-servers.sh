#!/usr/bin/env bash
set -euo pipefail

JELLYFIN_URL="${JELLYFIN_URL:-http://127.0.0.1:${JELLYFIN_PORT:-18096}}"
PLEX_URL="${PLEX_URL:-http://127.0.0.1:${PLEX_PORT:-32410}}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-180}"

wait_for_url() {
    local name="$1"
    local url="$2"
    local started_at
    started_at="$(date +%s)"

    until curl --fail --silent --show-error --max-time 5 "$url" >/dev/null 2>&1; do
        if [ "$(( $(date +%s) - started_at ))" -ge "$TIMEOUT_SECONDS" ]; then
            echo "Timed out waiting for $name at $url" >&2
            exit 1
        fi
        sleep 2
    done

    echo "$name is ready at $url"
}

wait_for_url "Jellyfin" "$JELLYFIN_URL/System/Info/Public"
wait_for_url "Plex" "$PLEX_URL/identity"
