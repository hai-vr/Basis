using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(BasisVideoMaterialOutput))]
public class BasisVideoMaterialOutputInspector : Editor
{
    private const string UxmlPath = "Packages/com.basis.videoplayer/Editor/StyleSheets/VideoMaterialOutputSDK.uxml";
    private const string UssPath = "Packages/com.basis.videoplayer/Editor/StyleSheets/VideoPlayerSDK.uss";

    private BasisVideoMaterialOutput _target;
    private VisualElement _root;
    private Label _playerResolved, _rendererResolved, _material, _property, _currentTex, _help;

    public override VisualElement CreateInspectorGUI()
    {
        _target = (BasisVideoMaterialOutput)target;
        _root = new VisualElement();

        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
        if (tree == null)
        {
            _root.Add(new HelpBox("VideoMaterialOutputSDK.uxml missing.", HelpBoxMessageType.Error));
            return _root;
        }
        tree.CloneTree(_root);
        if (sheet != null) _root.styleSheets.Add(sheet);

        var playerField = _root.Q<ObjectField>("PlayerField");
        if (playerField != null) playerField.objectType = typeof(BasisVideoPlayer);

        BindByName("PlayerField", "Player");
        BindByName("TargetRendererField", "TargetRenderer");
        BindByName("MaterialIndexField", "MaterialIndex");
        BindByName("TexturePropertyField", "TexturePropertyName");
        BindByName("UseSharedMaterialField", "UseSharedMaterial");
        BindByName("PlaceholderField", "PlaceholderTexture");
        BindByName("RestoreOnEndedField", "RestorePlaceholderOnEnded");
        BindByName("FlipVerticallyField", "FlipVertically");
        _root.Bind(serializedObject);

        _playerResolved = _root.Q<Label>("StatusPlayerResolved");
        _rendererResolved = _root.Q<Label>("StatusRendererResolved");
        _material = _root.Q<Label>("StatusMaterial");
        _property = _root.Q<Label>("StatusProperty");
        _currentTex = _root.Q<Label>("StatusCurrentTex");
        _help = _root.Q<Label>("StatusHelp");

        _root.schedule.Execute(RefreshStatus).Every(250);
        return _root;
    }

    private void RefreshStatus()
    {
        if (_target == null) _target = (BasisVideoMaterialOutput)target;
        if (_target == null) return;

        var player = _target.Player != null ? _target.Player : _target.GetComponentInParent<BasisVideoPlayer>();
        SetPill(_playerResolved, player != null);
        SetPill(_rendererResolved, _target.TargetRenderer != null);

        Material mat = null;
        if (_target.TargetRenderer != null)
        {
            var shared = _target.TargetRenderer.sharedMaterials;
            if (_target.MaterialIndex >= 0 && _target.MaterialIndex < shared.Length) mat = shared[_target.MaterialIndex];
        }
        SetText(_material, mat != null ? mat.name : "(none)");

        bool hasProp = false;
        if (mat != null && !string.IsNullOrEmpty(_target.TexturePropertyName))
            hasProp = mat.HasProperty(Shader.PropertyToID(_target.TexturePropertyName));
        SetPill(_property, hasProp);

        if (Application.isPlaying && player != null)
        {
            var tex = player.OutputTexture;
            SetText(_currentTex, tex != null ? $"{tex.name} ({tex.width}x{tex.height})" : "(no frame yet)");
        }
        else
        {
            SetText(_currentTex, Application.isPlaying ? "(no player)" : "Enter Play Mode for live texture.");
        }

        if (_target.TargetRenderer == null)
            ShowHelp("No Target Renderer assigned — video texture has nothing to bind to.", "bvp-help-warn");
        else if (mat != null && !hasProp && !string.IsNullOrEmpty(_target.TexturePropertyName))
            ShowHelp($"Material '{mat.name}' has no property '{_target.TexturePropertyName}'. URP shaders typically use _BaseMap; legacy BiRP uses _MainTex.", "bvp-help-warn");
        else
            HideHelp();
    }

    private void ShowHelp(string text, string cssClass)
    {
        if (_help == null) return;
        _help.text = text;
        _help.RemoveFromClassList("bvp-help-info");
        _help.RemoveFromClassList("bvp-help-warn");
        _help.RemoveFromClassList("bvp-help-error");
        _help.AddToClassList(cssClass);
        _help.style.display = DisplayStyle.Flex;
    }

    private void HideHelp()
    {
        if (_help != null) _help.style.display = DisplayStyle.None;
    }

    private static void SetText(Label l, string v) { if (l != null) l.text = v; }

    private static void SetPill(Label l, bool value)
    {
        if (l == null) return;
        l.RemoveFromClassList("bvp-pill-neutral");
        l.RemoveFromClassList("bvp-pill-good");
        l.RemoveFromClassList("bvp-pill-bad");
        l.text = value ? "YES" : "NO";
        l.AddToClassList(value ? "bvp-pill-good" : "bvp-pill-bad");
    }

    private void BindByName(string name, string property)
    {
        if (_root.Q<VisualElement>(name) is IBindable bindable) bindable.bindingPath = property;
    }
}
