# basis_media_native — OS-codec live media plugin for Basis Media Player.
#
# No third-party libraries: video/audio decode uses Media Foundation (Windows)
# and MediaCodec (Android); zero-copy present uses D3D11/D3D12 (Windows) and
# Vulkan (Android). The portable protocol/demux core (RTSP/RTMP/MPEG-TS/fMP4)
# is plain C and compiles on every host.
#
# Platforms with a real backend:
#   Windows x86_64 : basis_media_native.dll  -> Plugins/Windows/x86_64/
#   Android arm64  : libbasis_media_native.so -> Plugins/Android/arm64-v8a/
#
# Platforms that build the stub backend (links, never produces frames; lets
# Unity load the package without missing-DLL errors and exercises the demux
# core). Promote these to real backends as host implementations are added:
#   Windows ARM64  : basis_media_native.dll  -> Plugins/Windows/ARM64/
#   Linux x86_64   : libbasis_media_native.so -> Plugins/Linux/x86_64/
#   macOS x86_64   : libbasis_media_native.dylib -> Plugins/macOS/x86_64/
#   macOS arm64    : libbasis_media_native.dylib -> Plugins/macOS/arm64/
#   iOS arm64      : libbasis_media_native.a (static) -> Plugins/iOS/arm64/
#
# Unity PluginAPI headers are required for the Windows/Android glue; point
# UNITY_PLUGIN_API_DIR at <UnityEditor>/Editor/Data/PluginAPI.
#
# ---------------------------------------------------------------------------
# Quick reference (full per-platform commands live in Native~/build-*.sh):
#
#   Windows x64 (VS 2022):
#     cmake -S . -B build-win-x64 -A x64 ^
#           -DBASIS_WITH_RIST=ON ^
#           -DUNITY_PLUGIN_API_DIR="C:/Program Files/Unity/Hub/Editor/<ver>/Editor/Data/PluginAPI"
#     cmake --build build-win-x64 --config Release
#
#   Windows ARM64 (VS 2022, stub):
#     cmake -S . -B build-win-arm64 -A ARM64
#     cmake --build build-win-arm64 --config Release
#
#   Android arm64-v8a (NDK, Vulkan):
#     cmake -S . -B build-android-arm64 ^
#           -DCMAKE_TOOLCHAIN_FILE=$NDK/build/cmake/android.toolchain.cmake ^
#           -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-29 ^
#           -DCMAKE_BUILD_TYPE=Release ^
#           -DBASIS_WITH_RIST=ON ^
#           -DUNITY_PLUGIN_API_DIR=<UnityEditor>/Editor/Data/PluginAPI
#     cmake --build build-android-arm64 --config Release
#
#   Linux x86_64 (stub):           bash build-linux.sh
#   macOS universal (stub):        bash build-macos.sh
#   iOS arm64 static (stub):       bash build-ios.sh
#
#   NOTE: the shipped win-x64 and android-arm64 binaries are built RIST-enabled.
#   BASIS_WITH_RIST defaults OFF, so a rebuild that omits -DBASIS_WITH_RIST=ON
#   silently drops rist:// playback from the plugin (the rest of the plugin is
#   unaffected). Stage the librist static first — run Native~/build-librist.*, or
#   drop the media-native CI workflow artifact into third_party/<rid>/. win-x64 and
#   android-arm64 are the only RIST targets; other platforms ignore the flag.
# ---------------------------------------------------------------------------

cmake_minimum_required(VERSION 3.18)
project(basis_media_native C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 14)
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

set(UNITY_PLUGIN_API_DIR "" CACHE PATH "Path to Unity's Editor/Data/PluginAPI headers")

# RIST live-ingest transport (protocol/basis_rist.c). OFF by default: the plugin
# then links only OS frameworks exactly as before, and basis_rist.c compiles to a
# graceful "not built" stub. ON links the librist static (built from source by
# Native~/build-librist.*, mbedTLS bundled in) from third_party/ into the plugin —
# see third_party/README.md.
option(BASIS_WITH_RIST "Build the RIST transport (links librist built from source by build-librist.*)" OFF)

