using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(BasisVideoPlayerStreaming))]
public class BasisVideoPlayerStreamingInspector : Editor
{
    private const string UxmlPath = "Packages/com.basis.videoplayer/Editor/StyleSheets/VideoPlayerStreamingSDK.uxml";
    private const string UssPath = "Packages/com.basis.videoplayer/Editor/StyleSheets/VideoPlayerSDK.uss";

    private BasisVideoPlayerStreaming _target;
    private VisualElement _root;
    private Label _resolvedUrl, _hasPlayer;
    private TextField _pcUrl, _questUrl;
    private Toggle _autoSelect;
    private Button _configureBtn;

    public override VisualElement CreateInspectorGUI()
    {
        _target = (BasisVideoPlayerStreaming)target;
        _root = new VisualElement();

        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
        if (tree == null)
        {
            _root.Add(new HelpBox("VideoPlayerStreamingSDK.uxml missing.", HelpBoxMessageType.Error));
            return _root;
        }
        tree.CloneTree(_root);
        if (sheet != null) _root.styleSheets.Add(sheet);

        BindByName("StreamUrlField", "StreamUrl");
        BindByName("AutoSelectField", "AutoSelectPerPlatform");
        BindByName("PcUrlField", "PcUrl");
        BindByName("QuestUrlField", "QuestUrl");
        BindByName("ConfigureOnStartField", "ConfigureOnStart");
        _root.Bind(serializedObject);

        _resolvedUrl = _root.Q<Label>("StatusResolvedUrl");
        _hasPlayer = _root.Q<Label>("StatusHasPlayer");
        _pcUrl = _root.Q<TextField>("PcUrlField");
        _questUrl = _root.Q<TextField>("QuestUrlField");
        _autoSelect = _root.Q<Toggle>("AutoSelectField");

        _autoSelect.RegisterValueChangedCallback(_ => RefreshAutoSelectVisibility());
        RefreshAutoSelectVisibility();

        _configureBtn = _root.Q<Button>("ConfigureButton");
        if (_configureBtn != null) _configureBtn.clicked += () =>
        {
            if (_target != null) _target.Configure();
        };

        var debugBtn = _root.Q<Button>("OpenDebugButton");
        if (debugBtn != null) debugBtn.clicked += BasisVideoPlayerDebugWindow.ShowWindow;

        _root.schedule.Execute(RefreshStatus).Every(250);
        return _root;
    }

    private void RefreshAutoSelectVisibility()
    {
        bool show = _autoSelect != null && _autoSelect.value;
        if (_pcUrl != null) _pcUrl.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (_questUrl != null) _questUrl.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void RefreshStatus()
    {
        if (_target == null) _target = (BasisVideoPlayerStreaming)target;
        if (_target == null) return;

        if (_configureBtn != null) _configureBtn.style.display = Application.isPlaying ? DisplayStyle.Flex : DisplayStyle.None;

        if (_resolvedUrl != null) _resolvedUrl.text = string.IsNullOrEmpty(_target.ResolveUrl()) ? "—" : _target.ResolveUrl();

        bool hasPlayer = _target.GetComponent<BasisVideoPlayer>() != null;
        if (_hasPlayer != null)
        {
            _hasPlayer.text = hasPlayer ? "YES" : "NO";
            _hasPlayer.RemoveFromClassList("bvp-pill-neutral");
            _hasPlayer.RemoveFromClassList("bvp-pill-good");
            _hasPlayer.RemoveFromClassList("bvp-pill-bad");
            _hasPlayer.AddToClassList(hasPlayer ? "bvp-pill-good" : "bvp-pill-bad");
        }
    }

    private void BindByName(string name, string property)
    {
        if (_root.Q<VisualElement>(name) is IBindable bindable) bindable.bindingPath = property;
    }
}
