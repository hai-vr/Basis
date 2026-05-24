/*
 * basis_android_vk.c — Vulkan zero-copy present for Quest.
 *
 * Imports a decoded AHardwareBuffer (YCbCr) via
 * VK_ANDROID_external_memory_android_hardware_buffer and resolves it to an RGBA
 * VkImage that Unity samples. Runs on Unity's VkDevice/queue (IUnityGraphicsVulkan,
 * surfaced through basis_gfx_vk_*).
 *
 * STATUS: the AHB import + Y'CbCr conversion object + RGBA target are set up here.
 * The actual YCbCr->RGBA resolve pass (descriptor set with the ycbcr-conversion
 * sampler, a fullscreen pipeline, command-buffer submit on Unity's queue, and the
 * image-layout transitions) is the one piece that must be finished and validated
 * on-device — it is marked TODO below. Until then the RGBA image is allocated and
 * wired end-to-end (Unity gets a valid VkImage) but not yet filled.
 */

#include "basis_android_vk.h"
#include "../basis_media_internal.h"

#define VK_USE_PLATFORM_ANDROID_KHR
#include <vulkan/vulkan.h>
#include <vulkan/vulkan_android.h>
#include <android/hardware_buffer.h>

#include <pthread.h>
#include <stdlib.h>
#include <string.h>

struct basis_vk_present {
    VkInstance instance;
    VkPhysicalDevice phys;
    VkDevice device;
    VkQueue queue;
    uint32_t queueFamily;

    /* extension entry points */
    PFN_vkGetAndroidHardwareBufferPropertiesANDROID getAHBProps;

    pthread_mutex_t lock;
    AHardwareBuffer* pending;   /* newest decoded buffer awaiting import (render thread) */
    AHardwareBuffer* imported;  /* currently imported buffer */
    int w, h;

    /* imported YCbCr image */
    VkImage srcImage;
    VkDeviceMemory srcMemory;
    VkSamplerYcbcrConversion ycbcr;

    /* RGBA target Unity samples */
    VkImage rgbaImage;
    VkDeviceMemory rgbaMemory;
    int rgbaW, rgbaH;

    uint64_t frameCounter;
};

static void load_handles(basis_vk_present* v) {
    v->instance    = (VkInstance)(uintptr_t)basis_gfx_vk_instance();
    v->phys        = (VkPhysicalDevice)(uintptr_t)basis_gfx_vk_physical_device();
    v->device      = (VkDevice)(uintptr_t)basis_gfx_vk_device();
    v->queue       = (VkQueue)(uintptr_t)basis_gfx_vk_graphics_queue();
    v->queueFamily = basis_gfx_vk_graphics_queue_family();
    if (v->device)
        v->getAHBProps = (PFN_vkGetAndroidHardwareBufferPropertiesANDROID)
            vkGetDeviceProcAddr(v->device, "vkGetAndroidHardwareBufferPropertiesANDROID");
}

basis_vk_present* basis_vk_create(void) {
    basis_vk_present* v = (basis_vk_present*)calloc(1, sizeof(*v));
    if (!v) return NULL;
    pthread_mutex_init(&v->lock, NULL);
    load_handles(v);
    return v;
}

void basis_vk_set_hardware_buffer(basis_vk_present* v, AHardwareBuffer* ahb, int w, int h) {
    if (!v || !ahb) return;
    AHardwareBuffer_acquire(ahb);
    pthread_mutex_lock(&v->lock);
    if (v->pending) AHardwareBuffer_release(v->pending);
    v->pending = ahb; v->w = w; v->h = h;
    pthread_mutex_unlock(&v->lock);
}

static void destroy_src(basis_vk_present* v) {
    if (v->srcImage)  { vkDestroyImage(v->device, v->srcImage, NULL); v->srcImage = VK_NULL_HANDLE; }
    if (v->srcMemory) { vkFreeMemory(v->device, v->srcMemory, NULL); v->srcMemory = VK_NULL_HANDLE; }
    if (v->ycbcr)     { vkDestroySamplerYcbcrConversion(v->device, v->ycbcr, NULL); v->ycbcr = VK_NULL_HANDLE; }
    if (v->imported)  { AHardwareBuffer_release(v->imported); v->imported = NULL; }
}