# Portable core (every platform)
set(CORE_SOURCES
    basis_media_core.c
    protocol/basis_url.c
    protocol/basis_io.c
    protocol/basis_http.c
    protocol/basis_bitstream.c
    protocol/basis_rtsp.c
    protocol/basis_rtmp.c
    protocol/basis_ts.c
    protocol/basis_mp4.c
    protocol/basis_webm.c
    protocol/basis_ogg.c
    protocol/basis_wav.c
    protocol/basis_mp3.c
    protocol/basis_hls.c
    protocol/basis_rist.c
    protocol/basis_caption.c
)

# ---------------------------------------------------------------------------
# Platform classification.
#
# Only Win-x64 and Android-arm64 currently have real OS-codec backends.
# Everything else (Win-ARM64, Linux, macOS, iOS) links the portable stub so
# Unity can load the package and the demux core can be exercised.
# ---------------------------------------------------------------------------
set(BASIS_BACKEND "stub" CACHE STRING "Forced backend (stub|windows|android); auto-detected when empty")

set(PLATFORM_SOURCES "")
set(BASIS_LIBRARY_TYPE SHARED)
set(BASIS_IS_REAL_BACKEND OFF)

if(WIN32 AND CMAKE_SIZEOF_VOID_P EQUAL 8 AND NOT (CMAKE_GENERATOR_PLATFORM STREQUAL "ARM64"))
    # Windows x86_64 — real Media Foundation + D3D11/D3D12 backend.
    list(APPEND PLATFORM_SOURCES
        windows/basis_win_decode.cpp
        windows/basis_win_http.cpp
        unity/basis_unity_plugin.cpp)
    set(BASIS_IS_REAL_BACKEND ON)
elseif(ANDROID)
    # Android arm64-v8a — real MediaCodec + Vulkan backend.
    list(APPEND PLATFORM_SOURCES
        android/basis_android_decode.c
        android/basis_android_vk.c
        android/basis_jni_https.c
        unity/basis_unity_plugin.cpp)
    set(BASIS_IS_REAL_BACKEND ON)
else()
    # Stub backend (Win-ARM64, Linux, macOS, iOS, anything else).
    list(APPEND PLATFORM_SOURCES other/basis_decoder_stub.c)
    if(WIN32)
        # Windows ARM64 keeps the WinHTTP byte source even with a stub decoder:
        # run_http_like selects it on _WIN32, not on which decoder backend is in
        # play, so omitting it is an unresolved symbol at link time rather than a
        # smaller binary. It needs nothing from the decode side, and the link line
        # below already expects it.
        list(APPEND PLATFORM_SOURCES windows/basis_win_http.cpp)
    endif()
    if(IOS OR CMAKE_SYSTEM_NAME STREQUAL "iOS")
        # iOS Unity plugins must be static archives.
        set(BASIS_LIBRARY_TYPE STATIC)
    endif()
endif()

