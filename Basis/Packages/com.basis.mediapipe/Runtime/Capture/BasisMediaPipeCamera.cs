using UnityEngine;

namespace Basis.MediaPipe
{
    /// <summary>Owns a WebCamTexture: device selection, start/stop and readiness. No inference.</summary>
    public sealed class BasisMediaPipeCamera
    {
        public WebCamTexture Texture { get; private set; }
        public string DeviceName { get; private set; }

        public bool IsRunning => Texture != null && Texture.isPlaying;
        public bool IsReady => IsRunning && Texture.didUpdateThisFrame && Texture.width > 16;

        public static WebCamDevice[] EnumerateDevices() => WebCamTexture.devices;

        public bool Start(string deviceName = null, int requestedWidth = 640, int requestedHeight = 480, int requestedFps = 30)
        {
            Stop();

            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
            {
                BasisDebug.LogError("BasisMediaPipe: no webcam devices found.");
                return false;
            }

            DeviceName = ResolveDeviceName(devices, deviceName);
            Texture = new WebCamTexture(DeviceName, requestedWidth, requestedHeight, requestedFps)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            Texture.Play();
            BasisDebug.Log($"BasisMediaPipe: started webcam '{DeviceName}' ({requestedWidth}x{requestedHeight}@{requestedFps}).");
            return true;
        }

        public void Stop()
        {
            if (Texture == null) return;
            if (Texture.isPlaying) Texture.Stop();
            Object.Destroy(Texture);
            Texture = null;
        }

        private static string ResolveDeviceName(WebCamDevice[] devices, string requested)
        {
            if (!string.IsNullOrEmpty(requested))
            {
                for (int i = 0; i < devices.Length; i++)
                {
                    if (devices[i].name == requested) return devices[i].name;
                }
                BasisDebug.LogError($"BasisMediaPipe: requested camera '{requested}' not found; using default.");
            }
            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i].isFrontFacing) return devices[i].name;
            }
            return devices[0].name;
        }
    }
}
