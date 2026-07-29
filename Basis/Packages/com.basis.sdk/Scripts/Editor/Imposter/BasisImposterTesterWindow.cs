using System.Collections.Generic;
using Basis.Scripts.BasisSdk;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dev tool: runs the real imposter build on a scene avatar and shows the result — an
/// interactive 3D view of the decoded imposter, the baked atlas, payload stats and the
/// per-stage generation log. The preview is constructed from the serialized → reparsed
/// payload using the same builders the client runtime uses, so what you see is what a
/// remote player would get.
///
/// A scene copy is also spawned next to the source avatar; "Mirror source pose" drives it
/// with the same `rest * delta` composition the networked bone job applies, so posing or
/// animating the source live-validates skinning and the delta math.
/// </summary>
public class BasisImposterTesterWindow : EditorWindow
{
    [MenuItem("Basis/Avatar/Imposter Tester")]
    public static void Open()
    {
        BasisImposterTesterWindow window = GetWindow<BasisImposterTesterWindow>("Imposter Tester");
        window.minSize = new Vector2(390f, 580f);
    }

    private BasisAvatar _avatar;
    private BasisImposterPayload _payload;
    private BasisImposterGenerator.GenerationReport _report;
    private int _payloadBytes;
    private int _base64Bytes;
    private string _lastError;
    private bool _showStages = true;
    private bool _showBones;
    private int _tab;
    private static readonly string[] TabNames = { "3D View", "Atlas", "Info" };

    private GameObject _previewRoot;
    private Transform[] _previewBones;
    private Transform _previewHips;
    private Mesh _previewMesh;
    private Texture2D _previewTexture;
    private Material _previewMaterial;

    private Animator _sourceAnimator;
    private Transform[] _sourceBones;
    private Dictionary<HumanBodyBones, Quaternion> _sourceTposeLocals;
    private bool _mirrorPose = true;
    private float _previewOffset;

    private PreviewRenderUtility _previewRender;
    private float _orbitYaw = 135f;
    private float _orbitPitch = 12f;
    private float _orbitZoom = 1f;
    private Vector2 _scroll;

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        _previewRender?.Cleanup();
        _previewRender = null;
        DestroyPreview();
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawSourceSection();
        DrawStatusSection();
        if (_payload != null)
        {
            EditorGUILayout.Space(6);
            _tab = GUILayout.Toolbar(_tab, TabNames);
            EditorGUILayout.Space(2);
            switch (_tab)
            {
                case 0: DrawViewportTab(); break;
                case 1: DrawAtlasTab(); break;
                default: DrawInfoTab(); break;
            }
            DrawScenePreviewSection();
        }