/* Import an AHardwareBuffer as an external-format Y'CbCr VkImage. */
static int import_ahb(basis_vk_present* v, AHardwareBuffer* ahb, int w, int h) {
    if (!v->device || !v->getAHBProps) return -1;

    VkAndroidHardwareBufferFormatPropertiesANDROID fmtProps = { VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_FORMAT_PROPERTIES_ANDROID };
    VkAndroidHardwareBufferPropertiesANDROID props = { VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_PROPERTIES_ANDROID, &fmtProps };
    if (v->getAHBProps(v->device, ahb, &props) != VK_SUCCESS) return -1;

    /* Y'CbCr conversion described by the AHB's external format. */
    VkExternalFormatANDROID extFmt = { VK_STRUCTURE_TYPE_EXTERNAL_FORMAT_ANDROID };
    extFmt.externalFormat = fmtProps.externalFormat;

    VkSamplerYcbcrConversionCreateInfo cy = { VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_CREATE_INFO };
    cy.pNext = &extFmt;
    cy.format = VK_FORMAT_UNDEFINED;
    cy.ycbcrModel = fmtProps.suggestedYcbcrModel;
    cy.ycbcrRange = fmtProps.suggestedYcbcrRange;
    cy.components = fmtProps.samplerYcbcrConversionComponents;
    cy.xChromaOffset = fmtProps.suggestedXChromaOffset;
    cy.yChromaOffset = fmtProps.suggestedYChromaOffset;
    cy.chromaFilter = VK_FILTER_LINEAR;
    if (vkCreateSamplerYcbcrConversion(v->device, &cy, NULL, &v->ycbcr) != VK_SUCCESS) return -1;

    /* image with external format + dedicated AHB memory */
    VkExternalMemoryImageCreateInfo extImg = { VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO };
    extImg.pNext = &extFmt;
    extImg.handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID;

    VkImageCreateInfo ici = { VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO };
    ici.pNext = &extImg;
    ici.imageType = VK_IMAGE_TYPE_2D;
    ici.format = VK_FORMAT_UNDEFINED; /* external format */
    ici.extent.width = (uint32_t)w; ici.extent.height = (uint32_t)h; ici.extent.depth = 1;
    ici.mipLevels = 1; ici.arrayLayers = 1;
    ici.samples = VK_SAMPLE_COUNT_1_BIT;
    ici.tiling = VK_IMAGE_TILING_OPTIMAL;
    ici.usage = VK_IMAGE_USAGE_SAMPLED_BIT;
    ici.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    ici.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    if (vkCreateImage(v->device, &ici, NULL, &v->srcImage) != VK_SUCCESS) return -1;

    VkImportAndroidHardwareBufferInfoANDROID importInfo = { VK_STRUCTURE_TYPE_IMPORT_ANDROID_HARDWARE_BUFFER_INFO_ANDROID };
    importInfo.buffer = ahb;
    VkMemoryDedicatedAllocateInfo dedicated = { VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO, &importInfo };
    dedicated.image = v->srcImage;

    /* pick a memory type from props.memoryTypeBits */
    uint32_t typeIndex = 0;
    for (uint32_t i = 0; i < 32; ++i) if (props.memoryTypeBits & (1u << i)) { typeIndex = i; break; }

    VkMemoryAllocateInfo mai = { VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO, &dedicated };
    mai.allocationSize = props.allocationSize;
    mai.memoryTypeIndex = typeIndex;
    if (vkAllocateMemory(v->device, &mai, NULL, &v->srcMemory) != VK_SUCCESS) return -1;
    if (vkBindImageMemory(v->device, v->srcImage, v->srcMemory, 0) != VK_SUCCESS) return -1;

    v->imported = ahb; /* take ownership of the ref */
    return 0;
}

