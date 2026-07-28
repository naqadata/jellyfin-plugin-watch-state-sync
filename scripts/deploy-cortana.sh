#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
CORTANA_SSH_HOST="${CORTANA_SSH_HOST:-cortana}"
CORTANA_SSH_USER="${CORTANA_SSH_USER:-paul witt}"
CORTANA_WSL_DISTRO="${CORTANA_WSL_DISTRO:-Ubuntu-24.04}"
REMOTE_DIR="${CORTANA_REMOTE_DIR:-/opt/watch-state-sync-dev}"
SSH_KEY="${CORTANA_SSH_KEY:-$HOME/.ssh/id_ed25519}"
SSH_ARGS=(
    -o BatchMode=yes
    -o IdentitiesOnly=yes
    -i "$SSH_KEY"
    -l "$CORTANA_SSH_USER"
    "$CORTANA_SSH_HOST"
)

dotnet build \
    --configuration Release \
    "$REPO_DIR/Jellyfin.Plugin.WatchStateSync.csproj"

ssh "${SSH_ARGS[@]}" \
    "wsl.exe -d $CORTANA_WSL_DISTRO -u root -- mkdir -p $REMOTE_DIR"
git -C "$REPO_DIR" archive HEAD \
    | ssh "${SSH_ARGS[@]}" \
        "wsl.exe -d $CORTANA_WSL_DISTRO -u root -- tar -x -C $REMOTE_DIR"

ssh "${SSH_ARGS[@]}" \
    "wsl.exe -d $CORTANA_WSL_DISTRO -u root -- mkdir -p $REMOTE_DIR/deploy/cortana/plugin"
ssh "${SSH_ARGS[@]}" \
    "wsl.exe -d $CORTANA_WSL_DISTRO -u root -- install -m 644 /dev/stdin $REMOTE_DIR/deploy/cortana/plugin/Jellyfin.Plugin.WatchStateSync.dll" \
    <"$REPO_DIR/bin/Release/net9.0/Jellyfin.Plugin.WatchStateSync.dll"

if ! ssh "${SSH_ARGS[@]}" \
    "wsl.exe -d $CORTANA_WSL_DISTRO -u root -- test -f $REMOTE_DIR/deploy/cortana/.env"; then
    dev_password="$(
        LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c 24 || true
    )"
    sed "s/^JELLYFIN_PASSWORD=.*/JELLYFIN_PASSWORD=$dev_password/" \
        "$REPO_DIR/deploy/cortana/.env.example" \
        | ssh "${SSH_ARGS[@]}" \
            "wsl.exe -d $CORTANA_WSL_DISTRO -u root -- install -m 600 /dev/stdin $REMOTE_DIR/deploy/cortana/.env"
fi

ssh "${SSH_ARGS[@]}" \
    "wsl.exe -d $CORTANA_WSL_DISTRO -u root -- bash -lc 'apt-get update -qq && apt-get install -y -qq jq >/dev/null && chown -R paulwitt:paulwitt $REMOTE_DIR'"

ssh "${SSH_ARGS[@]}" \
    "wsl.exe -d $CORTANA_WSL_DISTRO -u paulwitt -- $REMOTE_DIR/deploy/cortana/up.sh"
