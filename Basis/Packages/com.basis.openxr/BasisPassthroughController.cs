using Basis.BasisUI;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using UnityEngine;

namespace Basis.OpenXR
{
    /// <summary>
    /// Bridges the <see cref="BasisSettingsDefaults.EnablePassthrough"/> setting to the
    /// <see cref="BasisPassthroughFeature"/> and the local camera: while passthrough is active it
    /// clears the main camera to transparent (and drops the skybox) so the real world shows through,
    /// restoring the world's original clear settings when it turns off. Only takes effect on
    /// standalone VR hardware with a runtime that supports the extension.
    /// </summary>
    public static class BasisPassthroughController
    {
        static bool s_Initialized;
        static bool s_DesiredActive;

        static bool s_Saved;
        static CameraClearFlags s_SavedFlags;
        static Color s_SavedBackground;
        static Material s_SavedSkybox;

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

        static void OnSettingChanged(bool _) => Apply();
        static void OnBootModeChanged(string _) => Apply();

        static void OnRenderSettingsApplied()
        {
            if (s_DesiredActive)
            {
                s_Saved = false;
                ApplyCameraClear(true);
            }
        }

        public static void NotifyRuntimeReady() => Apply();

        public static void NotifyRuntimeLost()
        {
            s_DesiredActive = false;
            ApplyCameraClear(false);
        }

        static void Apply()
        {
            bool want = BasisSettingsDefaults.EnablePassthrough.RawValue
                        && BasisDeviceManagement.IsMobileHardware()
                        && BasisDeviceManagement.IsCurrentModeVR()
                        && BasisPassthroughFeature.IsSupported;

            s_DesiredActive = want;
            BasisPassthroughFeature.SetActive(want);
            ApplyCameraClear(want);
        }

        static void ApplyCameraClear(bool on)
        {
            if (!BasisLocalCameraDriver.HasInstance)
            {
                return;
            }
            Camera cam = BasisLocalCameraDriver.Instance.Camera;
            if (cam == null)
            {
                return;
            }
            cam.TryGetComponent(out Skybox sky);

            if (on)
            {
                if (!s_Saved)
                {
                    s_SavedFlags = cam.clearFlags;
                    s_SavedBackground = cam.backgroundColor;
                    s_SavedSkybox = sky != null ? sky.material : null;
                    s_Saved = true;
                }
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                if (sky != null)
                {
                    sky.material = null;
                }
            }
            else if (s_Saved)
            {
                cam.clearFlags = s_SavedFlags;
                cam.backgroundColor = s_SavedBackground;
                if (sky != null)
                {
                    sky.material = s_SavedSkybox;
                }
                s_Saved = false;
            }
        }
    }
}
