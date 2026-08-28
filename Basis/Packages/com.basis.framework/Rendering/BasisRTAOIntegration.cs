// Ray traced ambient occlusion is optional: the define comes from the com.basis.rtao package being
// present (asmdef versionDefines), and neither the traced path nor the compute fallback is viable on
// mobile GPUs, so the whole integration compiles out on Android.
#if BASIS_HAS_RTAO && !UNITY_ANDROID
using Basis.BasisUI;
using Basis.Rendering.RTAO;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Settings;
using UnityEngine;

namespace Basis.Scripts.Rendering
{
    public static class BasisRTAOIntegration
    {
        private static bool installed;

        public static bool Installed => installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Install()
        {
            if (installed)
                return;

            installed = true;
            BasisRTAOFeature.CameraFilter = AcceptsCamera;
            BasisRTAOFeature.ViewerPosition = LocalViewerPosition;
            BasisSettingsSystem.OnSettingChanged += OnSettingChanged;
            BasisSettingsSystem.OnSettingsFinishedChanges += Apply;

            // An avatar that is not in the acceleration structure casts no contact shadow, and avatars change
            // far more often than the scene rescan interval, so every lifecycle event forces a refresh.
            BasisLocalPlayer.OnLocalAvatarChanged += OnLocalAvatarChanged;
            BasisNetworkPlayer.OnRemotePlayerJoined += OnRemotePlayerChanged;
            BasisNetworkPlayer.OnRemotePlayerLeft += OnRemotePlayerChanged;

            if (BasisSettingsSystem.SettingsLoaded)
                Apply();
        }

        public static void Uninstall()
        {
            if (!installed)
                return;

            installed = false;
            BasisSettingsSystem.OnSettingChanged -= OnSettingChanged;
            BasisSettingsSystem.OnSettingsFinishedChanges -= Apply;
            BasisLocalPlayer.OnLocalAvatarChanged -= OnLocalAvatarChanged;
            BasisNetworkPlayer.OnRemotePlayerJoined -= OnRemotePlayerChanged;
            BasisNetworkPlayer.OnRemotePlayerLeft -= OnRemotePlayerChanged;
            BasisRTAOFeature.CameraFilter = null;
            BasisRTAOFeature.ViewerPosition = null;
            ClearOverrides();
        }

        // Mirrors and the handheld camera are separate Game cameras. A mirror that shows the room without
        // its contact shadows reads as a different room, and a photo that does not match the view is worse
        // still, so they are included by default and the setting is what buys the frame time back.
        public static bool AcceptsCamera(Camera camera)
        {
            if (camera == null)
                return false;
            if (!BasisLocalCameraDriver.HasInstance || BasisLocalCameraDriver.CameraInstance == null)
                return true;
            if (ReferenceEquals(camera, BasisLocalCameraDriver.CameraInstance))
                return true;
            return BasisRTAOFeature.AllowSecondaryCameras;
        }

        private static Vector3 LocalViewerPosition()
        {
            return BasisLocalCameraDriver.HasInstance ? BasisLocalCameraDriver.Position : Vector3.zero;
        }

        private static void OnLocalAvatarChanged()
        {
            BasisRTAOFeature.MarkSceneDirty();
        }

        private static void OnRemotePlayerChanged(BasisNetworkPlayer networkPlayer, BasisRemotePlayer remotePlayer)
        {
            BasisRTAOFeature.MarkSceneDirty();
        }

        private static void OnSettingChanged(string key, string value)
        {
            if (!IsRtaoKey(key))
                return;
            Apply();
        }

        public static bool IsRtaoKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            string lowered = key.ToLowerInvariant();
            return lowered == BasisSettingsDefaults.UseRayTracedAmbientOcclusion.BindingKey
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionMode.BindingKey
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionQuality.BindingKey
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionIntensity.BindingKey
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionRadius.BindingKey
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionSkinnedMeshes.BindingKey
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionDirectStrength.BindingKey
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionDenoise.BindingKey
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionOtherCameras.BindingKey
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionApply.BindingKey
                || lowered == BasisSettingsDefaults.DevRtaoDebugView.BindingKey
                || lowered == BasisSettingsDefaults.DevRtaoDebugStage.BindingKey;
        }