        EditorGUILayout.EndScrollView();
    }

    // ─────────────────────────── source + generate ───────────────────────────

    private void DrawSourceSection()
    {
        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        _avatar = (BasisAvatar)EditorGUILayout.ObjectField("Avatar (scene)", _avatar, typeof(BasisAvatar), true);
        if (_avatar == null && Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent(out BasisAvatar selected))
        {
            if (GUILayout.Button($"Use selected: {selected.name}"))
            {
                _avatar = selected;
            }
        }

        BasisImposterGenerator.TargetTriangleCount = EditorGUILayout.IntSlider("Target Triangles", BasisImposterGenerator.TargetTriangleCount, 200, 8000);
        BasisImposterGenerator.AtlasSize = EditorGUILayout.IntPopup("Atlas Size", BasisImposterGenerator.AtlasSize,
            new[] { "128", "256", "512" }, new[] { 128, 256, 512 });

        bool persistent = _avatar != null && EditorUtility.IsPersistent(_avatar);
        if (persistent)
        {
            EditorGUILayout.HelpBox("Drop a scene instance of the avatar (drag the prefab into a scene first) — generation renders it with its real materials.", MessageType.Info);
        }

        using (new EditorGUI.DisabledScope(_avatar == null || persistent))
        {
            if (GUILayout.Button("Generate", GUILayout.Height(30)))
            {
                Generate();
                GUIUtility.ExitGUI();
            }
        }
    }

    private void DrawStatusSection()
    {
        if (!string.IsNullOrEmpty(_lastError))
        {
            EditorGUILayout.HelpBox(_lastError, MessageType.Error);
        }

        if (_report == null || _report.Entries.Count == 0)
        {
            return;
        }
        _showStages = EditorGUILayout.Foldout(_showStages, $"Generation Log — {_report.TotalSeconds:0.00}s total", true);
        if (!_showStages)
        {
            return;
        }
        using (new EditorGUI.IndentLevelScope())
        {
            for (int i = 0; i < _report.Entries.Count; i++)
            {
                var entry = _report.Entries[i];
                EditorGUILayout.LabelField($"{entry.Label} — {entry.Seconds:0.00}s", EditorStyles.miniBoldLabel);
                if (!string.IsNullOrEmpty(entry.Detail))
                {
                    EditorGUILayout.LabelField(entry.Detail, EditorStyles.miniLabel);
                }
            }
        }
    }

    private void Generate()
    {
        _lastError = null;
        _payload = null;
        _report = new BasisImposterGenerator.GenerationReport();
        BasisImposterPayload generated = null;
        BasisImposterGenerator.VerboseLogging = true;
        BasisImposterGenerator.ActiveReport = _report;
        try
        {
            generated = BasisImposterGenerator.Generate(_avatar);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            _lastError = $"Generation threw {e.GetType().Name}: {e.Message}\nFull stack is in the Console.";
            return;
        }
        finally
        {
            BasisImposterGenerator.ActiveReport = null;
            BasisImposterGenerator.VerboseLogging = false;
            EditorUtility.ClearProgressBar();
        }
        if (generated == null)
        {
            _lastError = "Generation returned nothing — the Console has the warning that stopped it.";
            return;
        }

        try
        {
            // Round-trip through the wire format so the preview is what a client would decode.
            byte[] bytes = generated.Serialize();
            _payloadBytes = bytes.Length;
            _base64Bytes = System.Convert.ToBase64String(bytes).Length;
            _payload = BasisImposterPayload.TryParse(bytes);
            if (_payload == null)
            {
                _lastError = "Serialize → parse round-trip failed — codec bug, see Console.";
                return;
            }

            BuildPreview();
            _tab = 0;
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            _lastError = $"Preview build threw {e.GetType().Name}: {e.Message}\nFull stack is in the Console.";
            DestroyPreview();
        }
    }

    // ─────────────────────────── result tabs ───────────────────────────

    private void DrawViewportTab()
    {
        if (_previewMesh == null || _previewMaterial == null)
        {
            EditorGUILayout.HelpBox("No preview mesh built.", MessageType.Warning);
            return;
        }

        float side = Mathf.Clamp(position.width - 24f, 200f, 460f);
        Rect rect = GUILayoutUtility.GetRect(side, side, GUILayout.ExpandWidth(false));
        HandleOrbitInput(rect);

        if (Event.current.type == EventType.Repaint)
        {
            _previewRender ??= CreatePreviewRender();

            Bounds bounds = new Bounds(
                (_payload.PositionBoundsMin + _payload.PositionBoundsMax) * 0.5f,
                _payload.PositionBoundsMax - _payload.PositionBoundsMin);
            float distance = bounds.extents.magnitude * 2.1f / Mathf.Max(_orbitZoom, 0.05f) + 0.05f;
            Quaternion orbit = Quaternion.Euler(_orbitPitch, _orbitYaw, 0f);

            _previewRender.BeginPreview(rect, GUIStyle.none);
            Camera camera = _previewRender.camera;
            camera.transform.SetPositionAndRotation(bounds.center + orbit * (Vector3.back * distance), orbit);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = distance * 6f + 10f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.17f, 0.19f, 1f);

            _previewRender.lights[0].intensity = 1.2f;
            _previewRender.lights[0].transform.rotation = Quaternion.Euler(40f, _orbitYaw - 30f, 0f);
            _previewRender.lights[1].intensity = 0.4f;
            _previewRender.ambientColor = new Color(0.32f, 0.32f, 0.34f, 1f);

            _previewRender.DrawMesh(_previewMesh, Matrix4x4.identity, _previewMaterial, 0);
            camera.Render();
            Texture result = _previewRender.EndPreview();
            GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);
        }

        EditorGUILayout.LabelField("Drag to orbit, scroll to zoom. Rest pose — use the scene copy below for posed/mirrored viewing.", EditorStyles.miniLabel);
    }

    private PreviewRenderUtility CreatePreviewRender()
    {
        PreviewRenderUtility preview = new PreviewRenderUtility();
        preview.camera.fieldOfView = 30f;
        return preview;
    }

    private void HandleOrbitInput(Rect rect)
    {
        Event current = Event.current;
        if (!rect.Contains(current.mousePosition))
        {
            return;
        }
        if (current.type == EventType.MouseDrag && (current.button == 0 || current.button == 1))
        {
            _orbitYaw += current.delta.x * 0.6f;
            _orbitPitch = Mathf.Clamp(_orbitPitch + current.delta.y * 0.6f, -85f, 85f);
            current.Use();
            Repaint();
        }
        else if (current.type == EventType.ScrollWheel)
        {
            _orbitZoom = Mathf.Clamp(_orbitZoom * (1f - current.delta.y * 0.04f), 0.2f, 6f);
            current.Use();
            Repaint();
        }
    }

    private void DrawAtlasTab()
    {
        if (_previewTexture == null)
        {
            EditorGUILayout.HelpBox("No atlas texture decoded.", MessageType.Warning);
            return;
        }
        float side = Mathf.Clamp(position.width - 24f, 200f, 460f);
        Rect rect = GUILayoutUtility.GetRect(side, side, GUILayout.ExpandWidth(false));
        EditorGUI.DrawPreviewTexture(rect, _previewTexture);
        EditorGUILayout.LabelField($"Decoded as {_previewTexture.format}, {_previewTexture.width}x{_previewTexture.height}, {_previewTexture.mipmapCount} mips", EditorStyles.miniLabel);
        for (int i = 0; i < _payload.Textures.Length; i++)
        {
            var texture = _payload.Textures[i];
            EditorGUILayout.LabelField($"Payload [{texture.Format}] {texture.Width}x{texture.Height}, {texture.MipCount} mips — {texture.Data.Length / 1024f:0.0} KB", EditorStyles.miniLabel);
        }
    }

    private void DrawInfoTab()
    {
        EditorGUILayout.LabelField("Payload", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Triangles", _payload.TriangleCount.ToString());
        EditorGUILayout.LabelField("Vertices", _payload.VertexCount.ToString());
        EditorGUILayout.LabelField("Bones", _payload.BoneCount.ToString());
        EditorGUILayout.LabelField("Raw size", $"{_payloadBytes / 1024f:0.0} KB");
        EditorGUILayout.LabelField("In connector (base64)", $"{_base64Bytes / 1024f:0.0} KB");
        EditorGUILayout.LabelField("Authored scale", _payload.AuthoredRootScale.ToString("0.###"));
        EditorGUILayout.LabelField("Eye height / fwd", _payload.AvatarEyePosition.ToString("0.###"));
        EditorGUILayout.LabelField("Mouth height / fwd", _payload.AvatarMouthPosition.ToString("0.###"));
        Vector3 size = _payload.PositionBoundsMax - _payload.PositionBoundsMin;
        EditorGUILayout.LabelField("Bounds (root space)", size.ToString("0.###"));

        _showBones = EditorGUILayout.Foldout(_showBones, "Skeleton", true);
        if (_showBones)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < _payload.BoneCount; i++)
                {
                    HumanBodyBones bone = (HumanBodyBones)_payload.BoneHumanBodyBone[i];
                    byte parent = _payload.BoneParentIndex[i];
                    string parentName = parent == 0xFF ? "(root)" : ((HumanBodyBones)_payload.BoneHumanBodyBone[parent]).ToString();
                    EditorGUILayout.LabelField($"{bone} ← {parentName}", EditorStyles.miniLabel);
                }
            }
        }
    }

    // ─────────────────────────── scene copy ───────────────────────────

    private void DrawScenePreviewSection()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Scene Copy", EditorStyles.boldLabel);
        if (_previewRoot == null)
        {
            EditorGUILayout.LabelField("Destroyed. Regenerate to respawn it.", EditorStyles.miniLabel);
            return;
        }
        _mirrorPose = EditorGUILayout.Toggle("Mirror Source Pose", _mirrorPose);
        _previewOffset = EditorGUILayout.FloatField("Offset (0 = auto)", _previewOffset);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Select In Scene"))
        {
            EditorGUIUtility.PingObject(_previewRoot);
            Selection.activeGameObject = _previewRoot;
        }
        if (GUILayout.Button("Destroy Copy"))
        {
            DestroyScenePreviewOnly();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void BuildPreview()
    {
        DestroyPreview();

        _sourceAnimator = _avatar.Animator;
        _sourceTposeLocals = BasisImposterGenerator.CaptureActualTposeLocals(_sourceAnimator);

        _previewMesh = _payload.CreateMesh();
        _previewTexture = _payload.CreateTexture();
        if (_previewMesh == null || _previewTexture == null)
        {
            _lastError = "Decoded payload did not build a mesh/texture — see Console.";
            DestroyPreview();
            return;
        }

        Shader shader = Shader.Find("Basis/AvatarImposter");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
            Debug.LogWarning("Basis/AvatarImposter shader not found — previewing with URP Unlit.");
        }
        _previewMaterial = new Material(shader) { mainTexture = _previewTexture };

        _previewRoot = new GameObject($"Imposter Preview ({_avatar.name})")
        {
            hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild,
        };

        int boneCount = _payload.BoneCount;
        _previewBones = new Transform[boneCount];
        _sourceBones = new Transform[boneCount];
        for (int i = 0; i < boneCount; i++)
        {
            HumanBodyBones bone = (HumanBodyBones)_payload.BoneHumanBodyBone[i];
            GameObject boneObject = new GameObject(bone.ToString());
            Transform boneTransform = boneObject.transform;
            byte parent = _payload.BoneParentIndex[i];
            boneTransform.SetParent(parent == 0xFF ? _previewRoot.transform : _previewBones[parent], false);
            boneTransform.SetLocalPositionAndRotation(_payload.BoneRestLocalPosition[i], _payload.BoneRestLocalRotation[i]);
            _previewBones[i] = boneTransform;
            _sourceBones[i] = _sourceAnimator.GetBoneTransform(bone);
        }
        int hipsIndex = _payload.FindBone(HumanBodyBones.Hips);
        _previewHips = hipsIndex >= 0 ? _previewBones[hipsIndex] : _previewBones[0];

        GameObject meshObject = new GameObject("Mesh");
        meshObject.transform.SetParent(_previewRoot.transform, false);
        SkinnedMeshRenderer renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = _previewMesh;
        renderer.sharedMaterial = _previewMaterial;
        renderer.bones = _previewBones;
        renderer.rootBone = _previewHips;
        renderer.localBounds = new Bounds(_payload.LocalBoundsCenter, _payload.LocalBoundsExtents * 2f);
        renderer.quality = SkinQuality.Bone2;
        renderer.updateWhenOffscreen = false;

        PositionPreviewRoot();
        if (_mirrorPose)
        {
            ApplyMirrorPose();
        }
        SceneView.RepaintAll();
    }

    private float ResolveOffset()
    {
        if (_previewOffset > 0.0001f)
        {
            return _previewOffset;
        }
        float width = _payload.PositionBoundsMax.x - _payload.PositionBoundsMin.x;
        float scale = Mathf.Max(_sourceAnimator.transform.localScale.x, 0.01f);
        return width * scale * 1.25f + 0.25f;
    }

    private void PositionPreviewRoot()
    {
        Transform source = _sourceAnimator.transform;
        Vector3 offset = source.rotation * Vector3.right * ResolveOffset();
        _previewRoot.transform.SetPositionAndRotation(source.position + offset, source.rotation);
        _previewRoot.transform.localScale = source.localScale;
    }

    private void ApplyMirrorPose()
    {
        Vector3 offset = _previewRoot.transform.position - _sourceAnimator.transform.position;
        for (int i = 0; i < _previewBones.Length; i++)
        {
            HumanBodyBones bone = (HumanBodyBones)_payload.BoneHumanBodyBone[i];
            if (bone == HumanBodyBones.Hips)
            {
                continue;
            }
            Transform source = _sourceBones[i];
            if (source == null || !_sourceTposeLocals.TryGetValue(bone, out Quaternion tposeLocal))
            {
                continue;
            }
            // Exactly the wire composition: delta is the source bone's rotation off its own
            // T-pose local frame, applied on top of the imposter's collapsed rest local.
            Quaternion delta = Quaternion.Inverse(tposeLocal) * source.localRotation;
            _previewBones[i].localRotation = _payload.BoneRestLocalRotation[i] * delta;
        }

        Transform sourceHips = _sourceAnimator.GetBoneTransform(HumanBodyBones.Hips);
        if (sourceHips != null)
        {
            // Hips is world-applied at runtime (ApplyHipsWorldJob) — copy world plus the view offset.
            _previewHips.SetPositionAndRotation(sourceHips.position + offset, sourceHips.rotation);
        }
    }

    private void OnEditorUpdate()
    {
        if (_previewRoot == null || _avatar == null || _sourceAnimator == null)
        {
            return;
        }
        PositionPreviewRoot();
        if (_mirrorPose)
        {
            ApplyMirrorPose();
        }
    }

    private void DestroyScenePreviewOnly()
    {
        if (_previewRoot != null)
        {
            DestroyImmediate(_previewRoot);
        }
        _previewRoot = null;
        _previewBones = null;
        _previewHips = null;
        _sourceBones = null;
    }

    private void DestroyPreview()
    {
        DestroyScenePreviewOnly();
        if (_previewMesh != null)
        {
            DestroyImmediate(_previewMesh);
        }
        if (_previewTexture != null)
        {
            DestroyImmediate(_previewTexture);
        }
        if (_previewMaterial != null)
        {
            DestroyImmediate(_previewMaterial);
        }
        _previewMesh = null;
        _previewTexture = null;
        _previewMaterial = null;
    }
}
