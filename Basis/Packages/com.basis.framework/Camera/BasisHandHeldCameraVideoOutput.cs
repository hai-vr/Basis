#if BASIS_HAS_SPOUT && (UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR))
#define BASIS_VIDEO_OUTPUT_SPOUT
#elif BASIS_HAS_SYPHON && (UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR))
#define BASIS_VIDEO_OUTPUT_SYPHON
#elif UNITY_EDITOR_LINUX || (UNITY_STANDALONE_LINUX && !UNITY_EDITOR)
#define BASIS_VIDEO_OUTPUT_V4L2
#endif
#if BASIS_VIDEO_OUTPUT_SPOUT || BASIS_VIDEO_OUTPUT_SYPHON || BASIS_VIDEO_OUTPUT_V4L2
#define BASIS_VIDEO_OUTPUT_SUPPORTED
#endif
using System;
using System.Collections.Generic;
using Basis;
using UnityEngine;
#if BASIS_VIDEO_OUTPUT_SPOUT
using Klak.Spout;
#endif
#if BASIS_VIDEO_OUTPUT_SYPHON
using Klak.Syphon;
#endif
#if BASIS_VIDEO_OUTPUT_V4L2
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Rendering;
#endif

/// <summary>
/// Live video output for the handheld camera: Spout sender on Windows, Syphon server
/// on macOS, v4l2loopback virtual camera on Linux. Streams whatever the preview shows
/// at a picked resolution and framerate.
/// </summary>
public partial class BasisHandHeldCamera
{
    /// <summary>Stream settings. Width/Height/FrameRate persist via <see cref="BasisHandHeldCameraUI.CameraSettings"/>.</summary>
    [NonSerialized]
    public BasisVideoOutputSettings VideoOutputSettings = new BasisVideoOutputSettings();

    /// <summary>True while streaming.</summary>
    [NonSerialized]
    public bool IsVideoOutputActive;

    /// <summary>True on platforms with a video output backend (Windows/Spout, macOS/Syphon, Linux/v4l2loopback).</summary>
    public static bool IsVideoOutputSupported =>
#if BASIS_VIDEO_OUTPUT_SUPPORTED
        true;
#else
        false;
#endif

#if BASIS_VIDEO_OUTPUT_SPOUT
    private BasisSpoutVideoOutputSink videoSink;
#elif BASIS_VIDEO_OUTPUT_SYPHON
    private BasisSyphonVideoOutputSink videoSink;
#elif BASIS_VIDEO_OUTPUT_V4L2
    private BasisV4L2VideoOutputSink videoSink;
#endif
#if BASIS_VIDEO_OUTPUT_SUPPORTED
    /// <summary>Sender names in use process-wide, so simultaneous cameras publish separate outputs (#854).</summary>
    private static readonly HashSet<string> ActiveVideoOutputNames = new HashSet<string>();
#endif
    private RenderTexture videoStreamTexture;
    private BasisRenderRateLimiter videoPacing;

    /// <summary>Starts streaming with <see cref="VideoOutputSettings"/>. The capture camera keeps rendering while active, even when the preview is hidden.</summary>
    public bool StartVideoOutput()
    {
#if BASIS_VIDEO_OUTPUT_SUPPORTED
        StopVideoOutput();
        if (captureCamera == null) return false;
        BasisVideoOutputSettings settings = VideoOutputSettings;
        settings.Width = Mathf.Clamp(settings.Width, 16, 8192);
        settings.Height = Mathf.Clamp(settings.Height, 16, 8192);

        string baseName = string.IsNullOrEmpty(settings.SenderName) ? "Basis Camera" : settings.SenderName;
        string senderName = baseName;
        for (int Suffix = 2; ActiveVideoOutputNames.Contains(senderName); Suffix++)
        {
            senderName = $"{baseName} {Suffix}";
        }
        settings.SenderName = senderName;
        ActiveVideoOutputNames.Add(senderName);

#if BASIS_VIDEO_OUTPUT_V4L2
        RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.BGRA32) ? RenderTextureFormat.BGRA32 : RenderTextureFormat.ARGB32;
        videoSink = new BasisV4L2VideoOutputSink();
#elif BASIS_VIDEO_OUTPUT_SYPHON
        RenderTextureFormat format = RenderTextureFormat.ARGB32;
        videoSink = new BasisSyphonVideoOutputSink();
#else
        RenderTextureFormat format = RenderTextureFormat.ARGB32;
        videoSink = new BasisSpoutVideoOutputSink();
#endif
        videoStreamTexture = new RenderTexture(new RenderTextureDescriptor(settings.Width, settings.Height, format, 0) { sRGB = true }) { name = "BasisVideoOutput" };
        videoStreamTexture.Create();