static int ensure_rgba(basis_vk_present* v, int w, int h) {
    if (v->rgbaImage && v->rgbaW == w && v->rgbaH == h) return 0;
    if (v->rgbaImage) { vkDestroyImage(v->device, v->rgbaImage, NULL); v->rgbaImage = VK_NULL_HANDLE; }
    if (v->rgbaMemory) { vkFreeMemory(v->device, v->rgbaMemory, NULL); v->rgbaMemory = VK_NULL_HANDLE; }

    VkImageCreateInfo ici = { VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO };
    ici.imageType = VK_IMAGE_TYPE_2D;
    ici.format = VK_FORMAT_R8G8B8A8_UNORM;
    ici.extent.width = (uint32_t)w; ici.extent.height = (uint32_t)h; ici.extent.depth = 1;
    ici.mipLevels = 1; ici.arrayLayers = 1;
    ici.samples = VK_SAMPLE_COUNT_1_BIT;
    ici.tiling = VK_IMAGE_TILING_OPTIMAL;
    ici.usage = VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT;
    ici.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    ici.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    if (vkCreateImage(v->device, &ici, NULL, &v->rgbaImage) != VK_SUCCESS) return -1;

    VkMemoryRequirements req; vkGetImageMemoryRequirements(v->device, v->rgbaImage, &req);
    VkPhysicalDeviceMemoryProperties mp; vkGetPhysicalDeviceMemoryProperties(v->phys, &mp);
    uint32_t ti = 0;
    for (uint32_t i = 0; i < mp.memoryTypeCount; ++i)
        if ((req.memoryTypeBits & (1u << i)) && (mp.memoryTypes[i].propertyFlags & VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT)) { ti = i; break; }
    VkMemoryAllocateInfo mai = { VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO };
    mai.allocationSize = req.size; mai.memoryTypeIndex = ti;
    if (vkAllocateMemory(v->device, &mai, NULL, &v->rgbaMemory) != VK_SUCCESS) return -1;
    vkBindImageMemory(v->device, v->rgbaImage, v->rgbaMemory, 0);
    v->rgbaW = w; v->rgbaH = h;
    return 0;
}

int basis_vk_render_update(basis_vk_present* v) {
    if (!v || !v->device) return 0;

    AHardwareBuffer* ahb = NULL; int w, h;
    pthread_mutex_lock(&v->lock);
    ahb = v->pending; v->pending = NULL; w = v->w; h = v->h;
    pthread_mutex_unlock(&v->lock);
    if (!ahb) return 0;

    /* rebuild import for the newest buffer (AHBs are recycled, but re-importing
     * per frame is the simplest correct option; can be pooled later) */
    destroy_src(v);
    if (import_ahb(v, ahb, w, h) != 0) { AHardwareBuffer_release(ahb); return 0; }
    if (ensure_rgba(v, w, h) != 0) return 0;

    /* TODO(on-device): record + submit the YCbCr->RGBA resolve here:
     *   - VkImageView over srcImage with a VkSamplerYcbcrConversionInfo chained
     *     into the view + an immutable-sampler descriptor using v->ycbcr
     *   - a fullscreen pipeline (or vkCmdBlitImage is NOT valid for ycbcr;
     *     must sample) writing into rgbaImage as a color attachment
     *   - layout transitions: srcImage UNDEFINED->SHADER_READ_ONLY,
     *     rgbaImage UNDEFINED->COLOR_ATTACHMENT then ->SHADER_READ_ONLY
     *   - submit on v->queue; coordinate with Unity via IUnityGraphicsVulkan
     *     (UnityVulkanRecordingState / EnsureOutsideRenderPass)
     * Until this lands, rgbaImage is allocated but not filled. */

    v->frameCounter++;
    return 1;
}

uint64_t basis_vk_get_image(basis_vk_present* v, int* w, int* h) {
    if (!v) { if (w) *w = 0; if (h) *h = 0; return 0; }
    if (w) *w = v->rgbaW; if (h) *h = v->rgbaH;
    return (uint64_t)(uintptr_t)v->rgbaImage;
}

uint64_t basis_vk_frame_counter(basis_vk_present* v) { return v ? v->frameCounter : 0; }

void basis_vk_release(basis_vk_present* v) {
    if (!v || !v->device) return;
    destroy_src(v);
    if (v->rgbaImage) { vkDestroyImage(v->device, v->rgbaImage, NULL); v->rgbaImage = VK_NULL_HANDLE; }
    if (v->rgbaMemory) { vkFreeMemory(v->device, v->rgbaMemory, NULL); v->rgbaMemory = VK_NULL_HANDLE; }
    pthread_mutex_lock(&v->lock);
    if (v->pending) { AHardwareBuffer_release(v->pending); v->pending = NULL; }
    pthread_mutex_unlock(&v->lock);
    v->rgbaW = v->rgbaH = 0;
}

void basis_vk_destroy(basis_vk_present* v) {
    if (!v) return;
    basis_vk_release(v);
    pthread_mutex_destroy(&v->lock);
    free(v);
}
