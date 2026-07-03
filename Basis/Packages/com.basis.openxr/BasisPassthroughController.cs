using Basis.BasisUI;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using UnityEngine;

namespace Basis.OpenXR
{
    /// <summary>
    /// Bridges the <see cref="BasisSettingsDefaults.EnablePassthrough"/> setting to
    /// <see cref="BasisPassthroughFeature"/> and the local camera. While active it clears the main
    /// camera to transparent and removes the skybox (both the camera component and RenderSettings)
    /// so the real world shows through; it saves and restores the world's values on toggle and
    /// re-captures them on each scene load. Only takes effect on standalone VR hardware whose
    /// runtime supports passthrough.
    /// </summary>
    public static class BasisPassthroughController
    {
        const string Tag = "[BasisPassthrough]";

        static bool s_Initialized;
        static bool s_DesiredActive;

        static bool s_Saved;
        static CameraClearFlags s_SavedFlags;
        static Color s_SavedBackground;
        static Material s_SavedCameraSky;
        static Material s_SavedRenderSky;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            if (s_Initialized)
            {
                return;
            }
            s_Initialized = true;
            BasisSettingsDefaults.EnablePassthrough.OnChanged += OnSettingChanged;
            BasisLocalCameraDriver.RenderSettingsApplied += OnRenderSettingsApplied;
            BasisDeviceManagement.OnBootModeChanged += OnBootModeChanged;
            Apply();
        }

        static void OnSettingChanged(bool value)
        {
            Debug.Log($"{Tag} Setting changed -> {value}");
            Apply();
        }

        static void OnBootModeChanged(string mode)
        {
            Debug.Log($"{Tag} Boot mode changed -> {mode}");
            Apply();
        }

        static void OnRenderSettingsApplied()
        {
            if (s_DesiredActive)
            {
                Debug.Log($"{Tag} Scene loaded while active — recapturing world visuals and re-applying passthrough clear.");
                s_Saved = false;
                ApplyVisuals(true);
            }
        }

        public static void NotifyRuntimeReady()
        {
            Debug.Log($"{Tag} Runtime ready (IsSupported={BasisPassthroughFeature.IsSupported}).");
            Apply();
        }

        public static void NotifyRuntimeLost()
        {
            Debug.Log($"{Tag} Runtime lost — restoring world visuals.");
            s_DesiredActive = false;
            ApplyVisuals(false);
        }

        static void Apply()
        {
            bool setting = BasisSettingsDefaults.EnablePassthrough.RawValue;
            bool mobile = BasisDeviceManagement.IsMobileHardware();
            bool vr = BasisDeviceManagement.IsCurrentModeVR();
            bool supported = BasisPassthroughFeature.IsSupported;
            bool want = setting && mobile && vr && supported;

            Debug.Log($"{Tag} Apply: setting={setting} mobile={mobile} vr={vr} supported={supported} -> want={want}");

            s_DesiredActive = want;
            BasisPassthroughFeature.SetActive(want);
            ApplyVisuals(want);
        }

        static void ApplyVisuals(bool on)
        {
            if (!BasisLocalCameraDriver.HasInstance)
            {
                Debug.Log($"{Tag} ApplyVisuals({on}) skipped — camera not ready yet (will re-apply on scene load).");
                return;
            }
            Camera cam = BasisLocalCameraDriver.Instance.Camera;
            if (cam == null)
            {
                return;
            }
            cam.TryGetComponent(out Skybox camSky);

            if (on)
            {
                if (!s_Saved)
                {
                    s_SavedFlags = cam.clearFlags;
                    s_SavedBackground = cam.backgroundColor;
                    s_SavedCameraSky = camSky != null ? camSky.material : null;
                    s_SavedRenderSky = RenderSettings.skybox;
                    s_Saved = true;
                }
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                if (camSky != null)
                {
                    camSky.material = null;
                }
                RenderSettings.skybox = null;
                Debug.Log($"{Tag} Visuals ON — clear=SolidColor(a=0), skybox removed.");
            }
            else if (s_Saved)
            {
                cam.clearFlags = s_SavedFlags;
                cam.backgroundColor = s_SavedBackground;
                if (camSky != null)
                {
                    camSky.material = s_SavedCameraSky;
                }
                RenderSettings.skybox = s_SavedRenderSky;
                s_Saved = false;
                Debug.Log($"{Tag} Visuals OFF — restored clear/skybox.");
            }
        }
    }
}
