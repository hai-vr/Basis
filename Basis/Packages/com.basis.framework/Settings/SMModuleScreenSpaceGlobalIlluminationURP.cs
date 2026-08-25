using System;
using System.Collections.Generic;
using Basis.BasisUI;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SMModuleScreenSpaceGlobalIlluminationURP : BasisSettingsBase
{
    public const float OverridePriority = 1000f;
    public const int CaptureSampleCount = 16;
    public const int CaptureMaxRaySteps = 64;
    private static readonly HashSet<Camera> registeredCameras = new HashSet<Camera>();
    private static readonly HashSet<Camera> suspendedCameras = new HashSet<Camera>();
    private static SMModuleScreenSpaceGlobalIlluminationURP instance;
    private readonly Dictionary<BasisRemotePlayer, Action> remoteAvatarHooks = new Dictionary<BasisRemotePlayer, Action>();
    private readonly Dictionary<int, Volume> cameraVolumes = new Dictionary<int, Volume>();
    private bool ssgiEnabled = BasisSettingsDefaults.UseScreenSpaceGlobalIllumination.DefaultValue.GetDefault();
    private ScreenSpaceGlobalIlluminationVolume.QualityMode quality = ReadQuality(BasisSettingsDefaults.ScreenSpaceGlobalIlluminationQuality.DefaultValue.GetDefault());
    private bool fullResolution = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationFullResolution.DefaultValue.GetDefault();
    private float intensity = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationIntensity.DefaultValue.GetDefault();
    private bool gBufferFallback = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationGBufferFallback.DefaultValue.GetDefault();
    private float fallbackAlbedo = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationFallbackAlbedo.DefaultValue.GetDefault();
    private bool reflectionProbes = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationReflectionProbes.DefaultValue.GetDefault();
    private bool highQualityUpscaling = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationHighQualityUpscaling.DefaultValue.GetDefault();
    private bool overrideAmbient = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationOverrideAmbient.DefaultValue.GetDefault();
    private bool backfaceLighting = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationBackfaceLighting.DefaultValue.GetDefault();
    private float denoiseStrength = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationDenoiseStrength.DefaultValue.GetDefault();
    private bool capturing;
    private Volume volume;
    private ScreenSpaceGlobalIlluminationVolume ssgi;
    public Volume Volume => volume;
    public ScreenSpaceGlobalIlluminationVolume Ssgi => ssgi;
    public IReadOnlyDictionary<int, Volume> CameraVolumes => cameraVolumes;
    public bool Capturing => capturing;
    private static string K_USE_SSGI => BasisSettingsDefaults.UseScreenSpaceGlobalIllumination.BindingKey;
    private static string K_SSGI_QUALITY => BasisSettingsDefaults.ScreenSpaceGlobalIlluminationQuality.BindingKey;
    private static string K_SSGI_FULL_RESOLUTION => BasisSettingsDefaults.ScreenSpaceGlobalIlluminationFullResolution.BindingKey;
    private static string K_SSGI_INTENSITY => BasisSettingsDefaults.ScreenSpaceGlobalIlluminationIntensity.BindingKey;
    private static string K_SSGI_DEBUG_VIEW => BasisSettingsDefaults.DevSsgiDebugView.BindingKey;
    private static string K_SSGI_GBUFFER_FALLBACK => BasisSettingsDefaults.ScreenSpaceGlobalIlluminationGBufferFallback.BindingKey;
    private static string K_SSGI_FALLBACK_ALBEDO => BasisSettingsDefaults.ScreenSpaceGlobalIlluminationFallbackAlbedo.BindingKey;
    private static string K_SSGI_REFLECTION_PROBES => BasisSettingsDefaults.ScreenSpaceGlobalIlluminationReflectionProbes.BindingKey;
    private static string K_SSGI_HQ_UPSCALING => BasisSettingsDefaults.ScreenSpaceGlobalIlluminationHighQualityUpscaling.BindingKey;
    private static string K_SSGI_OVERRIDE_AMBIENT => BasisSettingsDefaults.ScreenSpaceGlobalIlluminationOverrideAmbient.BindingKey;
    private static string K_SSGI_BACKFACE_LIGHTING => BasisSettingsDefaults.ScreenSpaceGlobalIlluminationBackfaceLighting.BindingKey;
    private static string K_SSGI_DENOISE => BasisSettingsDefaults.ScreenSpaceGlobalIlluminationDenoiseStrength.BindingKey;

    public override void Awake()
    {
        base.Awake();
        instance = this;
        ScreenSpaceGlobalIlluminationURP.CameraFilter = AcceptsCamera;
        ScreenSpaceGlobalIlluminationURP.KeepRenderingWithDebugger = true;
        BasisLocalPlayer.OnLocalAvatarChanged += RegisterLocalAvatar;
        BasisNetworkPlayer.OnRemotePlayerJoined += OnRemotePlayerJoined;
        BasisNetworkPlayer.OnRemotePlayerLeft += OnRemotePlayerLeft;
        RegisterLocalAvatar();
        foreach (BasisRemotePlayer remote in BasisNetworkPlayers.RemotePlayers.Values)
        {
            HookRemotePlayer(remote);
        }
        ApplyOverride();
    }

    public new void OnDestroy()
    {
        base.OnDestroy();
        BasisLocalPlayer.OnLocalAvatarChanged -= RegisterLocalAvatar;
        BasisNetworkPlayer.OnRemotePlayerJoined -= OnRemotePlayerJoined;
        BasisNetworkPlayer.OnRemotePlayerLeft -= OnRemotePlayerLeft;
        foreach (KeyValuePair<BasisRemotePlayer, Action> hook in remoteAvatarHooks)
        {
            if (hook.Key != null)
            {
                hook.Key.OnAvatarSwitched -= hook.Value;
            }
        }
        remoteAvatarHooks.Clear();
        RestoreAuthoredFeatureValues();
        if (instance == this)
        {
            instance = null;
        }
    }

    public static int RegisterAvatar(BasisAvatar avatar)
    {
        return avatar == null ? 0 : ScreenSpaceGlobalIlluminationURP.RegisterRenderers(avatar.gameObject);
    }

    private static void RegisterLocalAvatar()
    {
        BasisLocalPlayer local = BasisLocalPlayer.Instance;
        if (local != null)
        {
            RegisterAvatar(local.BasisAvatar);
        }
    }

    private void OnRemotePlayerJoined(BasisNetworkPlayer networkPlayer, BasisRemotePlayer remote)
    {
        HookRemotePlayer(remote);
    }

    private void HookRemotePlayer(BasisRemotePlayer remote)
    {
        if (remote == null || remoteAvatarHooks.ContainsKey(remote))
        {
            return;
        }
        Action hook = () => RegisterAvatar(remote.BasisAvatar);
        remote.OnAvatarSwitched += hook;
        remoteAvatarHooks[remote] = hook;
        RegisterAvatar(remote.BasisAvatar);
    }

    private void OnRemotePlayerLeft(BasisNetworkPlayer networkPlayer, BasisRemotePlayer remote)
    {
        if (remote != null && remoteAvatarHooks.TryGetValue(remote, out Action hook))
        {
            remote.OnAvatarSwitched -= hook;
            remoteAvatarHooks.Remove(remote);
        }
    }

    public static bool AcceptsCamera(Camera camera)
    {
        Camera localCamera = BasisLocalCameraDriver.CameraInstance;
        if (localCamera == null || ReferenceEquals(camera, localCamera))
        {
            return true;
        }
        return registeredCameras.Contains(camera) && !suspendedCameras.Contains(camera);
    }

    public static bool IsCameraRegistered(Camera camera)
    {
        return !(camera is null) && registeredCameras.Contains(camera);
    }

    public static void RegisterCamera(Camera camera)
    {
        if (camera == null)
        {
            return;
        }
        registeredCameras.Add(camera);
        if (instance != null)
        {
            instance.ApplyOverride();
        }
    }

    public static void UnregisterCamera(Camera camera)
    {
        if (camera is null)
        {
            return;
        }
        registeredCameras.Remove(camera);
        suspendedCameras.Remove(camera);
    }

    public static void SuspendCamera(Camera camera, bool suspended)
    {
        if (camera is null)
        {
            return;
        }
        if (suspended)
        {
            suspendedCameras.Add(camera);
        }
        else
        {
            suspendedCameras.Remove(camera);
        }
    }

    public static void BeginCapture(Camera camera)
    {
        if (instance == null || instance.capturing || !instance.ssgiEnabled || !IsCameraRegistered(camera))
        {
            return;
        }
        instance.capturing = true;
        instance.ApplyOverride();
    }

    public static void EndCapture()
    {
        if (instance == null || !instance.capturing)
        {
            return;
        }
        instance.capturing = false;
        instance.ApplyOverride();
    }

    public static int UncoveredVolumeLayer(Camera camera)
    {
        if (camera == null || !camera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
        {
            return -1;
        }
        int mask = cameraData.volumeLayerMask.value;
        if ((mask & 1) != 0)
        {
            return -1;
        }
        for (int layer = 1; layer < 32; layer++)
        {
            if ((mask & (1 << layer)) != 0)
            {
                return layer;
            }
        }
        return -1;
    }

    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        if (matchedSettingName == K_USE_SSGI)
        {
            ssgiEnabled = optionValue == "true";
        }
        else if (matchedSettingName == K_SSGI_QUALITY)
        {
            quality = ReadQuality(optionValue);
        }
        else if (matchedSettingName == K_SSGI_FULL_RESOLUTION)
        {
            fullResolution = optionValue == "true";
        }
        else if (matchedSettingName == K_SSGI_INTENSITY)
        {
            if (!SliderReadOption(optionValue, out float value))
            {
                return;
            }
            intensity = value;
        }
        else if (matchedSettingName == K_SSGI_DEBUG_VIEW)
        {
            ScreenSpaceGlobalIlluminationURP.DebugView = ReadDebugView(optionValue);
            return;
        }
        else if (matchedSettingName == K_SSGI_GBUFFER_FALLBACK)
        {
            gBufferFallback = optionValue == "true";
        }
        else if (matchedSettingName == K_SSGI_FALLBACK_ALBEDO)
        {
            if (!SliderReadOption(optionValue, out float albedo))
            {
                return;
            }
            fallbackAlbedo = albedo;
        }
        else if (matchedSettingName == K_SSGI_REFLECTION_PROBES)
        {
            reflectionProbes = optionValue == "true";
        }
        else if (matchedSettingName == K_SSGI_HQ_UPSCALING)
        {
            highQualityUpscaling = optionValue == "true";
        }
        else if (matchedSettingName == K_SSGI_OVERRIDE_AMBIENT)
        {
            overrideAmbient = optionValue == "true";
        }
        else if (matchedSettingName == K_SSGI_BACKFACE_LIGHTING)
        {
            backfaceLighting = optionValue == "true";
        }
        else if (matchedSettingName == K_SSGI_DENOISE)
        {
            if (!SliderReadOption(optionValue, out float denoise))
            {
                return;
            }
            denoiseStrength = denoise;
        }
        else
        {
            return;
        }
        ApplyOverride();
    }

    public override void ChangedSettings()
    {
        ssgiEnabled = BasisSettingsDefaults.UseScreenSpaceGlobalIllumination.RawValue;
        quality = ReadQuality(BasisSettingsDefaults.ScreenSpaceGlobalIlluminationQuality.RawValue);
        fullResolution = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationFullResolution.RawValue;
        intensity = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationIntensity.RawValue;
        gBufferFallback = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationGBufferFallback.RawValue;
        fallbackAlbedo = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationFallbackAlbedo.RawValue;
        reflectionProbes = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationReflectionProbes.RawValue;
        highQualityUpscaling = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationHighQualityUpscaling.RawValue;
        overrideAmbient = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationOverrideAmbient.RawValue;
        backfaceLighting = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationBackfaceLighting.RawValue;
        denoiseStrength = BasisSettingsDefaults.ScreenSpaceGlobalIlluminationDenoiseStrength.RawValue;
        ScreenSpaceGlobalIlluminationURP.DebugView = ReadDebugView(BasisSettingsDefaults.DevSsgiDebugView.RawValue);
        ApplyOverride();
    }

    public static ScreenSpaceGlobalIlluminationURP.DebugViewMode ReadDebugView(string optionValue)
    {
        switch (optionValue?.ToLowerInvariant())
        {
            case "indirect light": return ScreenSpaceGlobalIlluminationURP.DebugViewMode.IndirectLight;
            case "gi contribution": return ScreenSpaceGlobalIlluminationURP.DebugViewMode.GlobalIlluminationContribution;
            case "gbuffer albedo": return ScreenSpaceGlobalIlluminationURP.DebugViewMode.GBufferAlbedo;
            case "gbuffer normals": return ScreenSpaceGlobalIlluminationURP.DebugViewMode.GBufferNormals;
            default: return ScreenSpaceGlobalIlluminationURP.DebugViewMode.None;
        }
    }

    public static ScreenSpaceGlobalIlluminationVolume.QualityMode ReadQuality(string optionValue)
    {
        switch (optionValue?.ToLowerInvariant())
        {
            case "low": return ScreenSpaceGlobalIlluminationVolume.QualityMode.Low;
            case "high": return ScreenSpaceGlobalIlluminationVolume.QualityMode.High;
            default: return ScreenSpaceGlobalIlluminationVolume.QualityMode.Medium;
        }
    }

    public void ApplyOverride()
    {
        EnsureVolume();
        Apply(ssgi, ssgiEnabled, quality, fullResolution, intensity, capturing, denoiseStrength);
        volume.gameObject.SetActive(true);
        EnsureCameraVolumes();
        ApplyFeature();
    }

    /// <summary>
    /// Pushes the renderer-level options onto the feature itself. These are serialized on the renderer
    /// asset rather than on a Volume, so they cannot be driven through the volume stack like the rest.
    /// </summary>
    public void ApplyFeature()
    {
        ScreenSpaceGlobalIlluminationURP feature = FindFeature();
        RememberAuthoredFeatureValues(feature);
        Apply(feature, gBufferFallback, fallbackAlbedo, reflectionProbes, highQualityUpscaling, overrideAmbient, backfaceLighting);
    }

    // The feature is a sub-asset of the renderer, not a scene object, so writing to it in the editor
    // edits the project. The authored values are kept so play mode leaves the asset as it found it.
    private bool hasAuthoredFeatureValues;
    private bool authoredGBufferFallback;
    private float authoredFallbackAlbedo;
    private bool authoredReflectionProbes;
    private bool authoredHighQualityUpscaling;
    private bool authoredOverrideAmbient;
    private bool authoredBackfaceLighting;

    private void RememberAuthoredFeatureValues(ScreenSpaceGlobalIlluminationURP feature)
    {
        if (hasAuthoredFeatureValues || feature == null)
        {
            return;
        }
        hasAuthoredFeatureValues = true;
        authoredGBufferFallback = feature.GBufferFallback;
        authoredFallbackAlbedo = feature.FallbackAlbedo;
        authoredReflectionProbes = feature.ReflectionProbes;
        authoredHighQualityUpscaling = feature.HighQualityUpscaling;
        authoredOverrideAmbient = feature.OverrideAmbientLighting;
        authoredBackfaceLighting = feature.BackfaceLighting;
    }

    public void RestoreAuthoredFeatureValues()
    {
        if (!hasAuthoredFeatureValues)
        {
            return;
        }
        hasAuthoredFeatureValues = false;
        Apply(FindFeature(), authoredGBufferFallback, authoredFallbackAlbedo, authoredReflectionProbes,
            authoredHighQualityUpscaling, authoredOverrideAmbient, authoredBackfaceLighting);
    }

    public static void Apply(ScreenSpaceGlobalIlluminationURP feature, bool gBufferFallback, float fallbackAlbedo, bool reflectionProbes, bool highQualityUpscaling, bool overrideAmbient, bool backfaceLighting)
    {
        if (feature == null)
        {
            return;
        }
        feature.GBufferFallback = gBufferFallback;
        feature.FallbackAlbedo = Mathf.Clamp(fallbackAlbedo, BasisSettingsDefaults.SSGI_FALLBACK_ALBEDO_MIN, BasisSettingsDefaults.SSGI_FALLBACK_ALBEDO_MAX);
        feature.ReflectionProbes = reflectionProbes;
        feature.HighQualityUpscaling = highQualityUpscaling;
        feature.OverrideAmbientLighting = overrideAmbient;
        feature.BackfaceLighting = backfaceLighting;
    }

    /// <summary>
    /// The screen space global illumination feature on the active pipeline asset, or null when the
    /// platform's renderer does not carry one (Android ships without it).
    /// </summary>
    public static ScreenSpaceGlobalIlluminationURP FindFeature()
    {
        // The quality level's own asset first, which is what the sibling URP settings modules drive
        // (SMModuleHDRURP, SMModuleAntialiasingURP, SMModuleShadowQualityURP, SMModuleQualityAndQualitySetURP),
        // then the active and default pipeline. Basis swaps the asset per quality tier, so any one of these
        // can be the one carrying the renderer that holds the feature.
        return FindFeature(QualitySettings.renderPipeline as UniversalRenderPipelineAsset)
            ?? FindFeature(GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset)
            ?? FindFeature(GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset);
    }

    public static ScreenSpaceGlobalIlluminationURP FindFeature(UniversalRenderPipelineAsset asset)
    {
        if (asset == null)
        {
            return null;
        }
        ReadOnlySpan<ScriptableRendererData> renderers = asset.rendererDataList;
        for (int Index = 0; Index < renderers.Length; Index++)
        {
            ScriptableRendererData data = renderers[Index];
            if (data == null)
            {
                continue;
            }
            List<ScriptableRendererFeature> features = data.rendererFeatures;
            for (int Feature = 0; Feature < features.Count; Feature++)
            {
                if (features[Feature] is ScreenSpaceGlobalIlluminationURP ssgiFeature)
                {
                    return ssgiFeature;
                }
            }
        }
        return null;
    }

    public static void Apply(ScreenSpaceGlobalIlluminationVolume target, bool enabled, ScreenSpaceGlobalIlluminationVolume.QualityMode quality, bool fullResolution, float intensity)
    {
        Apply(target, enabled, quality, fullResolution, intensity, false);
    }

    public static void Apply(ScreenSpaceGlobalIlluminationVolume target, bool enabled, ScreenSpaceGlobalIlluminationVolume.QualityMode quality, bool fullResolution, float intensity, bool capture)
    {
        Apply(target, enabled, quality, fullResolution, intensity, capture, BasisSettingsDefaults.ScreenSpaceGlobalIlluminationDenoiseStrength.DefaultValue.GetDefault());
    }

    public static void Apply(ScreenSpaceGlobalIlluminationVolume target, bool enabled, ScreenSpaceGlobalIlluminationVolume.QualityMode quality, bool fullResolution, float intensity, bool capture, float denoiseStrength)
    {
        target.active = true;
        target.enable.overrideState = true;
        target.enable.value = enabled;
        target.quality.overrideState = true;
        target.quality.value = quality;
        target.sampleCount.overrideState = true;
        target.maxRaySteps.overrideState = true;
        switch (quality)
        {
            case ScreenSpaceGlobalIlluminationVolume.QualityMode.Low:
                target.sampleCount.value = 1;
                target.maxRaySteps.value = 24;
                break;
            case ScreenSpaceGlobalIlluminationVolume.QualityMode.High:
                target.sampleCount.value = 4;
                target.maxRaySteps.value = 64;
                break;
            default:
                target.sampleCount.value = 2;
                target.maxRaySteps.value = 32;
                break;
        }
        if (capture)
        {
            target.sampleCount.value = CaptureSampleCount;
            target.maxRaySteps.value = CaptureMaxRaySteps;
        }
        target.fullResolutionSS.overrideState = true;
        target.fullResolutionSS.value = fullResolution || capture;
        target.indirectDiffuseLightingMultiplier.overrideState = true;
        target.indirectDiffuseLightingMultiplier.value = Mathf.Clamp(intensity, BasisSettingsDefaults.SSGI_INTENSITY_MIN, BasisSettingsDefaults.SSGI_INTENSITY_MAX);
        // Temporal accumulation is a comfort setting: at the volume default the bounce trails behind
        // anything that moves, so the player's choice wins over the world's, like the master switch does.
        target.denoiseIntensitySS.overrideState = true;
        target.denoiseIntensitySS.value = Mathf.Clamp(denoiseStrength, BasisSettingsDefaults.SSGI_DENOISE_MIN, BasisSettingsDefaults.SSGI_DENOISE_MAX);
    }

    private void EnsureVolume()
    {
        if (volume != null)
        {
            return;
        }
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "BasisScreenSpaceGlobalIllumination";
        profile.hideFlags = HideFlags.HideAndDontSave;
        ssgi = profile.Add<ScreenSpaceGlobalIlluminationVolume>(false);
        volume = CreateVolume(0, profile);
    }

    private void EnsureCameraVolumes()
    {
        registeredCameras.RemoveWhere(camera => camera == null);
        suspendedCameras.RemoveWhere(camera => camera == null);
        foreach (Camera camera in registeredCameras)
        {
            int layer = UncoveredVolumeLayer(camera);
            if (layer < 0 || cameraVolumes.ContainsKey(layer))
            {
                continue;
            }
            cameraVolumes[layer] = CreateVolume(layer, volume.sharedProfile);
        }
    }

    private Volume CreateVolume(int layer, VolumeProfile profile)
    {
        GameObject host = new GameObject(layer == 0 ? "BasisScreenSpaceGlobalIllumination" : $"BasisScreenSpaceGlobalIllumination Layer {layer}");
        host.transform.SetParent(transform, false);
        host.layer = layer;
        Volume created = host.AddComponent<Volume>();
        created.isGlobal = true;
        created.priority = OverridePriority;
        created.weight = 1f;
        created.sharedProfile = profile;
        return created;
    }
}
