using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

[CustomEditor(typeof(BasisMediaAudioChannel))]
public class BasisMediaAudioChannelInspector : Editor
{
    private const string UxmlPath = "Packages/com.basis.mediaplayer/Editor/StyleSheets/MediaPlayerChannelSDK.uxml";
    private const string UssPath = "Packages/com.basis.mediaplayer/Editor/StyleSheets/MediaPlayerSDK.uss";

    private VisualElement _root;

    public override VisualElement CreateInspectorGUI()
    {
        _root = new VisualElement();

        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
        if (tree == null)
        {
            _root.Add(new HelpBox("MediaPlayerChannelSDK.uxml missing.", HelpBoxMessageType.Error));
            return _root;
        }
        tree.CloneTree(_root);
        if (sheet != null) _root.styleSheets.Add(sheet);

        BindByName("ChannelField", "Channel");
        BindByName("AnalysisFeedField", "AnalysisFeed");
        BindByName("AnalysisFeedLatencyField", "AnalysisFeedLatency");
        _root.Bind(serializedObject);

        // The buffer length and the note only mean anything once the feed is on.
        var toggle = _root.Q<PropertyField>("AnalysisFeedField");
        toggle?.RegisterValueChangeCallback(evt => ShowAnalysisFeedDetail(evt.changedProperty.boolValue));
        ShowAnalysisFeedDetail(serializedObject.FindProperty("AnalysisFeed").boolValue);

        return _root;
    }

    private void ShowAnalysisFeedDetail(bool on)
    {
        DisplayStyle style = on ? DisplayStyle.Flex : DisplayStyle.None;
        if (_root.Q<VisualElement>("AnalysisFeedLatencyField") is VisualElement latency) latency.style.display = style;
        if (_root.Q<VisualElement>("AnalysisFeedHelp") is VisualElement help) help.style.display = style;
    }

    private void BindByName(string name, string property)
    {
        if (_root.Q<VisualElement>(name) is IBindable bindable) bindable.bindingPath = property;
    }
}