        if (!videoSink.Start(settings, captureCamera.gameObject))
        {
            StopVideoOutput();
            return false;
        }
        videoPacing = default;
        IsVideoOutputActive = true;
        VisibilityFlag(Renderer != null && Renderer.isVisible);
        BasisDebug.Log($"Video output started: {settings.Width}x{settings.Height} @ {settings.FrameRate}fps", BasisDebug.LogTag.Camera);
        return true;
#else
        return false;
#endif
    }

    /// <summary>Stops streaming.</summary>
    public void StopVideoOutput()
    {
#if BASIS_VIDEO_OUTPUT_SUPPORTED
        bool wasActive = IsVideoOutputActive;
        IsVideoOutputActive = false;
        ActiveVideoOutputNames.Remove(VideoOutputSettings.SenderName);
        if (videoSink != null)
        {
            videoSink.Stop();
            videoSink = null;
        }
        if (videoStreamTexture != null)
        {
            videoStreamTexture.Release();
            Destroy(videoStreamTexture);
            videoStreamTexture = null;
        }
        if (wasActive) VisibilityFlag(Renderer != null && Renderer.isVisible);
#endif
    }

    /// <summary>Sets the stream resolution, restarting the stream when active.</summary>
    public void SetVideoOutputResolution(int width, int height)
    {
        VideoOutputSettings.Width = width;
        VideoOutputSettings.Height = height;
        if (IsVideoOutputActive) StartVideoOutput();
    }

    /// <summary>Sets the stream framerate. Applies live; 0 or below streams at the render rate.</summary>
    public void SetVideoOutputFrameRate(float frameRate)
    {
        VideoOutputSettings.FrameRate = frameRate;
    }

    private void TickVideoOutput()
    {
#if BASIS_VIDEO_OUTPUT_SUPPORTED
        if (!IsVideoOutputActive) return;
        if (videoSink.FailureMessage != null)
        {
            BasisDebug.LogError($"Video output stopped: {videoSink.FailureMessage}", BasisDebug.LogTag.Camera);
            StopVideoOutput();
            return;
        }
        Texture source = IsOverridingDesktopView ? (Texture)CopyCameraColorToStaticRTFeature.OutputRT : renderTexture;
        if (source == null) return;
        if (!videoPacing.AllowThisFrame(Time.unscaledDeltaTime, VideoOutputSettings.FrameRate, true)) return;
#if BASIS_VIDEO_OUTPUT_V4L2
        // Readback of Unity RTs is bottom-up; V4L2 wants rows top-down.
        Graphics.Blit(source, videoStreamTexture, new Vector2(1f, -1f), new Vector2(0f, 1f));
#else
        Graphics.Blit(source, videoStreamTexture);
#endif
        videoSink.PushFrame(videoStreamTexture);
#endif
    }
}

namespace Basis
{
    public class BasisVideoOutputSettings
    {
        public int Width = 1920;
        public int Height = 1080;
        /// <summary>Target framerate. 0 or below streams at the render rate.</summary>
        public float FrameRate = 30f;
        /// <summary>Sender/server name shown to Spout (Windows) and Syphon (macOS) receivers.</summary>
        public string SenderName = "Basis Camera";
        /// <summary>Optional explicit /dev/videoN path (Linux only). Empty auto-detects the first v4l2loopback device.</summary>
        public string DevicePath = string.Empty;
    }

#if BASIS_VIDEO_OUTPUT_SPOUT
    public sealed class BasisSpoutVideoOutputSink
    {
        // In Always Included Shaders so the runtime-created SpoutResources survives build stripping.
        private const string BlitShaderPath = "Hidden/Klak/Spout/Blit";

        private SpoutSender sender;
        private SpoutResources resources;

        public string FailureMessage => null;

        public bool Start(BasisVideoOutputSettings settings, GameObject host)
        {
            Shader blitShader = Shader.Find(BlitShaderPath);
            if (blitShader == null)
            {
                BasisDebug.LogError($"Spout blit shader '{BlitShaderPath}' was stripped from the build — keep it in Always Included Shaders.", BasisDebug.LogTag.Camera);
                return false;
            }
            resources = ScriptableObject.CreateInstance<SpoutResources>();
            resources.blitShader = blitShader;
            sender = host.AddComponent<SpoutSender>();
            sender.SetResources(resources);
            sender.spoutName = settings.SenderName;
            sender.keepAlpha = false;
            sender.captureMethod = CaptureMethod.Texture;
            return true;
        }

