#!/usr/bin/env bash
#
# Build the demux fuzz targets with clang libFuzzer + ASan + UBSan.
# Runs on Linux/CI (clang on PATH) and on Windows via Git Bash (the LLVM
# install). No Unity, no Media Foundation, no fixtures needed to build.
#
#   ./build.sh          # build all targets
#   ./build.sh ts       # build one
#
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
native="$here/../../Basis/Packages/com.basis.mediaplayer/Native~"
out="$here/build"
mkdir -p "$out"

CLANG="${CLANG:-clang}"
if ! command -v "$CLANG" >/dev/null 2>&1; then
    CLANG="/c/Program Files/LLVM/bin/clang.exe"
fi
[ -x "$CLANG" ] || command -v "$CLANG" >/dev/null 2>&1 || { echo "clang not found (set \$CLANG)"; exit 1; }

# UBSan must abort (not recover) so libFuzzer records the input as a crash.
san=(-fsanitize=fuzzer,address,undefined -fno-sanitize-recover=all)
flags=(-g -O1 -fno-omit-frame-pointer -I "$native")

# target -> extra protocol sources beyond its own <name>.c
build_target() {
    local name="$1"; shift
    local srcs=("$here/native/fuzz_${name}.c" "$@")
    echo "building fuzz_${name} ..."
    "$CLANG" "${flags[@]}" "${san[@]}" "${srcs[@]}" -o "$out/fuzz_${name}.exe"
    echo "  -> $out/fuzz_${name}.exe"
}

want="${1:-all}"
case "$want" in
    all|ts|mp4|webm|caption|ogg) ;;
    *) echo "unknown fuzz target: $want (expected: all ts mp4 webm caption ogg)" >&2; exit 2 ;;
esac
if [ "$want" = "all" ] || [ "$want" = "ts" ]; then
    build_target ts \
        "$native/protocol/basis_ts.c" \
        "$native/protocol/basis_bitstream.c" \
        "$native/protocol/basis_caption.c"
fi
if [ "$want" = "all" ] || [ "$want" = "mp4" ]; then
    build_target mp4 \
        "$native/protocol/basis_mp4.c" \
        "$native/protocol/basis_bitstream.c" \
        "$native/protocol/basis_caption.c"
fi
if [ "$want" = "all" ] || [ "$want" = "webm" ]; then
    build_target webm \
        "$native/protocol/basis_webm.c" \
        "$native/protocol/basis_bitstream.c"
fi
if [ "$want" = "all" ] || [ "$want" = "ogg" ]; then
    build_target ogg \
        "$native/protocol/basis_ogg.c"
fi
if [ "$want" = "all" ] || [ "$want" = "caption" ]; then
    build_target caption \
        "$native/protocol/basis_caption.c" \
        "$native/protocol/basis_bitstream.c"
fi

# On Windows the ASan runtime is a DLL that must sit next to the exe (or on
# PATH) at run time. On Linux it is statically linked; nothing to copy.
if [ -d "/c/Program Files/LLVM" ]; then
    rt=$(ls "/c/Program Files/LLVM/lib/clang/"*/lib/windows/clang_rt.asan_dynamic-x86_64.dll 2>/dev/null | head -1 || true)
    [ -n "$rt" ] && cp -f "$rt" "$out/" && echo "staged ASan runtime dll"
fi
