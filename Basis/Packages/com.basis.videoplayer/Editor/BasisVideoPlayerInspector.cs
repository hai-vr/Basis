using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(BasisVideoPlayer))]
public class BasisVideoPlayerInspector : Editor
{
    private const string UxmlPath = "Packages/com.basis.videoplayer/Editor/StyleSheets/VideoPlayerSDK.uxml";
    private const string UssPath = "Packages/com.basis.videoplayer/Editor/StyleSheets/VideoPlayerSDK.uss";

    private BasisVideoPlayer _player;
    private VisualElement _root;
    private Label _statusPlaying;
    private Label _statusPaused;
    private Label _statusPrepared;
    private Label _statusVideoSize;
    private Label _statusQueued;
    private Label _statusPresented;
    private Label _statusDropped;
    private Label _statusMediaTime;
    private Label _editModeHint;

    public override VisualElement CreateInspectorGUI()
    {
        _player = (BasisVideoPlayer)target;
        _root = new VisualElement();

        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
        if (tree == null)
        {
            _root.Add(new HelpBox("VideoPlayerSDK.uxml missing.", HelpBoxMessageType.Error));
            return _root;
        }

        tree.CloneTree(_root);
        if (sheet != null) _root.styleSheets.Add(sheet);

        BindFields();
        BindStatus();
        BindActions();

        _root.schedule.Execute(RefreshStatus).Every(250);
        return _root;
    }

    private void BindFields()
    {
        serializedObject.Update();

        BindByName("DisplayNameField", "DisplayName");
        BindByName("MaxQueueLengthField", "MaxQueueLength");
        BindByName("OverflowPolicyField", "OverflowPolicy");
        BindByName("LateFrameSkipField", "LateFrameSkipUs");
        BindByName("BufferModeField", "BufferMode");
        BindByName("BufferMillisecondsField", "BufferMilliseconds");

        BindByName("AutoPlayField", "AutoPlayOnSourceAssigned");
        BindByName("StopOnDisableField", "StopOnDisable");
        BindByName("LoopField", "Loop");
        BindByName("LoopRestartDelayField", "LoopRestartDelaySeconds");
        BindByName("PresentationOffsetField", "PresentationOffsetUs");

        BindByName("AudioRoutingField", "AudioRouting");
        BindByName("VolumeField", "Volume");
        BindByName("MuteField", "Mute");
        BindByName("PlaybackRateField", "PlaybackRate");

        BindByName("VerboseLoggingField", "VerboseLogging");

        _root.Bind(serializedObject);
    }

    private void BindByName(string name, string property)
    {
        if (_root.Q<VisualElement>(name) is IBindable bindable) bindable.bindingPath = property;
    }

    private void BindStatus()
    {
        _statusPlaying = _root.Q<Label>("StatusPlaying");
        _statusPaused = _root.Q<Label>("StatusPaused");
        _statusPrepared = _root.Q<Label>("StatusPrepared");
        _statusVideoSize = _root.Q<Label>("StatusVideoSize");
        _statusQueued = _root.Q<Label>("StatusQueued");
        _statusPresented = _root.Q<Label>("StatusPresented");
        _statusDropped = _root.Q<Label>("StatusDropped");
        _statusMediaTime = _root.Q<Label>("StatusMediaTime");
        _editModeHint = _root.Q<Label>("StatusEditModeHint");
    }

    private void BindActions()
    {
        var debugBtn = _root.Q<Button>("OpenDebugWindowButton");
        if (debugBtn != null) debugBtn.clicked += BasisVideoPlayerDebugWindow.ShowWindow;

        var docBtn = _root.Q<Button>("DocumentationButton");
        if (docBtn != null) docBtn.clicked += () =>
        {
            Application.OpenURL("https://github.com/dooly123/Basis");
        };
    }

    private void RefreshStatus()
    {
        if (_player == null) _player = (BasisVideoPlayer)target;
        if (_player == null) return;

        bool live = Application.isPlaying;
        if (_editModeHint != null) _editModeHint.style.display = live ? DisplayStyle.None : DisplayStyle.Flex;

        SetPill(_statusPlaying, _player.IsPlaying, live);
        SetPill(_statusPaused, _player.IsPaused, live);
        SetPill(_statusPrepared, _player.IsPrepared, live);

        var sz = _player.VideoSize;
        SetValue(_statusVideoSize, sz.x > 0 ? $"{sz.x} x {sz.y}" : "—");
        SetValue(_statusQueued, $"{_player.QueuedFrameCount} / {_player.MaxQueueLength}");
        SetValue(_statusPresented, _player.PresentedFrameCount.ToString());
        SetValue(_statusDropped, _player.DroppedFrameCount.ToString());
        SetValue(_statusMediaTime, FormatUs(_player.Clock != null ? _player.Clock.CurrentMediaTimeUs : 0));
    }

    private static void SetPill(Label label, bool value, bool live)
    {
        if (label == null) return;
        label.RemoveFromClassList("bvp-pill-neutral");
        label.RemoveFromClassList("bvp-pill-good");
        label.RemoveFromClassList("bvp-pill-bad");
        if (!live)
        {
            label.text = "—";
            label.AddToClassList("bvp-pill-neutral");
            return;
        }
        label.text = value ? "YES" : "NO";
        label.AddToClassList(value ? "bvp-pill-good" : "bvp-pill-bad");
    }

    private static void SetValue(Label label, string text)
    {
        if (label != null) label.text = text;
    }

    private static string FormatUs(long us)
    {
        if (us <= 0) return "0 us";
        long ms = us / 1000;
        if (ms < 1000) return $"{us} us ({ms} ms)";
        double s = us / 1_000_000.0;
        return $"{us} us ({s:F3} s)";
    }
}
