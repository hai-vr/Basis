#!/usr/bin/env bash
# Build librist as a static library for basis_media_native's RIST transport
# (-DBASIS_WITH_RIST=ON) and stage it into third_party/<rid>/.
#
# librist vendors its own mbedTLS and links it into the archive, so a single
# library per platform is produced; the consumer links one lib (rist).
#
# Usage: build-librist.sh <target>
#   android-arm64   NDK arm64-v8a / android-29 static (needs ANDROID_NDK_ROOT)
#
# Requires: git, meson, ninja. librist is cloned from upstream at the tag
# basis_rist.c targets (override with LIBRIST_REF).
#
# Output: third_party/<rid>/librist.a, third_party/include/librist/*.h
set -euo pipefail

TARGET="${1:-}"
LIBRIST_REF="${LIBRIST_REF:-v0.2.11}"
LIBRIST_REPO="${LIBRIST_REPO:-https://code.videolan.org/rist/librist.git}"

HERE="$(cd "$(dirname "$0")" && pwd)"
TP="$HERE/third_party"
WORK="$HERE/build-librist/$TARGET"

if [ -z "$TARGET" ]; then
    echo "usage: build-librist.sh <target>   (supported: android-arm64)" >&2
    exit 2
fi
for t in git meson ninja; do
    command -v "$t" >/dev/null 2>&1 || { echo "error: $t not on PATH" >&2; exit 1; }
done

rm -rf "$WORK"; mkdir -p "$WORK"
SRC="$WORK/librist"
git clone --depth 1 -b "$LIBRIST_REF" "$LIBRIST_REPO" "$SRC"

case "$TARGET" in
    android-arm64)
        : "${ANDROID_NDK_ROOT:?set ANDROID_NDK_ROOT to your NDK path}"
        TC="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/linux-x86_64/bin"
        CC="$TC/aarch64-linux-android29-clang"
        CXX="$TC/aarch64-linux-android29-clang++"
        CROSS="$WORK/android-arm64.ini"
        cat > "$CROSS" <<EOF
[binaries]
c = '$CC'
cpp = '$CXX'
ar = '$TC/llvm-ar'
strip = '$TC/llvm-strip'
[host_machine]
system = 'android'
cpu_family = 'aarch64'
cpu = 'aarch64'
endian = 'little'
EOF
        # Hardening has to be spelled out here. This archive is built by meson
        # against the cross file above, so it inherits nothing from the plugin's
        # CMake target options and nothing from the NDK's CMake toolchain file —
        # the clang driver on its own defines no _FORTIFY_SOURCE. librist parses
        # the RIST transport's wire bytes and links straight into the client, so
        # it wants the same treatment as the rest of the core.
        #
        # -mbranch-protection additionally has to *match* the plugin's own flag:
        # lld emits the AArch64 BTI/PAC property note only when every input
        # object carries it, so an archive built without it drops BTI from the
        # linked .so. The -U mirrors the plugin build, so a future NDK that
        # starts predefining _FORTIFY_SOURCE cannot turn this into a
        # macro-redefinition warning.
        #
        # The set matches what CMakeLists.txt applies to the plugin target, and
        # each flag is probed the same way it is there. ANDROID_NDK_ROOT is
        # whatever the caller points at and there is no version floor, so the
        # compiler genuinely varies: -ftrivial-auto-var-init=zero needs a separate
        # enabling option on the clang in NDK r25 (LLVM 14) and only became
        # unconditional in clang 16, so passing it blind fails that toolchain
        # outright and no archive gets staged.
        #
        # A rejected flag is reported rather than swallowed. -mbranch-protection
        # especially: lld emits the BTI/PAC property note only when every input
        # object carries it, so losing it here silently de-hardens the linked .so
        # with nothing in the output to say so.
        # Probed against BOTH drivers because the set is written to c_args and
        # cpp_args alike: a flag the C++ driver rejects would fail every C++
        # translation unit meson compiles, the opposite of the graceful-degrade
        # intent. Keep only flags both accept.
        C_ARGS=()
        for flag in -mbranch-protection=standard \
                    -fstack-protector-strong \
                    -ftrivial-auto-var-init=zero; do
            if printf 'int main(void) { return 0; }\n' |
                   "$CC" -Werror "$flag" -x c -c -o /dev/null - >/dev/null 2>&1 &&
               printf 'int main() { return 0; }\n' |
                   "$CXX" -Werror "$flag" -x c++ -c -o /dev/null - >/dev/null 2>&1; then
                C_ARGS+=("$flag")
            else
                echo "warning: $CC or $CXX rejected $flag; librist.a is built without it" >&2
            fi
        done
        # Preprocessor defines, accepted by every clang the above can select.
        C_ARGS+=(-U_FORTIFY_SOURCE -D_FORTIFY_SOURCE=2)

        # A real array in the cross file, not -Dc_args on the command line: meson
        # splits the command-line form on whitespace and the first flag swallows the
        # rest. cpp_args gets the identical set — BTI needs -mbranch-protection on
        # every object, C++ ones included (both drivers are probed above).
        args=''
        sep=''
        for a in "${C_ARGS[@]}"; do args="$args$sep'$a'"; sep=', '; done
        printf '[built-in options]\nc_args = [%s]\ncpp_args = [%s]\n' "$args" "$args" >> "$CROSS"

        meson setup "$SRC/build" "$SRC" --cross-file "$CROSS" \
            --default-library=static --buildtype=release
        ;;
    *)
        echo "error: unknown target '$TARGET' (supported: android-arm64)" >&2
        exit 2
        ;;
esac

ninja -C "$SRC/build" librist.a   # only the static we link; skip librist's CLI tools/tests

LIB="$SRC/build/librist.a"
if [ ! -f "$LIB" ] || [ "$(wc -c < "$LIB")" -lt 102400 ]; then
    echo "error: librist static missing or implausibly small at $LIB" >&2
    exit 1
fi

mkdir -p "$TP/$TARGET" "$TP/include/librist"
cp -f "$LIB" "$TP/$TARGET/librist.a"
cp -f "$SRC"/include/librist/*.h "$TP/include/librist/"
[ -d "$SRC/build/include/librist" ] && cp -f "$SRC"/build/include/librist/*.h "$TP/include/librist/" || true

echo "Staged: third_party/$TARGET/librist.a + third_party/include/librist/"