add_library(basis_media_native ${BASIS_LIBRARY_TYPE} ${CORE_SOURCES} ${PLATFORM_SOURCES})
target_include_directories(basis_media_native PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

if(BASIS_IS_REAL_BACKEND AND UNITY_PLUGIN_API_DIR)
    target_include_directories(basis_media_native PRIVATE ${UNITY_PLUGIN_API_DIR})
endif()
# Unity headers are also discoverable if copied into unity/.
target_include_directories(basis_media_native PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/unity)

# ---------------------------------------------------------------------------
# System libraries / per-platform link surface.
# ---------------------------------------------------------------------------
if(BASIS_IS_REAL_BACKEND AND WIN32)
    target_link_libraries(basis_media_native PRIVATE
        mfplat mfuuid wmcodecdspuuid
        d3d11 dxgi winhttp ws2_32)
elseif(BASIS_IS_REAL_BACKEND AND ANDROID)
    find_library(ANDROID_LIB android)
    find_library(LOG_LIB log)
    find_library(MEDIANDK_LIB mediandk)
    find_library(VULKAN_LIB vulkan)
    target_link_libraries(basis_media_native PRIVATE
        ${ANDROID_LIB} ${LOG_LIB} ${MEDIANDK_LIB} ${VULKAN_LIB})
elseif(WIN32)
    # Stub on Windows ARM64 — link only what protocol/demux needs.
    target_link_libraries(basis_media_native PRIVATE winhttp ws2_32)
elseif(APPLE)
    # macOS + iOS stub. Hook up frameworks the future real backend will need
    # (VideoToolbox/AVFoundation) so dependent symbols resolve once the real
    # implementation lands; harmless for the stub.
    find_library(FOUNDATION_LIB Foundation)
    find_library(COREFOUNDATION_LIB CoreFoundation)
    target_link_libraries(basis_media_native PRIVATE
        ${FOUNDATION_LIB} ${COREFOUNDATION_LIB})
    if(NOT (IOS OR CMAKE_SYSTEM_NAME STREQUAL "iOS"))
        set_target_properties(basis_media_native PROPERTIES
            SUFFIX ".dylib"
            INSTALL_NAME_DIR "@rpath")
    endif()
else()
    # Linux stub.
    find_package(Threads REQUIRED)
    target_link_libraries(basis_media_native PRIVATE Threads::Threads ${CMAKE_DL_LIBS})
endif()

# ---------------------------------------------------------------------------
# RIST: link the librist static for the target platform. librist vendors its own
# mbedTLS and links it into the archive, so a single library per platform is all
# that's needed. The static is built from source by Native~/build-librist.* into
# third_party/<rid>/ (see third_party/README.md); headers under third_party/include.
# They stay out of Unity's import (Native~/ is hidden by the trailing ~), so they're
# pure build inputs — never committed, never shipped as separate Unity plugins; they
# link into basis_media_native itself.
# ---------------------------------------------------------------------------
if(BASIS_WITH_RIST)
    set(RIST_ROOT ${CMAKE_CURRENT_SOURCE_DIR}/third_party)

    # Map the target to its librist static directory (mirrors the runtime-identifier
    # naming Basis already uses for Opus's native binaries).
    if(WIN32 AND CMAKE_SIZEOF_VOID_P EQUAL 8 AND NOT (CMAKE_GENERATOR_PLATFORM STREQUAL "ARM64"))
        set(RIST_RID win-x64)
    elseif(ANDROID AND ANDROID_ABI STREQUAL "arm64-v8a")
        set(RIST_RID android-arm64)
    else()
        message(FATAL_ERROR "BASIS_WITH_RIST: no librist static for this target yet. "
                            "Supported: Windows x64, Android arm64-v8a. See third_party/README.md.")
    endif()

    set(RIST_LIBDIR ${RIST_ROOT}/${RIST_RID})
    # NO_CMAKE_FIND_ROOT_PATH: the Android NDK toolchain re-roots find_library under the sysroot,
    # which hides this vendored static living outside it. No-op on the desktop toolchains.
    find_library(RIST_LIB rist PATHS ${RIST_LIBDIR} NO_DEFAULT_PATH NO_CMAKE_FIND_ROOT_PATH)  # rist.lib (win) / librist.a (android)
    if(NOT RIST_LIB)
        message(FATAL_ERROR "BASIS_WITH_RIST: librist not found in ${RIST_LIBDIR}. "
                            "Build it from source first: run Native~/build-librist.* (see third_party/README.md).")
    endif()

    target_compile_definitions(basis_media_native PRIVATE BASIS_WITH_RIST)
    target_include_directories(basis_media_native PRIVATE ${RIST_ROOT}/include)
    target_link_libraries(basis_media_native PRIVATE ${RIST_LIB})

    if(WIN32)
        # librist's transitive Windows deps (a static archive doesn't carry them):
        # ws2_32 (UDP sockets), bcrypt (bundled mbedTLS entropy), iphlpapi
        # (GetAdaptersInfo, used by librist's network code for the adapter MAC).
        target_link_libraries(basis_media_native PRIVATE ws2_32 bcrypt iphlpapi)
    endif()
endif()

# ---------------------------------------------------------------------------
# Warnings and exploit mitigations.
#
# The demux core parses remote containers in-process inside the Unity client,
# so the mitigations are requested explicitly instead of left to toolchain
# defaults: MSVC supplies a subset (/GS, /DYNAMICBASE, /NXCOMPAT) and clang
# supplies none of them for the bare targets the stub platforms build against.
# ---------------------------------------------------------------------------
if(MSVC)
    # /guard:cf has to reach both the compiler and the linker to take effect.
    # /Qspectre is deliberately absent: MSBuild fails the build outright
    # (MSB8040) unless the Spectre-mitigated libraries component is installed,
    # and a speculative side channel buys an attacker nothing here — the plugin
    # already runs in-process with the client's full privileges.
    target_compile_options(basis_media_native PRIVATE /W3 /guard:cf)
    target_link_options(basis_media_native PRIVATE /guard:cf /DYNAMICBASE /NXCOMPAT)
    if(CMAKE_SIZEOF_VOID_P EQUAL 8)
        target_link_options(basis_media_native PRIVATE /HIGHENTROPYVA)
    endif()
    # /CETCOMPAT applies to x64 only. CMAKE_GENERATOR_PLATFORM is the obvious
    # test but only the Visual Studio generators set it, so an ARM64 Ninja build
    # would slip through; the compiler's own target id holds for any generator.
    #
    # It also needs the linker to know the option at all — it arrived in VS 2019
    # 16.7 (19.27). An older link.exe answers an unknown switch with LNK4044 and
    # carries on, so without this test the mitigation would drop out silently and
    # the build would still look clean. A link probe cannot tell the two apart for
    # the same reason: the probe link succeeds either way. Hence a version test,
    # and a warning rather than a quiet no-op.
    # Compared case-folded: a CMake that spells the id "X64" instead of "x64"
    # would otherwise fail an exact match, drop /CETCOMPAT, and warn about
    # nothing — the same silent de-hardening the version test above guards against.
    string(TOLOWER "${CMAKE_C_COMPILER_ARCHITECTURE_ID}" _basis_msvc_arch)
    if(_basis_msvc_arch STREQUAL "x64")
        if(MSVC_VERSION GREATER_EQUAL 1927)
            target_link_options(basis_media_native PRIVATE /CETCOMPAT)
        else()
            message(WARNING
                "MSVC ${MSVC_VERSION} predates /CETCOMPAT (19.27 / VS 2019 16.7): "
                "linking basis_media_native without CET shadow-stack compatibility.")
        endif()
    endif()
else()
    include(CheckCCompilerFlag)
    include(CMakePushCheckState)

    target_compile_options(basis_media_native PRIVATE
        -Wall -Wextra -Wno-unused-parameter -Wformat -Wformat-security)

    # Probed, not assumed: this tree is built by Apple clang, NDK clang and gcc,
    # and each rejects a different subset (PAC/BTI is aarch64, CET is x86_64).
    # Appended rather than assigned: a toolchain file may already have put flags
    # here that a test compile needs, and dropping them would fail every probe and
    # silently take the whole set of mitigations with it.
    cmake_push_check_state()
    string(APPEND CMAKE_REQUIRED_FLAGS " -Werror")
    foreach(_flag -fstack-protector-strong -fcf-protection=full
                  -mbranch-protection=standard -ftrivial-auto-var-init=zero)
        string(MAKE_C_IDENTIFIER "BASIS_HAS${_flag}" _have)
        check_c_compiler_flag(${_flag} ${_have})
        if(${_have})
            target_compile_options(basis_media_native PRIVATE ${_flag})
            # On the link line too: under LTO codegen happens at link, so a
            # compile-only mitigation flag would be lost.
            target_link_options(basis_media_native PRIVATE ${_flag})
        endif()
    endforeach()
    cmake_pop_check_state()

    # Optimised configurations only: Bionic gates FORTIFY on __OPTIMIZE__ and
    # glibc warns when it is defined without -O, and an unset CMAKE_BUILD_TYPE
    # compiles unoptimised. -U first because the NDK already defines it. A custom
    # optimised configuration has to be added here to get FORTIFY.
    target_compile_options(basis_media_native PRIVATE
        "$<$<OR:$<CONFIG:Release>,$<CONFIG:RelWithDebInfo>,$<CONFIG:MinSizeRel>>:-U_FORTIFY_SOURCE;-D_FORTIFY_SOURCE=2>")

    # ELF-only linker hardening. Excludes Apple (Mach-O) and Windows: a non-MSVC
    # Windows toolchain (MinGW, clang *-windows-gnu) reaches this non-MSVC branch
    # too, and its PE linker rejects -z relro/now/noexecstack.
    if(NOT APPLE AND NOT WIN32)
        target_link_options(basis_media_native PRIVATE
            -Wl,-z,relro -Wl,-z,now -Wl,-z,noexecstack)
    endif()
endif()
