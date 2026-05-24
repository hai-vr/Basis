# basis_media_native — OS-codec live media plugin for Basis Video Player.
#
# No third-party libraries: video/audio decode uses Media Foundation (Windows) and
# MediaCodec (Android); zero-copy present uses D3D11/D3D12 (Windows) and Vulkan
# (Android). The portable protocol/demux core (RTSP/RTMP/MPEG-TS/fMP4) is plain C.
#
# Output (Unity picks these up under Plugins/<platform>/ automatically):
#   Windows : basis_media_native.dll        -> Plugins/Windows/x86_64/
#   Android : libbasis_media_native.so      -> Plugins/Android/arm64-v8a/   (Vulkan)
#   Linux/macOS: stub backend (protocol-core testing only)
#
# Unity PluginAPI headers are required for the Windows/Android plugin glue; point
# UNITY_PLUGIN_API_DIR at <UnityEditor>/Editor/Data/PluginAPI (see unity/README.md).
#
# ---------------------------------------------------------------------------
# Windows (Visual Studio):
#   cmake -S . -B build -A x64 -DUNITY_PLUGIN_API_DIR="C:/Program Files/Unity/Hub/Editor/<ver>/Editor/Data/PluginAPI"
#   cmake --build build --config Release
#   copy build/Release/basis_media_native.dll  -> Plugins/Windows/x86_64/
#
# Android (NDK arm64-v8a, Vulkan):
#   cmake -S . -B build-android -DCMAKE_TOOLCHAIN_FILE=$NDK/build/cmake/android.toolchain.cmake ^
#         -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-29 ^
#         -DUNITY_PLUGIN_API_DIR=<UnityEditor>/Editor/Data/PluginAPI
#   cmake --build build-android --config Release
#   copy libbasis_media_native.so -> Plugins/Android/arm64-v8a/
# ---------------------------------------------------------------------------

cmake_minimum_required(VERSION 3.18)
project(basis_media_native C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 14)
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

set(UNITY_PLUGIN_API_DIR "" CACHE PATH "Path to Unity's Editor/Data/PluginAPI headers")

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
)

set(PLATFORM_SOURCES "")
if(WIN32)
    list(APPEND PLATFORM_SOURCES
        windows/basis_win_decode.cpp
        windows/basis_win_http.cpp
        unity/basis_unity_plugin.cpp)
elseif(ANDROID)
    list(APPEND PLATFORM_SOURCES
        android/basis_android_decode.c
        android/basis_android_vk.c
        unity/basis_unity_plugin.cpp)
else()
    list(APPEND PLATFORM_SOURCES other/basis_decoder_stub.c)
endif()

add_library(basis_media_native SHARED ${CORE_SOURCES} ${PLATFORM_SOURCES})
target_include_directories(basis_media_native PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

if((WIN32 OR ANDROID) AND UNITY_PLUGIN_API_DIR)
    target_include_directories(basis_media_native PRIVATE ${UNITY_PLUGIN_API_DIR})
endif()
# Unity headers are also discoverable if copied into unity/.
target_include_directories(basis_media_native PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/unity)

if(WIN32)
    target_link_libraries(basis_media_native PRIVATE
        mfplat mfuuid wmcodecdspuuid
        d3d11 dxgi winhttp ws2_32)
elseif(ANDROID)
    find_library(ANDROID_LIB android)
    find_library(LOG_LIB log)
    find_library(MEDIANDK_LIB mediandk)
    find_library(VULKAN_LIB vulkan)
    target_link_libraries(basis_media_native PRIVATE
        ${ANDROID_LIB} ${LOG_LIB} ${MEDIANDK_LIB} ${VULKAN_LIB})
else()
    find_package(Threads REQUIRED)
    target_link_libraries(basis_media_native PRIVATE Threads::Threads)
endif()

if(MSVC)
    target_compile_options(basis_media_native PRIVATE /W3)
else()
    target_compile_options(basis_media_native PRIVATE -Wall -Wextra -Wno-unused-parameter)
endif()
