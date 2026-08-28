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
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;

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
#if BASIS_HAS_GI
            BasisRTAOFeature.SharedStructureProvider = ProvideSharedStructure;
            BasisRTAOFeature.SharedStructureBuilder = BuildSharedStructure;
#endif
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
#if BASIS_HAS_GI
            BasisRTAOFeature.SharedStructureProvider = null;
            BasisRTAOFeature.SharedStructureBuilder = null;
#endif
            ClearOverrides();
        }

#if BASIS_HAS_GI
        /// <summary>
        /// The acceleration structure global illumination is already tracing, when it holds everything
        /// ambient occlusion asked for.
        ///
        /// Both effects trace the same avatars and the same world, and building that twice a frame - two
        /// scans of the scene, two transform sweeps, two builds, two copies of every avatar's capsules -
        /// is the single largest thing they duplicate. Sharing costs nothing in fidelity: the instances
        /// carry a category mask and each effect's rays only walk the half it asked for.
        ///
        /// Only ever borrowed, never widened. If global illumination is tracing a narrower set than
        /// occlusion wants - the player asked for World + Avatars here and Avatars there - the structure
        /// simply does not contain the geometry, and no mask can add it back, so occlusion keeps its own.
        /// </summary>
        private static IRayTracingAccelStruct ProvideSharedStructure(byte wanted)
        {
            BasisGlobalIlluminationSettings settings = BasisGlobalIlluminationSettings.Current;
            if (settings == null || !settings.enable || settings.mode != BasisGlobalIlluminationMode.RayTraced)
                return null;

            BasisGlobalIlluminationRayTracer tracer = BasisGlobalIlluminationRayTracer.Instance;
            if (tracer == null || tracer.Scene == null || !tracer.Scene.HasGeometry)
                return null;

            byte held = settings.TraceCategories;
            if ((held & wanted) != wanted)
                return null;

            return tracer.Scene.AccelerationStructure;
        }

        /// <summary>
        /// Records the shared structure's build if it still needs one.
        ///
        /// The occlusion pass runs at AfterRenderingPrePasses and the global illumination pass runs after
        /// the opaques, so the borrower is the one that gets there first and has to do the building. Build
        /// clears the dirty flag, so the later pass finds nothing to do rather than building it twice.
        /// </summary>
        private static void BuildSharedStructure(CommandBuffer cmd)
        {
            BasisGlobalIlluminationRayTracer tracer = BasisGlobalIlluminationRayTracer.Instance;
            if (tracer?.Scene != null && tracer.Scene.NeedsBuild)
                tracer.Scene.Build(cmd);
        }
#endif

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
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionLayers.BindingKey
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionNormalBias.BindingKey
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionDistanceBias.BindingKey
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionFalloff.BindingKey
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionPower.BindingKey
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionFadeStart.BindingKey
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionFadeEnd.BindingKey
                || lowered == BasisSettingsDefaults.RayTracedAmbientOcclusionSpecularRelief.BindingKey
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

            BasisRTAOFeature.HasLayerMaskOverride = true;
            BasisRTAOFeature.LayerMaskOverride = BasisRTAOSettingsMap.ReadLayers(BasisSettingsDefaults.RayTracedAmbientOcclusionLayers.RawValue);

            BasisRTAOFeature.HasSkinnedModeOverride = true;
            BasisRTAOFeature.SkinnedModeOverride = BasisRTAOSettingsMap.ReadSkinnedMode(BasisSettingsDefaults.RayTracedAmbientOcclusionSkinnedMeshes.RawValue);

            BasisRTAOFeature.HasNormalBiasOverride = true;
            BasisRTAOFeature.NormalBiasOverride = BasisSettingsDefaults.RayTracedAmbientOcclusionNormalBias.RawValue;

            BasisRTAOFeature.HasDistanceBiasOverride = true;
            BasisRTAOFeature.DistanceBiasOverride = BasisSettingsDefaults.RayTracedAmbientOcclusionDistanceBias.RawValue;

            BasisRTAOFeature.HasFalloffOverride = true;
            BasisRTAOFeature.FalloffOverride = BasisSettingsDefaults.RayTracedAmbientOcclusionFalloff.RawValue;

            BasisRTAOFeature.HasPowerOverride = true;
            BasisRTAOFeature.PowerOverride = BasisSettingsDefaults.RayTracedAmbientOcclusionPower.RawValue;

            BasisRTAOFeature.HasFadeStartOverride = true;
            BasisRTAOFeature.FadeStartOverride = BasisSettingsDefaults.RayTracedAmbientOcclusionFadeStart.RawValue;

            BasisRTAOFeature.HasFadeEndOverride = true;
            BasisRTAOFeature.FadeEndOverride = BasisSettingsDefaults.RayTracedAmbientOcclusionFadeEnd.RawValue;

            BasisRTAOFeature.HasSpecularOcclusionOverride = true;
            BasisRTAOFeature.SpecularOcclusionReliefOverride = BasisSettingsDefaults.RayTracedAmbientOcclusionSpecularRelief.RawValue;

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
            BasisRTAOFeature.HasLayerMaskOverride = false;
            BasisRTAOFeature.HasSkinnedModeOverride = false;
            BasisRTAOFeature.HasNormalBiasOverride = false;
            BasisRTAOFeature.HasDistanceBiasOverride = false;
            BasisRTAOFeature.HasFalloffOverride = false;
            BasisRTAOFeature.HasPowerOverride = false;
            BasisRTAOFeature.HasFadeStartOverride = false;
            BasisRTAOFeature.HasFadeEndOverride = false;
            BasisRTAOFeature.HasSpecularOcclusionOverride = false;
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
