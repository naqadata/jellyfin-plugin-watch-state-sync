#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
ENV_FILE="$SCRIPT_DIR/.env"

if [ -f "$ENV_FILE" ]; then
    set -a
    # shellcheck disable=SC1090
    source "$ENV_FILE"
    set +a
fi

docker compose \
    --env-file "${ENV_FILE:-$REPO_DIR/tests/e2e/.env.example}" \
    --file "$REPO_DIR/tests/e2e/compose.yml" \
    ps

curl --fail --silent --show-error \
    "http://127.0.0.1:${JELLYFIN_PORT:-18096}/System/Info/Public" \
    | jq '{ServerName,Version,StartupWizardCompleted}'
curl --fail --silent --show-error \
    --header 'Accept: application/json' \
    "http://127.0.0.1:${PLEX_PORT:-32410}/identity" \
    | jq '.MediaContainer | {machineIdentifier,version}'
