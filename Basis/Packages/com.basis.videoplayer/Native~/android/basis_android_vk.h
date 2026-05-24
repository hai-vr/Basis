/* Vulkan zero-copy present for Android/Quest: imports a decoded AHardwareBuffer
 * (YCbCr, from MediaCodec via AImageReader) and produces an RGBA VkImage that
 * Unity samples through Texture2D.CreateExternalTexture.
 *
 * Uses VK_ANDROID_external_memory_android_hardware_buffer to import the AHB and a
 * VkSamplerYcbcrConversion + a small graphics pass to resolve YCbCr -> RGBA. The
 * Vulkan device/queue come from Unity via IUnityGraphicsVulkan (basis_gfx_vk_*).
 */
#ifndef BASIS_ANDROID_VK_H
#define BASIS_ANDROID_VK_H

#include <stdint.h>

struct AHardwareBuffer;

#ifdef __cplusplus
extern "C" {
#endif

typedef struct basis_vk_present basis_vk_present;

basis_vk_present* basis_vk_create(void);
void             basis_vk_destroy(basis_vk_present* v);

/* Decode thread: hand off the newest decoded AHardwareBuffer (the present takes
 * its own ref). Cheap; the GPU work happens in basis_vk_render_update. */
void basis_vk_set_hardware_buffer(basis_vk_present* v, struct AHardwareBuffer* ahb, int w, int h);

/* Render thread: import the pending AHB (if any) and resolve it to the RGBA
 * VkImage. Returns 1 if a new frame was published. */
int basis_vk_render_update(basis_vk_present* v);

/* The RGBA VkImage handle (as uintptr_t) to wrap with CreateExternalTexture. */
uint64_t basis_vk_get_image(basis_vk_present* v, int* w, int* h);
uint64_t basis_vk_frame_counter(basis_vk_present* v);

/* Render thread: free all Vulkan + AHB resources. */
void basis_vk_release(basis_vk_present* v);

#ifdef __cplusplus
}
#endif
#endif
