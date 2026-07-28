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

mkdir -p \
    "$SCRIPT_DIR/state/jellyfin-config/plugins/Watch State Sync_0.1.0.0" \
    "$SCRIPT_DIR/state/jellyfin-cache" \
    "$SCRIPT_DIR/state/plex-config" \
    "$SCRIPT_DIR/state/plex-transcode"

dotnet build "$REPO_DIR/Jellyfin.Plugin.WatchStateSync.csproj"
cp \
    "$REPO_DIR/bin/Debug/net9.0/Jellyfin.Plugin.WatchStateSync.dll" \
    "$SCRIPT_DIR/state/jellyfin-config/plugins/Watch State Sync_0.1.0.0/Jellyfin.Plugin.WatchStateSync.dll"
"$SCRIPT_DIR/generate-media.sh"

docker compose --env-file "$SCRIPT_DIR/.env.example" \
    --file "$SCRIPT_DIR/compose.yml" \
    up --detach --force-recreate

"$SCRIPT_DIR/wait-for-servers.sh"
"$SCRIPT_DIR/bootstrap-jellyfin.sh"
"$SCRIPT_DIR/bootstrap-plex.sh"

echo "Watch State Sync fixture servers are running"
