#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT_DIR/Jellyfin.Plugin.WatchStateSync.csproj"
PLUGIN_DLL="Jellyfin.Plugin.WatchStateSync.dll"
MANIFEST="$ROOT_DIR/manifest.json"
TARGET_ABI="10.11.0.0"
RAW_BASE="https://raw.githubusercontent.com/naqadata/jellyfin-plugin-watch-state-sync/main/dist"
CHANGELOG="${1:-Initial release with manual baseline migration and opt-in completed-view sync.}"

require_tool() {
    command -v "$1" >/dev/null 2>&1 || { echo "Missing required tool: $1" >&2; exit 1; }
}

require_tool dotnet
require_tool jq
require_tool zip

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJECT" | head -n 1)"
if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Expected a four-part plugin version in $PROJECT, got: $version" >&2
    exit 1
fi

package_name="Jellyfin.Plugin.WatchStateSync_${version}.zip"
package_path="$ROOT_DIR/dist/$package_name"
source_url="$RAW_BASE/$package_name"
timestamp="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"

if [ -e "$package_path" ] || jq -e --arg version "$version" '.[0].versions[] | select(.version == $version)' "$MANIFEST" >/dev/null; then
    echo "Release artifact or manifest version $version already exists; bump the plugin version first." >&2
    exit 1
fi

rm -rf "$ROOT_DIR/package"
mkdir -p "$ROOT_DIR/package" "$ROOT_DIR/dist"
dotnet build --configuration Release "$PROJECT"
cp "$ROOT_DIR/bin/Release/net9.0/$PLUGIN_DLL" "$ROOT_DIR/package/$PLUGIN_DLL"

(
    cd "$ROOT_DIR/package"
    zip -X -9 "../dist/$package_name" "$PLUGIN_DLL"
)

if command -v md5sum >/dev/null 2>&1; then
    checksum="$(md5sum "$package_path" | awk '{print $1}')"
else
    checksum="$(md5 -q "$package_path")"
fi

tmp_manifest="$(mktemp)"
jq \
    --arg version "$version" \
    --arg changelog "$CHANGELOG" \
    --arg targetAbi "$TARGET_ABI" \
    --arg sourceUrl "$source_url" \
    --arg checksum "$checksum" \
    --arg timestamp "$timestamp" \
    '.[0].versions += [{version: $version, changelog: $changelog, targetAbi: $targetAbi, sourceUrl: $sourceUrl, checksum: $checksum, timestamp: $timestamp}]' \
    "$MANIFEST" >"$tmp_manifest"
mv "$tmp_manifest" "$MANIFEST"

echo "Wrote $package_path"
echo "MD5 $checksum"
