#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"

set -a
# shellcheck disable=SC1091
source "$SCRIPT_DIR/.env"
set +a

exec "$REPO_DIR/tests/e2e/baseline-migration.sh"