        public static void Apply()
        {
            BasisRTAOFeature.RuntimeEnabled = BasisSettingsDefaults.UseRayTracedAmbientOcclusion.RawValue;

            BasisRTAOFeature.HasTracingModeOverride = true;
            BasisRTAOFeature.TracingModeOverride = BasisRTAOSettingsMap.ReadMode(BasisSettingsDefaults.RayTracedAmbientOcclusionMode.RawValue);

            BasisRTAOFeature.HasQualityOverride = true;
            BasisRTAOFeature.QualityOverride = ClampQuality(BasisRTAOSettingsMap.ReadQuality(BasisSettingsDefaults.RayTracedAmbientOcclusionQuality.RawValue));

            BasisRTAOFeature.HasIntensityOverride = true;
            // Clamped to the slider's own range. A value saved before the range narrowed would otherwise
            // keep being applied at its old strength, with no way to reproduce or undo it from the UI.
            BasisRTAOFeature.IntensityOverride = Mathf.Clamp(
                BasisSettingsDefaults.RayTracedAmbientOcclusionIntensity.RawValue,
                BasisSettingsDefaults.RTAO_INTENSITY_MIN, BasisSettingsDefaults.RTAO_INTENSITY_MAX);

            BasisRTAOFeature.HasRadiusOverride = true;
            BasisRTAOFeature.RadiusOverride = Mathf.Clamp(
                BasisSettingsDefaults.RayTracedAmbientOcclusionRadius.RawValue,
                BasisSettingsDefaults.RTAO_RADIUS_MIN, BasisSettingsDefaults.RTAO_RADIUS_MAX);

            BasisRTAOFeature.HasDirectStrengthOverride = true;
            BasisRTAOFeature.DirectStrengthOverride = BasisSettingsDefaults.RayTracedAmbientOcclusionDirectStrength.RawValue;

            BasisRTAOFeature.HasDenoisePassesOverride = true;
            BasisRTAOFeature.DenoisePassesOverride = BasisRTAOSettingsMap.ReadDenoisePasses(BasisSettingsDefaults.RayTracedAmbientOcclusionDenoise.RawValue);

            BasisRTAOFeature.HasSkinnedModeOverride = true;
            BasisRTAOFeature.SkinnedModeOverride = BasisRTAOSettingsMap.ReadSkinnedMode(BasisSettingsDefaults.RayTracedAmbientOcclusionSkinnedMeshes.RawValue);

            BasisRTAOFeature.AllowSecondaryCameras = BasisSettingsDefaults.RayTracedAmbientOcclusionOtherCameras.RawValue;

            BasisRTAOFeature.HasApplyModeOverride = true;
            BasisRTAOFeature.ApplyModeOverride = BasisRTAOSettingsMap.ReadApplyMode(BasisSettingsDefaults.RayTracedAmbientOcclusionApply.RawValue);

            BasisRTAOFeature.HasDebugViewOverride = true;
            BasisRTAOFeature.DebugViewOverride = BasisSettingsDefaults.DevRtaoDebugView.RawValue;

            BasisRTAOFeature.HasDebugStageOverride = true;
            BasisRTAOFeature.DebugStageOverride = BasisRTAOSettingsMap.ReadDebugStage(BasisSettingsDefaults.DevRtaoDebugStage.RawValue);
        }

        public static void ClearOverrides()
        {
            BasisRTAOFeature.RuntimeEnabled = true;
            BasisRTAOFeature.AllowSecondaryCameras = true;
            BasisRTAOFeature.HasTracingModeOverride = false;
            BasisRTAOFeature.HasQualityOverride = false;
            BasisRTAOFeature.HasIntensityOverride = false;
            BasisRTAOFeature.HasRadiusOverride = false;
            BasisRTAOFeature.HasDirectStrengthOverride = false;
            BasisRTAOFeature.HasDenoisePassesOverride = false;
            BasisRTAOFeature.HasSkinnedModeOverride = false;
            BasisRTAOFeature.HasApplyModeOverride = false;
            BasisRTAOFeature.HasDebugStageOverride = false;
            BasisRTAOFeature.HasDebugViewOverride = false;
        }

        // The graphics quality level caps how much of the frame this may take, the same way shadows and
        // HDR clamp themselves, rather than writing the player's dropdown back down.
        public static BasisRTAOQuality ClampQuality(BasisRTAOQuality requested)
        {
            return ClampQuality(requested, BasisQualityTier.Current);
        }

        public static BasisRTAOQuality ClampQuality(BasisRTAOQuality requested, int tier)
        {
            if (tier <= BasisQualityTier.VeryLow)
                return BasisRTAOQuality.Low;
            if (tier <= BasisQualityTier.Low && requested > BasisRTAOQuality.Medium)
                return BasisRTAOQuality.Medium;
            if (tier <= BasisQualityTier.Medium && requested > BasisRTAOQuality.High)
                return BasisRTAOQuality.High;
            return requested;
        }
    }
}
#endif
