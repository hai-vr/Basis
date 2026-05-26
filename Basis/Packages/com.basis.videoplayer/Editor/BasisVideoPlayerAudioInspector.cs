using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(BasisVideoPlayerAudio))]
public class BasisVideoPlayerAudioInspector : Editor
{
    private const string UxmlPath = "Packages/com.basis.videoplayer/Editor/StyleSheets/VideoPlayerAudioSDK.uxml";
    private const string UssPath = "Packages/com.basis.videoplayer/Editor/StyleSheets/VideoPlayerSDK.uss";

    private BasisVideoPlayerAudio _target;
    private VisualElement _root;
    private Label _hasSource, _playing, _anchor, _mediaTime, _consumed, _dropped, _pcmLevels, _latency, _depthValue, _editHint;
    private VisualElement _depthFill;

    public override VisualElement CreateInspectorGUI()
    {
        _target = (BasisVideoPlayerAudio)target;
        _root = new VisualElement();

        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
        if (tree == null)
        {
            _root.Add(new HelpBox("VideoPlayerAudioSDK.uxml missing.", HelpBoxMessageType.Error));
            return _root;
        }
        tree.CloneTree(_root);
        if (sheet != null) _root.styleSheets.Add(sheet);

        BindByName("TargetAudioSourceField", "TargetAudioSource");
        BindByName("AssignClipOnAwakeField", "AssignClipOnAwake");
        BindByName("ClipNameField", "ClipName");
        BindByName("SampleRateField", "SampleRate");
        BindByName("ChannelCountField", "ChannelCount");
        BindByName("ClipLengthField", "ClipLengthSeconds");
        BindByName("MaxQueuedField", "MaxQueuedFrames");
        BindByName("DropOldestField", "DropOldestOnOverflow");
        BindByName("RebufferField", "RebufferFrames");
        BindByName("AutoPlayField", "AutoPlayOnEnable");
        BindByName("StopOnDisableField", "StopOnDisable");
        BindByName("ClearQueueOnEnableField", "ClearQueueOnEnable");
        BindByName("VolumeGainField", "VolumeGain");
        BindByName("MuteField", "Mute");
        _root.Bind(serializedObject);

        _hasSource = _root.Q<Label>("StatusHasSource");
        _playing = _root.Q<Label>("StatusPlaying");
        _anchor = _root.Q<Label>("StatusAnchor");
        _mediaTime = _root.Q<Label>("StatusMediaTime");
        _consumed = _root.Q<Label>("StatusConsumed");
        _dropped = _root.Q<Label>("StatusDropped");
        _pcmLevels = _root.Q<Label>("StatusPcmLevels");
        _latency = _root.Q<Label>("StatusLatency");
        _depthValue = _root.Q<Label>("StatusDepthValue");
        _depthFill = _root.Q<VisualElement>("StatusDepthFill");
        _editHint = _root.Q<Label>("StatusEditModeHint");

        var clearBtn = _root.Q<Button>("ClearQueueButton");
        if (clearBtn != null) clearBtn.clicked += () => { if (_target != null) _target.ClearQueue(); };

        var resetBtn = _root.Q<Button>("ResetAnchorButton");
        if (resetBtn != null) resetBtn.clicked += () => { if (_target != null) _target.ResetSyncAnchor(); };

        _root.schedule.Execute(RefreshStatus).Every(250);
        return _root;
    }

    private void RefreshStatus()
    {
        if (_target == null) _target = (BasisVideoPlayerAudio)target;
        if (_target == null) return;

        bool live = Application.isPlaying;
        if (_editHint != null) _editHint.style.display = live ? DisplayStyle.None : DisplayStyle.Flex;

        var src = _target.ActiveAudioSource;
        SetPill(_hasSource, src != null, live);
        SetPill(_playing, src != null && src.isPlaying, live);
        SetPill(_anchor, _target.HasMediaTime, live);
        SetText(_mediaTime, FormatUs(_target.CurrentMediaTimeUs));
        SetText(_consumed, _target.ConsumedSampleCount.ToString());
        SetText(_dropped, _target.DroppedFrameCount.ToString());
        SetText(_pcmLevels, $"{_target.LastPcmPeak:F3} / {_target.LastPcmRms:F3}");
        SetText(_latency, FormatUs(_target.AudioOutputLatencyUs));

        int depth = _target.QueuedFrameCount;
        int max = _target.MaxQueuedFrames > 0 ? _target.MaxQueuedFrames : Mathf.Max(1, depth + 1);
        float fill = _target.MaxQueuedFrames > 0 ? (float)depth / max : Mathf.Min(1f, depth / 64f);
        SetText(_depthValue, _target.MaxQueuedFrames > 0 ? $"{depth} / {max}" : $"{depth} / unbounded");
        if (_depthFill != null) _depthFill.style.width = new Length(Mathf.Clamp01(fill) * 100f, LengthUnit.Percent);
    }

    private static void SetText(Label l, string v) { if (l != null) l.text = v; }

    private static void SetPill(Label l, bool value, bool live)
    {
        if (l == null) return;
        l.RemoveFromClassList("bvp-pill-neutral");
        l.RemoveFromClassList("bvp-pill-good");
        l.RemoveFromClassList("bvp-pill-bad");
        if (!live) { l.text = "—"; l.AddToClassList("bvp-pill-neutral"); return; }
        l.text = value ? "YES" : "NO";
        l.AddToClassList(value ? "bvp-pill-good" : "bvp-pill-bad");
    }

    private static string FormatUs(long us)
    {
        if (us <= 0) return "0 us";
        long ms = us / 1000;
        if (ms < 1000) return $"{us} us ({ms} ms)";
        double s = us / 1_000_000.0;
        return $"{us} us ({s:F3} s)";
    }

    private void BindByName(string name, string property)
    {
        if (_root.Q<VisualElement>(name) is IBindable bindable) bindable.bindingPath = property;
    }
}
