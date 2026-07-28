#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
ENV_FILE="$SCRIPT_DIR/.env"
PLUGIN_DLL="$SCRIPT_DIR/plugin/Jellyfin.Plugin.WatchStateSync.dll"

if [ ! -f "$ENV_FILE" ]; then
    echo "Missing $ENV_FILE; copy .env.example and set a development password first" >&2
    exit 1
fi
if [ ! -f "$PLUGIN_DLL" ]; then
    echo "Missing deployed plugin at $PLUGIN_DLL" >&2
    exit 1
fi

set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

export WATCH_STATE_SYNC_SKIP_BUILD=true
export WATCH_STATE_SYNC_PLUGIN_DLL="$PLUGIN_DLL"

"$REPO_DIR/tests/e2e/up.sh"
"$REPO_DIR/tests/e2e/smoke.sh"

cat <<EOF

Cortana watch-state-sync development stack is ready.

Jellyfin: http://localhost:${JELLYFIN_PORT:-18096}
  user: ${JELLYFIN_USERNAME}
  password: ${JELLYFIN_PASSWORD}

Plex:     http://localhost:${PLEX_PORT:-32410}/web

Plex does not have local users. Sign into Plex Web with the disposable Plex
account you want to use, claim this server, then put that account's PLEX_TOKEN
in $ENV_FILE before exercising authenticated watch-state sync.
EOF
