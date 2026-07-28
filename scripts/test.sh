#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

dotnet test \
    "$ROOT_DIR/tests/Jellyfin.Plugin.WatchStateSync.Tests/Jellyfin.Plugin.WatchStateSync.Tests.csproj"

if [ "${1:-}" = "--e2e" ]; then
    "$ROOT_DIR/tests/e2e/up.sh"
    "$ROOT_DIR/tests/e2e/smoke.sh"
fi