        public void PushFrame(RenderTexture frame)
        {
            if (sender != null) sender.sourceTexture = frame;
        }

        public void Stop()
        {
            if (sender != null) UnityEngine.Object.Destroy(sender);
            if (resources != null) UnityEngine.Object.Destroy(resources);
            sender = null;
            resources = null;
        }
    }
#endif

#if BASIS_VIDEO_OUTPUT_SYPHON
    public sealed class BasisSyphonVideoOutputSink
    {
        // In Always Included Shaders so the runtime-created SyphonResources survives build stripping.
        private const string BlitShaderPath = "Hidden/Klak/Syphon/Blit";

        private SyphonServer server;
        private SyphonResources resources;

        public string FailureMessage => null;

        public bool Start(BasisVideoOutputSettings settings, GameObject host)
        {
            Shader blitShader = Shader.Find(BlitShaderPath);
            if (blitShader == null)
            {
                BasisDebug.LogError($"Syphon blit shader '{BlitShaderPath}' was stripped from the build — keep it in Always Included Shaders.", BasisDebug.LogTag.Camera);
                return false;
            }
            resources = ScriptableObject.CreateInstance<SyphonResources>();
            resources.blitShader = blitShader;
            server = host.AddComponent<SyphonServer>();
            server.Resources = resources;
            server.KeepAlpha = false;
            server.CaptureMethod = CaptureMethod.Texture;
            server.ServerName = settings.SenderName;
            return true;
        }

        public void PushFrame(RenderTexture frame)
        {
            // Property setters tear the server down, so only assign when the texture changes.
            if (server != null && server.SourceTexture != frame)
            {
                server.SourceTexture = frame;
            }
        }

        public void Stop()
        {
            if (server != null) UnityEngine.Object.Destroy(server);
            if (resources != null) UnityEngine.Object.Destroy(resources);
            server = null;
            resources = null;
        }
    }
#endif

#if BASIS_VIDEO_OUTPUT_V4L2
    /// <summary>
    /// Writes frames into a v4l2loopback virtual camera purely through libc so Linux apps
    /// (OBS, ffmpeg, browsers) consume the stream like a webcam. Requires the kernel
    /// module: sudo modprobe v4l2loopback exclusive_caps=1
    /// </summary>
    public sealed unsafe class BasisV4L2VideoOutputSink
    {
        private const int O_RDWR = 0x0002;
        private const int EINTR = 4;
        private const uint FourccXBGR32 = 0x34325258;              // 'XR24' — B,G,R,X in memory, matches BGRA32 readback
        private const ulong VIDIOC_QUERYCAP = 0x80685600;
        private const ulong VIDIOC_S_FMT = 0xC0D05605;
        private const uint V4L2_BUF_TYPE_VIDEO_OUTPUT = 2;
        private const uint V4L2_FIELD_NONE = 1;
        private const uint V4L2_COLORSPACE_SRGB = 8;
        private const uint V4L2_CAP_VIDEO_OUTPUT = 0x00000002;
        private const uint V4L2_CAP_DEVICE_CAPS = 0x80000000;
        private const string LoopbackDriverName = "v4l2 loopback";

        private int fd = -1;
        private string devicePath;
        private Action<AsyncGPUReadbackRequest> readbackCallback;

        /// <summary>Devices already streaming from this process, so simultaneous cameras each claim their own (#854).</summary>
        private static readonly HashSet<string> ClaimedDevices = new HashSet<string>();

        public string FailureMessage { get; private set; }

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int Open(string path, int flags);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        private static extern int Close(int fd);

