using System.Collections.Generic;
using Basis.BasisUI;
using UnityEngine;
using UnityEngine.Rendering;

public class SMModuleRealtimeReflectionProbes : BasisSettingsBase
{
    private static string K_USE => BasisSettingsDefaults.UseRealtimeReflectionProbes.BindingKey;
    private static string K_RATE => BasisSettingsDefaults.RealtimeReflectionProbeRate.BindingKey;

    private bool _enabled;
    private float _intervalSeconds = 1f / 30f;
    private bool _matchRender;
    private float _accumulator;

    private readonly List<ReflectionProbe> _managed = new List<ReflectionProbe>(4);
    private readonly List<ReflectionProbeRefreshMode> _originalRefresh = new List<ReflectionProbeRefreshMode>(4);

    public override void Awake()
    {
        base.Awake();
        _enabled = BasisSettingsDefaults.UseRealtimeReflectionProbes.RawValue;
        ApplyRate(BasisSettingsDefaults.RealtimeReflectionProbeRate.RawValue);
        if (_enabled)
        {
            CaptureAndOverride();
        }
    }

    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        if (matchedSettingName == K_USE)
        {
            bool next = optionValue == "true";
            if (next == _enabled) return;
            _enabled = next;
            if (_enabled)
            {
                CaptureAndOverride();
            }
            else
            {
                Restore();
            }
            _accumulator = 0f;
        }
        else if (matchedSettingName == K_RATE)
        {
            ApplyRate(optionValue);
            if (_enabled)
            {
                ApplyRefreshModeToManaged();
            }
        }
    }

    public override void ChangedSettings()
    {
    }

    private void Update()
    {
        if (!_enabled || _matchRender) return;

        _accumulator += Time.unscaledDeltaTime;
        if (_accumulator < _intervalSeconds) return;
        _accumulator = 0f;

        for (int i = _managed.Count - 1; i >= 0; i--)
        {
            ReflectionProbe probe = _managed[i];
            if (probe == null)
            {
                _managed.RemoveAt(i);
                _originalRefresh.RemoveAt(i);
                continue;
            }
            if (probe.mode == ReflectionProbeMode.Realtime &&
                probe.refreshMode == ReflectionProbeRefreshMode.ViaScripting)
            {
                probe.RenderProbe();
            }
        }
    }

    private void ApplyRate(string value)
    {
        switch (value)
        {
            case "match render":
                _matchRender = true;
                _intervalSeconds = 0f;
                break;
            case "30hz":
                _matchRender = false;
                _intervalSeconds = 1f / 30f;
                break;
            case "15hz":
                _matchRender = false;
                _intervalSeconds = 1f / 15f;
                break;
            case "10hz":
                _matchRender = false;
                _intervalSeconds = 1f / 10f;
                break;
            case "5hz":
                _matchRender = false;
                _intervalSeconds = 1f / 5f;
                break;
            case "1hz":
                _matchRender = false;
                _intervalSeconds = 1f;
                break;
            default:
                _matchRender = false;
                _intervalSeconds = 1f / 30f;
                break;
        }
    }

    private void CaptureAndOverride()
    {
        _managed.Clear();
        _originalRefresh.Clear();

        ReflectionProbe[] all = FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            ReflectionProbe probe = all[i];
            if (probe.mode != ReflectionProbeMode.Realtime) continue;
            _managed.Add(probe);
            _originalRefresh.Add(probe.refreshMode);
        }
        ApplyRefreshModeToManaged();
    }

    private void ApplyRefreshModeToManaged()
    {
        ReflectionProbeRefreshMode target = _matchRender
            ? ReflectionProbeRefreshMode.EveryFrame
            : ReflectionProbeRefreshMode.ViaScripting;

        for (int i = 0; i < _managed.Count; i++)
        {
            if (_managed[i] == null) continue;
            _managed[i].refreshMode = target;
        }
    }

    private void Restore()
    {
        for (int i = 0; i < _managed.Count; i++)
        {
            if (_managed[i] == null) continue;
            _managed[i].refreshMode = _originalRefresh[i];
        }
        _managed.Clear();
        _originalRefresh.Clear();
    }
}
