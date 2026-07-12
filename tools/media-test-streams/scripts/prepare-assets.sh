#!/usr/bin/env bash
# Prepare the ./assets content the test-stream stack serves.
#
# Usage:  scripts/prepare-assets.sh <input-video>
#
# <input-video> is your own test clip. Prefer real footage with visible speech
# (lip-sync moments make A/V drift visible; synthetic patterns hide it) and,
# for the multichannel lanes, a source with 6 or 8 audio channels. Everything
# is derived from this one file.
#
# Needs ffmpeg/ffprobe on PATH. The LPCM lane needs a build with the
# pcm_bluray encoder and the caption fixture needs python3 (both optional —
# skipped with a warning if unavailable).
set -euo pipefail

IN="${1:?usage: prepare-assets.sh <input-video>}"
HERE="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$HERE/assets"
mkdir -p "$OUT"

channels=$(ffprobe -v error -select_streams a:0 -show_entries stream=channels \
    -of default=nw=1:nk=1 "$IN" 2>/dev/null || echo 0)
echo "== input: $IN (audio channels: $channels)"

# Canonical live mezzanine: fixed 2 s keyframe grid so live joins land a
# decodable frame within ~2 s. Publishers -c copy from this; encode once here.
echo "== mezzanine.mp4 (2s GOP)"
ffmpeg -y -hide_banner -loglevel warning -i "$IN" \
    -c:v libx264 -crf 18 -g 48 -keyint_min 48 -sc_threshold 0 -r 24 \
    -c:a aac -b:a 384k \
    "$OUT/mezzanine.mp4"

echo "== silent.mp4 (video + silent stereo)"
ffmpeg -y -hide_banner -loglevel warning -i "$OUT/mezzanine.mp4" \
    -f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000 \
    -map 0:v -map 1:a -c:v copy -c:a aac -shortest \
    "$OUT/silent.mp4"

# Adversarial long-GOP variant: joins mid-GOP wait up to ~10 s for an IDR.
echo "== slowjoin.mp4 (10s GOP)"
ffmpeg -y -hide_banner -loglevel warning -i "$IN" \
    -c:v libx264 -crf 18 -g 240 -keyint_min 240 -sc_threshold 0 -r 24 \
    -c:a aac -b:a 384k \
    "$OUT/slowjoin.mp4"

echo "== hls/ (HLS VOD packaging)"
mkdir -p "$OUT/hls"
ffmpeg -y -hide_banner -loglevel warning -i "$OUT/mezzanine.mp4" \
    -c copy -f hls -hls_time 4 -hls_playlist_type vod \
    -hls_segment_filename "$OUT/hls/seg%04d.ts" \
    "$OUT/hls/index.m3u8"

# Split-stream pair (video-only + audio-only) for the AudioUri lane.
echo "== videoonly.mp4 / audioonly.mp4"
ffmpeg -y -hide_banner -loglevel warning -i "$OUT/mezzanine.mp4" \
    -map 0:v -c copy -an "$OUT/videoonly.mp4"
ffmpeg -y -hide_banner -loglevel warning -i "$OUT/mezzanine.mp4" \
    -map 0:a -c copy -vn "$OUT/audioonly.mp4"

if [ "$channels" -ge 6 ]; then
    # LPCM over M2TS: the full-multichannel lane (AAC on Windows caps at 5.1;
    # LPCM plays all 8 lanes discretely).
    if ffmpeg -hide_banner -encoders 2>/dev/null | grep -q pcm_bluray; then
        echo "== lpcm.m2ts (LPCM ${channels}ch over M2TS)"
        ffmpeg -y -hide_banner -loglevel warning -i "$IN" \
            -c:v libx264 -crf 18 -g 48 -keyint_min 48 -sc_threshold 0 -r 24 \
            -c:a pcm_bluray -ar 48000 \
            -f mpegts "$OUT/lpcm.m2ts"
    else
        echo "!! skipping lpcm.m2ts: this ffmpeg lacks the pcm_bluray encoder (use a full build)"
    fi

    echo "== w${channels}.wav (multichannel WAV, 24-bit)"
    ffmpeg -y -hide_banner -loglevel warning -i "$IN" \
        -map 0:a -c:a pcm_s24le -ar 48000 -vn "$OUT/w${channels}.wav"
else
    echo "!! input has <6 audio channels: skipping LPCM/multichannel-WAV lanes"
    echo "== wav (stereo, 24-bit)"
    ffmpeg -y -hide_banner -loglevel warning -i "$IN" \
        -map 0:a -c:a pcm_s24le -ar 48000 -vn "$OUT/stereo.wav"
fi

if command -v python3 >/dev/null 2>&1; then
    echo "== test_cc.ts (CEA-608 caption fixture)"
    python3 "$HERE/scripts/gen_cc_ts.py" "$OUT/test_cc.ts"
else
    echo "!! skipping test_cc.ts: python3 not found"
fi

echo "== done. assets in $OUT:"
ls -lh "$OUT"