        [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
        private static extern int Ioctl(int fd, ulong request, void* argp);

        [DllImport("libc", EntryPoint = "write", SetLastError = true)]
        private static extern IntPtr Write(int fd, void* buffer, UIntPtr count);

        [StructLayout(LayoutKind.Sequential)]
        private struct V4L2Capability                              // struct v4l2_capability, 104 bytes
        {
            public fixed byte Driver[16];
            public fixed byte Card[32];
            public fixed byte BusInfo[32];
            public uint Version;
            public uint Capabilities;
            public uint DeviceCaps;
            public fixed uint Reserved[3];
        }

        [StructLayout(LayoutKind.Explicit, Size = 208)]
        private struct V4L2Format                                  // struct v4l2_format, union at offset 8 (pointer-aligned)
        {
            [FieldOffset(0)] public uint Type;
            [FieldOffset(8)] public uint Width;
            [FieldOffset(12)] public uint Height;
            [FieldOffset(16)] public uint PixelFormat;
            [FieldOffset(20)] public uint Field;
            [FieldOffset(24)] public uint BytesPerLine;
            [FieldOffset(28)] public uint SizeImage;
            [FieldOffset(32)] public uint Colorspace;
        }

        public bool Start(BasisVideoOutputSettings settings, GameObject host)
        {
            fd = OpenDevice(settings.DevicePath, out devicePath);
            if (fd < 0)
            {
                BasisDebug.LogError("No v4l2loopback output device found. Load the module first: sudo modprobe v4l2loopback exclusive_caps=1", BasisDebug.LogTag.Camera);
                return false;
            }

            var format = new V4L2Format
            {
                Type = V4L2_BUF_TYPE_VIDEO_OUTPUT,
                Width = (uint)settings.Width,
                Height = (uint)settings.Height,
                PixelFormat = FourccXBGR32,
                Field = V4L2_FIELD_NONE,
                BytesPerLine = (uint)settings.Width * 4,
                SizeImage = (uint)(settings.Width * settings.Height * 4),
                Colorspace = V4L2_COLORSPACE_SRGB
            };
            if (Ioctl(fd, VIDIOC_S_FMT, &format) != 0)
            {
                BasisDebug.LogError($"VIDIOC_S_FMT on {devicePath} failed (errno {Marshal.GetLastWin32Error()})", BasisDebug.LogTag.Camera);
                Stop();
                return false;
            }

            readbackCallback = OnReadbackComplete;
            BasisDebug.Log($"V4L2 video output writing to {devicePath}", BasisDebug.LogTag.Camera);
            return true;
        }

        public void PushFrame(RenderTexture frame)
        {
            if (fd < 0 || FailureMessage != null) return;
            AsyncGPUReadback.Request(frame, 0, TextureFormat.BGRA32, readbackCallback);
        }

        public void Stop()
        {
            if (fd >= 0)
            {
                Close(fd);
                fd = -1;
            }
            if (devicePath != null)
            {
                ClaimedDevices.Remove(devicePath);
                devicePath = null;
            }
        }

        private void OnReadbackComplete(AsyncGPUReadbackRequest request)
        {
            if (fd < 0 || FailureMessage != null) return;
            if (request.hasError)
            {
                FailureMessage = "GPU readback for the video output failed.";
                return;
            }

            NativeArray<byte> data = request.GetData<byte>();
            byte* source = (byte*)data.GetUnsafeReadOnlyPtr();
            long total = data.Length;
            long offset = 0;
            while (offset < total)
            {
                long written = Write(fd, source + offset, (UIntPtr)(total - offset)).ToInt64();
                if (written < 0)
                {
                    int errno = Marshal.GetLastWin32Error();
                    if (errno == EINTR) continue;
                    FailureMessage = $"Writing to {devicePath} failed (errno {errno}).";
                    return;
                }
                offset += written;
            }
        }

        private static int OpenDevice(string overridePath, out string chosenPath)
        {
            if (!string.IsNullOrEmpty(overridePath))
            {
                chosenPath = overridePath;
                if (!ClaimedDevices.Add(overridePath)) return -1;
                int overrideFd = Open(overridePath, O_RDWR);
                if (overrideFd < 0) ClaimedDevices.Remove(overridePath);
                return overrideFd;
            }

            string[] candidates;
            try
            {
                candidates = Directory.GetFiles("/dev", "video*");
            }
            catch (Exception)
            {
                chosenPath = null;
                return -1;
            }
            Array.Sort(candidates);

            foreach (string candidate in candidates)
            {
                if (ClaimedDevices.Contains(candidate)) continue;
                int candidateFd = Open(candidate, O_RDWR);
                if (candidateFd < 0) continue;

                var capability = default(V4L2Capability);
                if (Ioctl(candidateFd, VIDIOC_QUERYCAP, &capability) == 0)
                {
                    uint caps = (capability.Capabilities & V4L2_CAP_DEVICE_CAPS) != 0 ? capability.DeviceCaps : capability.Capabilities;
                    if ((caps & V4L2_CAP_VIDEO_OUTPUT) != 0 && DriverNameMatches(capability.Driver))
                    {
                        chosenPath = candidate;
                        ClaimedDevices.Add(candidate);
                        return candidateFd;
                    }
                }
                Close(candidateFd);
            }

            chosenPath = null;
            return -1;
        }

        private static bool DriverNameMatches(byte* driver)
        {
            for (int Index = 0; Index < LoopbackDriverName.Length; Index++)
            {
                if (driver[Index] != (byte)LoopbackDriverName[Index]) return false;
            }
            return driver[LoopbackDriverName.Length] == 0;
        }
    }
#endif
}
