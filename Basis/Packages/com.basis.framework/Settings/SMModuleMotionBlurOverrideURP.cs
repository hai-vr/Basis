using Basis.BasisUI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Accessibility settings module that takes motion blur away from the world and gives it to the
/// player: strength, streak limit, quality and mode, or zero strength to suppress a world that
/// forces motion blur on someone it makes ill.
///
/// <para>Unlike the bloom and fog overrides this does not walk the scene's Volumes, because almost
/// no world authors a <see cref="MotionBlur"/> override at all — there would be nothing to edit.
/// Instead the module owns one global Volume at a priority above anything a scene ships, so it
/// wins the blend whether or not the world has an opinion. It is created the first time the
/// override is switched on and disabled again when it is switched off, which hands the world's own
/// authored motion blur straight back untouched.</para>
/// </summary>
public class SMModuleMotionBlurOverrideURP : BasisSettingsBase
{
    /// <summary>Above any priority a world would plausibly ship on its own Volumes.</summary>
    private const float OverridePriority = 1000f;

    private bool _overrideEnabled = BasisSettingsDefaults.UseMotionBlurOverride.DefaultValue.GetDefault();
    private float _pendingIntensity = BasisSettingsDefaults.MotionBlurIntensity.DefaultValue.GetDefault();
    private float _pendingClamp = BasisSettingsDefaults.MotionBlurClamp.DefaultValue.GetDefault();
    private MotionBlurQuality _pendingQuality = MotionBlurQuality.Low;
    private MotionBlurMode _pendingMode = MotionBlurMode.CameraOnly;

    private Volume _volume;
    private MotionBlur _motionBlur;

    private static string K_USE_MOTION_BLUR_OVERRIDE => BasisSettingsDefaults.UseMotionBlurOverride.BindingKey;
    private static string K_MOTION_BLUR_INTENSITY => BasisSettingsDefaults.MotionBlurIntensity.BindingKey;
    private static string K_MOTION_BLUR_CLAMP => BasisSettingsDefaults.MotionBlurClamp.BindingKey;
    private static string K_MOTION_BLUR_QUALITY => BasisSettingsDefaults.MotionBlurQuality.BindingKey;
    private static string K_MOTION_BLUR_MODE => BasisSettingsDefaults.MotionBlurMode.BindingKey;

    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        if (matchedSettingName == K_USE_MOTION_BLUR_OVERRIDE)
        {
            _overrideEnabled = optionValue == "true";
            ApplyOverride();
        }
        else if (matchedSettingName == K_MOTION_BLUR_INTENSITY)
        {
            if (SliderReadOption(optionValue, out float intensity))
            {
                _pendingIntensity = intensity;
                ApplyOverride();
            }
        }
        else if (matchedSettingName == K_MOTION_BLUR_CLAMP)
        {
            if (SliderReadOption(optionValue, out float clamp))
            {
                _pendingClamp = clamp;
                ApplyOverride();
            }
        }
        else if (matchedSettingName == K_MOTION_BLUR_QUALITY)
        {
            _pendingQuality = ReadQuality(optionValue);
            ApplyOverride();
        }
        else if (matchedSettingName == K_MOTION_BLUR_MODE)
        {
            _pendingMode = ReadMode(optionValue);
            ApplyOverride();
        }
    }

    public override void ChangedSettings()
    {
    }

    /// <summary>Dropdown values arrive lowercased through <c>TOLowerValidSettingsChange</c>.</summary>
    private static MotionBlurQuality ReadQuality(string optionValue)
    {
        switch (optionValue)
        {
            case "medium": return MotionBlurQuality.Medium;
            case "high": return MotionBlurQuality.High;
            default: return MotionBlurQuality.Low;
        }
    }

    private static MotionBlurMode ReadMode(string optionValue)
    {
        return optionValue == "camera and objects" ? MotionBlurMode.CameraAndObjects : MotionBlurMode.CameraOnly;
    }

    private void ApplyOverride()
    {
        if (!_overrideEnabled)
        {
            if (_volume != null)
            {
                _volume.gameObject.SetActive(false);
            }
            return;
        }

        EnsureVolume();
        if (_motionBlur == null)
        {
            return;
        }

        // active stays on even at zero strength. A component that is off contributes nothing to the
        // blend, which would let the world's own motion blur back in — and zero strength is exactly
        // how someone turns a world's motion blur off. URP skips the pass on its own here, because
        // MotionBlur.IsActive() is intensity > 0.
        _motionBlur.active = true;
        _motionBlur.intensity.value = Mathf.Clamp(_pendingIntensity,
            BasisSettingsDefaults.MOTION_BLUR_INTENSITY_MIN, BasisSettingsDefaults.MOTION_BLUR_INTENSITY_MAX);
        _motionBlur.clamp.value = Mathf.Clamp(_pendingClamp,
            BasisSettingsDefaults.MOTION_BLUR_CLAMP_MIN, BasisSettingsDefaults.MOTION_BLUR_CLAMP_MAX);
        _motionBlur.quality.value = _pendingQuality;
        _motionBlur.mode.value = _pendingMode;

        _volume.gameObject.SetActive(true);
    }

    private void EnsureVolume()
    {
        if (_volume != null)
        {
            return;
        }

        GameObject host = new GameObject("BasisMotionBlurOverride");
        host.transform.SetParent(transform, false);
        // The local player camera's volume layer mask is Default only, so the trigger has to sit on
        // Default to be seen at all. It also keeps this off the handheld camera, whose mask is layer
        // 11 - that camera drives its own MotionBlur from the photo panel.
        host.layer = 0;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "BasisMotionBlurOverride";
        // Runtime-only profile — it is never written to disk and lives as long as the module does.
        profile.hideFlags = HideFlags.HideAndDontSave;
        _motionBlur = profile.Add<MotionBlur>(true);

        _volume = host.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = OverridePriority;
        _volume.weight = 1f;
        _volume.sharedProfile = profile;
    }
}
