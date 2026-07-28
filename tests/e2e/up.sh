#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"

if [ -f "$SCRIPT_DIR/.env" ]; then
    set -a
    # shellcheck disable=SC1091
    source "$SCRIPT_DIR/.env"
    set +a
fi

PLUGIN_VERSION="0.3.0.1"
PLUGIN_DIRECTORY="$SCRIPT_DIR/state/jellyfin-config/plugins/Watch State Sync_$PLUGIN_VERSION"

# A versioned plugin directory is Jellyfin's cache key for embedded configuration
# pages. Remove only this fixture's previous plugin directory before installing.
rm -rf "$SCRIPT_DIR/state/jellyfin-config/plugins/Watch State Sync_0.1.0.0"
mkdir -p \
    "$PLUGIN_DIRECTORY" \
    "$SCRIPT_DIR/state/jellyfin-cache" \
    "$SCRIPT_DIR/state/plex-config" \
    "$SCRIPT_DIR/state/plex-transcode"

PLUGIN_DLL="${WATCH_STATE_SYNC_PLUGIN_DLL:-$REPO_DIR/bin/Debug/net9.0/Jellyfin.Plugin.WatchStateSync.dll}"
if [ "${WATCH_STATE_SYNC_SKIP_BUILD:-false}" != "true" ]; then
    dotnet build "$REPO_DIR/Jellyfin.Plugin.WatchStateSync.csproj"
fi
if [ ! -f "$PLUGIN_DLL" ]; then
    echo "Plugin DLL not found: $PLUGIN_DLL" >&2
    exit 1
fi
cp \
    "$PLUGIN_DLL" \
    "$PLUGIN_DIRECTORY/Jellyfin.Plugin.WatchStateSync.dll"
cp "$REPO_DIR/deploy/malcolm/meta.json" "$PLUGIN_DIRECTORY/meta.json"
"$SCRIPT_DIR/generate-media.sh"

docker compose --env-file "$SCRIPT_DIR/.env.example" \
    --file "$SCRIPT_DIR/compose.yml" \
    up --detach --force-recreate

"$SCRIPT_DIR/wait-for-servers.sh"
"$SCRIPT_DIR/bootstrap-jellyfin.sh"
"$SCRIPT_DIR/bootstrap-plex.sh"

echo "Watch State Sync fixture servers are running"
