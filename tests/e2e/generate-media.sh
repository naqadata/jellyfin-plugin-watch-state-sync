#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MEDIA_DIR="$SCRIPT_DIR/media"
JELLYFIN_IMAGE="jellyfin/jellyfin:10.11.6"

mkdir -p \
    "$MEDIA_DIR/Movies/Fixture Movie (2024)" \
    "$MEDIA_DIR/Shows/Fixture Show (2024)/Season 01"

generate_video() {
    local output="$1"
    local color="$2"
    local frequency="$3"

    if [ -s "$output" ]; then
        return
    fi

    local relative_output="${output#"$MEDIA_DIR"/}"
    docker run --rm \
        --entrypoint /usr/lib/jellyfin-ffmpeg/ffmpeg \
        --volume "$MEDIA_DIR:/media" \
        "$JELLYFIN_IMAGE" \
        -hide_banner \
        -loglevel error \
        -f lavfi \
        -i "color=c=${color}:s=320x180:d=2" \
        -f lavfi \
        -i "sine=frequency=${frequency}:duration=2" \
        -shortest \
        -c:v libx264 \
        -pix_fmt yuv420p \
        -c:a aac \
        -metadata title="Watch State Sync fixture" \
        -y \
        "/media/$relative_output"
}

generate_video \
    "$MEDIA_DIR/Movies/Fixture Movie (2024)/Fixture Movie (2024).mp4" \
    blue \
    440

generate_video \
    "$MEDIA_DIR/Shows/Fixture Show (2024)/Season 01/Fixture Show (2024) - S01E01 - Pilot.mp4" \
    green \
    660

echo "Fixture media is ready under $MEDIA_DIR"
