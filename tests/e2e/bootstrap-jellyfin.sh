#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
JELLYFIN_URL="${JELLYFIN_URL:-http://127.0.0.1:${JELLYFIN_PORT:-18096}}"
JELLYFIN_USERNAME="${JELLYFIN_USERNAME:-fixture-admin}"
JELLYFIN_PASSWORD="${JELLYFIN_PASSWORD:-fixture-password}"
JELLYFIN_ENABLE_REMOTE_ACCESS="${JELLYFIN_ENABLE_REMOTE_ACCESS:-false}"
JELLYFIN_SERVER_NAME="${JELLYFIN_SERVER_NAME:-Watch State Sync Fixture}"
AUTHORIZATION='MediaBrowser Client="WatchStateSyncE2E", Device="DockerFixture", DeviceId="watch-state-sync-e2e", Version="0.1.0"'
TOKEN_FILE="$SCRIPT_DIR/state/jellyfin-token"
CURL_RETRY=(
    --retry 30
    --retry-delay 2
    --retry-all-errors
    --connect-timeout 5
    --max-time 10
)

public_info="$(
    curl --fail --silent --show-error \
        "${CURL_RETRY[@]}" \
        "$JELLYFIN_URL/System/Info/Public"
)"
if [ "$(jq -r '.StartupWizardCompleted' <<<"$public_info")" != "true" ]; then
    user_ready_started_at="$(date +%s)"
    until curl --fail --silent --show-error "$JELLYFIN_URL/Startup/User" \
        | jq -e '.Name | length > 0' >/dev/null; do
        if [ "$(( $(date +%s) - user_ready_started_at ))" -ge 60 ]; then
            echo "Timed out waiting for Jellyfin's initial user record" >&2
            exit 1
        fi
        sleep 1
    done

    curl --fail --silent --show-error \
        "${CURL_RETRY[@]}" \
        --request POST \
        --header 'Content-Type: application/json' \
        --data "$(jq -n \
            --arg serverName "$JELLYFIN_SERVER_NAME" \
            '{UICulture:"en-US",MetadataCountryCode:"US",PreferredMetadataLanguage:"en",ServerName:$serverName}')" \
        "$JELLYFIN_URL/Startup/Configuration" >/dev/null

    curl --fail --silent --show-error \
        "${CURL_RETRY[@]}" \
        --request POST \
        --header 'Content-Type: application/json' \
        --data "$(jq -n --arg name "$JELLYFIN_USERNAME" --arg password "$JELLYFIN_PASSWORD" '{Name:$name,Password:$password}')" \
        "$JELLYFIN_URL/Startup/User" >/dev/null

    curl --fail --silent --show-error \
        "${CURL_RETRY[@]}" \
        --request POST \
        --header 'Content-Type: application/json' \
        --data "$(jq -n \
            --argjson enableRemoteAccess "$JELLYFIN_ENABLE_REMOTE_ACCESS" \
            '{EnableRemoteAccess:$enableRemoteAccess,EnableAutomaticPortMapping:false}')" \
        "$JELLYFIN_URL/Startup/RemoteAccess" >/dev/null

    curl --fail --silent --show-error \
        "${CURL_RETRY[@]}" \
        --request POST \
        "$JELLYFIN_URL/Startup/Complete" >/dev/null
fi

authentication="$(
    curl --fail --silent --show-error \
        "${CURL_RETRY[@]}" \
        --request POST \
        --header 'Content-Type: application/json' \
        --header "Authorization: $AUTHORIZATION" \
        --data "$(jq -n --arg username "$JELLYFIN_USERNAME" --arg password "$JELLYFIN_PASSWORD" '{Username:$username,Pw:$password}')" \
        "$JELLYFIN_URL/Users/AuthenticateByName"
)"
token="$(jq -er '.AccessToken' <<<"$authentication")"
printf '%s' "$token" >"$TOKEN_FILE"
chmod 600 "$TOKEN_FILE"

system_configuration="$(
    curl --fail --silent --show-error \
        "${CURL_RETRY[@]}" \
        --header "X-Emby-Token: $token" \
        "$JELLYFIN_URL/System/Configuration"
)"
if [ "$(jq -r '.ServerName' <<<"$system_configuration")" != "$JELLYFIN_SERVER_NAME" ]; then
    jq --arg serverName "$JELLYFIN_SERVER_NAME" '.ServerName = $serverName' \
        <<<"$system_configuration" \
        | curl --fail --silent --show-error \
            "${CURL_RETRY[@]}" \
            --request POST \
            --header 'Content-Type: application/json' \
            --header "X-Emby-Token: $token" \
            --data-binary @- \
            "$JELLYFIN_URL/System/Configuration" >/dev/null
fi

add_library() {
    local name="$1"
    local collection_type="$2"
    local path="$3"

    if curl --fail --silent --show-error \
        "${CURL_RETRY[@]}" \
        --header "X-Emby-Token: $token" \
        "$JELLYFIN_URL/Library/VirtualFolders" \
        | jq -e --arg name "$name" '.[] | select(.Name == $name)' >/dev/null; then
        return
    fi

    curl --fail --silent --show-error \
        "${CURL_RETRY[@]}" \
        --request POST \
        --header "X-Emby-Token: $token" \
        --get \
        --data-urlencode "name=$name" \
        --data-urlencode "collectionType=$collection_type" \
        --data-urlencode "paths=$path" \
        --data-urlencode 'refreshLibrary=true' \
        "$JELLYFIN_URL/Library/VirtualFolders" >/dev/null
}

add_library "Fixture Movies" "movies" "/media/Movies"
add_library "Fixture Shows" "tvshows" "/media/Shows"

echo "Jellyfin fixture is configured; token stored in ignored fixture state"
