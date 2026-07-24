using Basis.Scripts.Device_Management;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace Basis.Scripts.Rendering
{
    public static class BasisDynamicResolution
    {
        public const float MinimumSupportedScale = 0.25f;
        public const float MaximumSupportedScale = 2f;

        public static bool Enabled { get; private set; }
        public static float UserRenderScale { get; private set; } = 1f;
        public static bool ExternalAllocationOwner { get; set; }

        /// <summary>
        /// Raised when the eye texture allocation a backend should target has moved. Backends that
        /// own their own allocation (OpenVR, which sizes against the compositor recommendation)
        /// subscribe so a slider change applies on the next frame instead of waiting for their poll.
        /// </summary>
        public static event System.Action OnAllocationScaleChanged;

        static BasisDynamicResolutionSettings Settings = BasisDynamicResolutionSettings.Default();
        static BasisDynamicResolutionState State = new BasisDynamicResolutionState { Scale = 1f };
        static float TargetFrameRateOverride;
        static FrameTiming[] Timings = new FrameTiming[1];
        static float LastAppliedViewport = -1f;
        static float LastAppliedRenderScale = -1f;
        static float LastAppliedEyeTextureScale = -1f;
        static float LastNotifiedAllocationScale = -1f;

        public static float MaximumScale => Enabled ? Settings.MaximumScale : 1f;

        public static float CurrentScale => Enabled ? State.Scale : 1f;

        public static float AllocationScale => UserRenderScale * MaximumScale;

        public static float SmoothedGpuMilliseconds => State.SmoothedGpuMilliseconds;

        public static void Configure(bool enabled, float minimumScale, float maximumScale, float targetFrameRate)
        {
            if (maximumScale < minimumScale)
            {
                maximumScale = minimumScale;
            }

            Settings.MinimumScale = Mathf.Clamp(minimumScale, MinimumSupportedScale, MaximumSupportedScale);
            Settings.MaximumScale = Mathf.Clamp(maximumScale, MinimumSupportedScale, MaximumSupportedScale);
            TargetFrameRateOverride = targetFrameRate;

            if (Enabled != enabled)
            {
                Enabled = enabled;
                State.Scale = Settings.MaximumScale;
                State.SmoothedGpuMilliseconds = 0f;
                State.FramesSinceChange = 0;
            }

            State.Scale = Mathf.Clamp(State.Scale, Settings.MinimumScale, Settings.MaximumScale);
            NotifyAllocationScale();
            Apply();
        }

        public static void SetUserRenderScale(float scale)
        {
            UserRenderScale = Mathf.Clamp(scale, MinimumSupportedScale, MaximumSupportedScale);
            NotifyAllocationScale();
            Apply();
        }

        static void NotifyAllocationScale()
        {
            float allocation = AllocationScale;
            if (Mathf.Approximately(LastNotifiedAllocationScale, allocation))
            {
                return;
            }

            LastNotifiedAllocationScale = allocation;
            OnAllocationScaleChanged?.Invoke();
        }

        public static void Tick()
        {
            if (Enabled)
            {
                float gpuMilliseconds = SampleGpuMilliseconds();
                float targetMilliseconds = ResolveTargetMilliseconds();

                if (BasisDynamicResolutionPolicy.Evaluate(Settings, ref State, gpuMilliseconds, targetMilliseconds))
                {
                    Apply();
                    return;
                }
            }

            Apply();
        }

        static float SampleGpuMilliseconds()
        {
            FrameTimingManager.CaptureFrameTimings();
            uint captured = FrameTimingManager.GetLatestTimings(1, Timings);
            if (captured == 0)
            {
                return 0f;
            }

            return (float)Timings[0].gpuFrameTime;
        }

        static float ResolveTargetMilliseconds()
        {
            if (TargetFrameRateOverride > 0f)
            {
                return 1000f / TargetFrameRateOverride;
            }

            if (BasisDeviceManagement.IsCurrentModeVR())
            {
                XRDisplaySubsystem display = XRDisplaySubsystem.activeSubsystemOrStub;
                if (display != null && display.TryGetDisplayRefreshRate(out float headsetRefresh) && headsetRefresh > 0f)
                {
                    return 1000f / headsetRefresh;
                }
            }

            int applicationTarget = Application.targetFrameRate;
            if (applicationTarget > 0)
            {
                return 1000f / applicationTarget;
            }

            double screenRefresh = Screen.currentResolution.refreshRateRatio.value;
            if (screenRefresh > 0d)
            {
                return (float)(1000d / screenRefresh);
            }

            return 1000f / 60f;
        }

        static void Apply()
        {
            if (BasisDeviceManagement.IsCurrentModeVR())
            {
                ApplyVirtualReality();
            }
            else
            {
                ApplyDesktop();
            }
        }

        static void ApplyVirtualReality()
        {
            float maximum = MaximumScale;
            float viewport = maximum > 0f ? CurrentScale / maximum : 1f;
            viewport = Mathf.Clamp(viewport, 0.1f, 1f);

            if (!Mathf.Approximately(LastAppliedViewport, viewport))
            {
                XRSettings.renderViewportScale = viewport;
                LastAppliedViewport = viewport;
            }

            if (!ExternalAllocationOwner)
            {
                float allocation = Mathf.Clamp(AllocationScale, MinimumSupportedScale, MaximumSupportedScale);
                if (!Mathf.Approximately(LastAppliedEyeTextureScale, allocation))
                {
                    XRSettings.eyeTextureResolutionScale = allocation;
                    LastAppliedEyeTextureScale = allocation;
                }
            }

            ApplyPipelineRenderScale(1f);
        }

        static void ApplyDesktop()
        {
            if (!Mathf.Approximately(LastAppliedViewport, 1f))
            {
                XRSettings.renderViewportScale = 1f;
                LastAppliedViewport = 1f;
            }

            ApplyPipelineRenderScale(Mathf.Clamp(UserRenderScale * CurrentScale, 0.1f, MaximumSupportedScale));
        }

        static void ApplyPipelineRenderScale(float renderScale)
        {
            if (Mathf.Approximately(LastAppliedRenderScale, renderScale))
            {
                return;
            }

            UniversalRenderPipelineAsset asset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            if (asset == null)
            {
                return;
            }

            if (asset.renderScale != renderScale)
            {
                asset.renderScale = renderScale;
            }

            LastAppliedRenderScale = renderScale;
        }

        public static void ResetAppliedState()
        {
            LastAppliedViewport = -1f;
            LastAppliedRenderScale = -1f;
            LastAppliedEyeTextureScale = -1f;
        }
    }
}
