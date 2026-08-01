using System;
using System.IO;
using Basis.Editor.Localization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Renders an icon for a piece of Basis content and writes it into the project as a PNG.
///
/// <para>Two capture paths, because the three content types want different framing: models
/// (props, avatars) render isolated in a preview scene so nothing around them ends up in the
/// shot, while a whole scene can only be photographed through a camera that is actually in it.</para>
///
/// <para>The result is written to disk as a real texture asset rather than kept in memory, so the
/// icon survives domain reloads and can be reassigned by hand later. Assignment goes through the
/// content's <see cref="SerializedObject"/> so Ctrl+Z restores the previous icon — the PNG itself
/// stays on disk, the same way undoing any "create asset" button in Unity leaves the file behind.</para>
/// </summary>
public static class BasisIconCapture
{
    /// <summary>Serialized path of the icon, shared by every <see cref="BasisContentBase"/>.</summary>
    public const string IconPropertyPath = "BasisBundleDescription.AssetBundleIcon";

    /// <summary>
    /// What lands on disk. Twice the imported size so the importer downscale does the
    /// anti-aliasing for us — cheaper and more predictable than asking the pipeline for MSAA
    /// into an offscreen target.
    /// </summary>
    public const int CaptureSize = 1024;

    /// <summary>
    /// What the importer clamps to. Matches the ceiling BasisTextureCompression enforces when the
    /// icon is packed into the bee file, so what the author sees is what ships.
    /// </summary>
    public const int IconSize = 512;

    // Matches the preview background used by the Bee explorer and the FarLod tester, so an icon
    // generated here sits next to those previews without looking like it came from elsewhere.
    private static readonly Color PreviewBackground = new Color(0.16f, 0.17f, 0.19f, 1f);
    private static readonly Color PreviewAmbient = new Color(0.32f, 0.32f, 0.34f, 1f);

    /// <summary>Three-quarter view from slightly above: reads well for a prop sitting on a shelf.</summary>
    public const float PropYaw = 30f;
    public const float PropPitch = 18f;

    /// <summary>Near-front for an avatar — enough angle to show depth, not enough to hide the face.</summary>
    public const float AvatarYaw = 15f;
    public const float AvatarPitch = 6f;

    /// <summary>
    /// Renders <paramref name="source"/> on its own in a preview scene, framed to its renderers.
    /// The caller owns the returned texture and must destroy it.
    /// </summary>
    public static Texture2D CaptureGameObject(GameObject source, float yaw, float pitch)
    {
        if (source == null)
        {
            return null;
        }

        PreviewRenderUtility preview = new PreviewRenderUtility();
        GameObject clone = null;
        try
        {
            clone = Object.Instantiate(source);
            preview.AddSingleGO(clone);
            clone.SetActive(true);

            if (!TryComputeRendererBounds(clone, out Bounds bounds))
            {
                return null;
            }

            FrameCamera(preview, bounds, yaw, pitch);
            preview.BeginStaticPreview(new Rect(0f, 0f, CaptureSize, CaptureSize));
            preview.Render(true);
            return preview.EndStaticPreview();
        }
        finally
        {
            if (clone != null)
            {
                Object.DestroyImmediate(clone);
            }
            preview.Cleanup();
        }
    }

    /// <summary>
    /// Renders what <paramref name="source"/> sees, through that camera itself rather than a copy:
    /// a copy would lose the pipeline's per-camera data (post processing, renderer choice,
    /// background handling) and give an icon that doesn't match what players will see. Its render
    /// target and aspect are restored in a finally, so a failure mid-capture can't leave the
    /// scene's camera pointing at a texture.
    /// </summary>
    public static Texture2D CaptureFromCamera(Camera source)
    {
        if (source == null)
        {
            return null;
        }

        RenderTexture previousTarget = source.targetTexture;
        try
        {
            // Square target, square framing — an icon rendered at the game's aspect and then
            // squashed into a square slot would not match what the author framed.
            source.aspect = 1f;
            return RenderIntoIcon(source);
        }
        finally
        {
            source.targetTexture = previousTarget;
            source.ResetAspect();
        }
    }

    /// <summary>
    /// Renders the scene from wherever the scene view is looking, minus the grid, gizmos and
    /// selection outlines the scene view draws on top of it.
    /// </summary>
    public static Texture2D CaptureFromSceneView()
    {
        SceneView view = SceneView.lastActiveSceneView;
        Camera sceneCamera = view != null ? view.camera : null;
        if (sceneCamera == null)
        {
            return null;
        }

        Transform sourceTransform = sceneCamera.transform;
        return RenderThroughTemporaryCamera(camera =>
        {
            camera.transform.SetPositionAndRotation(sourceTransform.position, sourceTransform.rotation);
            camera.orthographic = sceneCamera.orthographic;
            camera.orthographicSize = sceneCamera.orthographicSize;
            camera.fieldOfView = sceneCamera.fieldOfView;
            camera.nearClipPlane = sceneCamera.nearClipPlane;
            camera.farClipPlane = sceneCamera.farClipPlane;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = PreviewBackground;
            // The scene view hides layers independently of the game view; an icon taken from it
            // should show what the author is actually looking at.
            camera.cullingMask = Tools.visibleLayers;
        });
    }

    /// <summary>
    /// Runs a capture, asks where to put it, writes the PNG and points the content's icon at the
    /// imported asset in one undo step.
    /// </summary>
    /// <returns>Whether an icon was assigned, plus a message for the inspector to show.</returns>
    public static (bool success, string message) GenerateAndAssign(SerializedObject serializedObject, BasisContentBase content, Func<Texture2D> capture, string undoName)
    {
        if (content == null || capture == null)
        {
            return (false, BasisEditorLocalization.Get("sdk.commonInspector.icon.failed"));
        }

        Texture2D captured = null;
        try
        {
            try
            {
                captured = capture();
            }
            catch (Exception e)
            {
                BasisDebug.LogError($"Basis icon capture failed: {e}", BasisDebug.LogTag.Editor);
                return (false, BasisEditorLocalization.Get("sdk.commonInspector.icon.failed"));
            }

            if (captured == null)
            {
                return (false, BasisEditorLocalization.Get("sdk.commonInspector.icon.nothingToCapture"));
            }

            string path = EditorUtility.SaveFilePanelInProject(
                BasisEditorLocalization.Get("sdk.commonInspector.icon.saveTitle"),
                DefaultFileName(content),
                "png",
                BasisEditorLocalization.Get("sdk.commonInspector.icon.saveMessage"),
                DefaultDirectory(content));

            if (string.IsNullOrEmpty(path))
            {
                return (false, null); // cancelled — say nothing rather than report a failure
            }

            try
            {
                File.WriteAllBytes(path, captured.EncodeToPNG());
            }
            catch (Exception e)
            {
                BasisDebug.LogError($"Failed to write Basis icon to \"{path}\": {e}", BasisDebug.LogTag.Editor);
                return (false, BasisEditorLocalization.Get("sdk.commonInspector.icon.writeFailed", path));
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ApplyImporterSettings(path);

            Texture2D asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (asset == null)
            {
                return (false, BasisEditorLocalization.Get("sdk.commonInspector.icon.writeFailed", path));
            }

            Assign(serializedObject, content, asset, undoName);
            EditorGUIUtility.PingObject(asset);
            return (true, BasisEditorLocalization.Get("sdk.commonInspector.icon.saved", path));
        }
        finally
        {
            if (captured != null)
            {
                Object.DestroyImmediate(captured);
            }
        }
    }

    /// <summary>
    /// Writes the icon reference as its own undo step. Going through the SerializedObject is what
    /// makes Ctrl+Z work and what keeps prefab overrides honest — writing the field directly would
    /// do neither.
    /// </summary>
    public static void Assign(SerializedObject serializedObject, BasisContentBase content, Texture2D icon, string undoName)
    {
        if (content == null)
        {
            return;
        }

        Undo.IncrementCurrentGroup();

        SerializedProperty property = null;
        if (serializedObject != null)
        {
            serializedObject.Update();
            property = serializedObject.FindProperty(IconPropertyPath);
        }

        if (property != null)
        {
            property.objectReferenceValue = icon;
            serializedObject.ApplyModifiedProperties();
        }
        else
        {
            // Content whose serialized layout we can't resolve still gets an undo entry, just a
            // coarser one covering the whole component.
            Undo.RecordObject(content, undoName);
            if (content.BasisBundleDescription == null)
            {
                content.BasisBundleDescription = new BasisBundleDescription();
            }
            content.BasisBundleDescription.AssetBundleIcon = icon;
            EditorUtility.SetDirty(content);
            if (PrefabUtility.IsPartOfPrefabInstance(content))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(content);
            }
        }

        Undo.SetCurrentGroupName(undoName);
    }

    private static Texture2D RenderThroughTemporaryCamera(Action<Camera> configure)
    {
        GameObject holder = new GameObject("BasisIconCaptureCamera")
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        try
        {
            Camera camera = holder.AddComponent<Camera>();
            camera.enabled = false;
            configure(camera);
            camera.aspect = 1f;
            return RenderIntoIcon(camera);
        }
        finally
        {
            Object.DestroyImmediate(holder);
        }
    }

    /// <summary>
    /// Points a camera at a square offscreen target and reads the result back. RGB24 on purpose:
    /// what a pipeline leaves in the alpha channel of an offscreen target is its own business, and
    /// an icon that came back half transparent because of it would be a puzzling thing to debug.
    /// </summary>
    private static Texture2D RenderIntoIcon(Camera camera)
    {
        RenderTextureDescriptor descriptor = new RenderTextureDescriptor(CaptureSize, CaptureSize, RenderTextureFormat.ARGB32, 24)
        {
            // URP builds its whole intermediate chain from the descriptor of whatever target the
            // camera points at, and the descriptor constructor leaves sRGB off. Left that way the
            // pipeline never applies the linear-to-sRGB encode on the way out and the icon reads
            // back darker than the view it was taken from. Same line URP uses for its own targets.
            sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
        };

        RenderTexture target = RenderTexture.GetTemporary(descriptor);
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            SubmitRender(camera);

            RenderTexture.active = target;
            Texture2D icon = new Texture2D(CaptureSize, CaptureSize, TextureFormat.RGB24, false);
            icon.ReadPixels(new Rect(0f, 0f, CaptureSize, CaptureSize), 0, 0, false);
            icon.Apply(false, false);
            return icon;
        }
        finally
        {
            RenderTexture.active = previousActive;
            // Let go of the target before it is recycled — the caller puts back whatever the
            // camera was pointing at before, and it must never be pointing at a released one.
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    /// <summary>
    /// Same submission dance the FarLod baker does: prefer the render request when the pipeline
    /// supports it, and clear the camera's target for the duration because some URP versions land
    /// the wrong buffer in the readback when both are set.
    /// </summary>
    private static void SubmitRender(Camera camera)
    {
        RenderPipeline.StandardRequest request = new RenderPipeline.StandardRequest();
        if (!RenderPipeline.SupportsRenderRequest(camera, request))
        {
            camera.Render();
            return;
        }

        RenderTexture destination = camera.targetTexture;
        camera.targetTexture = null;
        try
        {
            request.destination = destination;
            RenderPipeline.SubmitRenderRequest(camera, request);
        }
        finally
        {
            camera.targetTexture = destination;
        }
    }

    private static void FrameCamera(PreviewRenderUtility preview, Bounds bounds, float yaw, float pitch)
    {
        Camera camera = preview.camera;
        camera.fieldOfView = 30f;
        camera.nearClipPlane = 0.01f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = PreviewBackground;

        float distance = bounds.extents.magnitude * 2.1f + 0.05f;
        Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
        camera.transform.SetPositionAndRotation(bounds.center + orbit * (Vector3.back * distance), orbit);
        camera.farClipPlane = distance * 6f + 10f;

        preview.lights[0].intensity = 1.2f;
        preview.lights[0].transform.rotation = Quaternion.Euler(40f, yaw - 30f, 0f);
        preview.lights[1].intensity = 0.4f;
        preview.ambientColor = PreviewAmbient;
    }

    /// <summary>
    /// Frames what will actually show up in the shot: visible renderers only, falling back to the
    /// hidden ones when there is nothing else, so a prop authored inactive still gets an icon.
    /// </summary>
    private static bool TryComputeRendererBounds(GameObject root, out Bounds bounds)
    {
        if (TryComputeRendererBounds(root, false, out bounds))
        {
            return true;
        }
        return TryComputeRendererBounds(root, true, out bounds);
    }

    private static bool TryComputeRendererBounds(GameObject root, bool includeInactive, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.one);
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(includeInactive);
        bool hasAny = false;
        for (int Index = 0; Index < renderers.Length; Index++)
        {
            Renderer renderer = renderers[Index];
            if (renderer == null)
            {
                continue;
            }
            if (!includeInactive && !renderer.enabled)
            {
                continue;
            }
            if (!hasAny)
            {
                bounds = renderer.bounds;
                hasAny = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasAny && bounds.extents.magnitude < 0.0001f)
        {
            // A single point-sized renderer would put the camera inside it.
            bounds.Expand(0.1f);
        }
        return hasAny;
    }

    private static void ApplyImporterSettings(string path)
    {
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
        {
            return;
        }

        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = IconSize;
        // The icon is referenced by the content, so it rides along inside the built bundle as well
        // as being embedded in the bee metadata. High-quality compression keeps that copy small
        // without the block artifacts plain DXT would put in front of users in the library, and
        // leaving it non-readable keeps the CPU copy out of memory at runtime — the build path
        // already blits unreadable textures when it needs their pixels.
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    private static string DefaultFileName(BasisContentBase content)
    {
        string name = content != null ? content.BasisBundleDescription?.AssetBundleName : null;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = content != null ? content.gameObject.name : "Basis";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        for (int Index = 0; Index < invalid.Length; Index++)
        {
            name = name.Replace(invalid[Index], '_');
        }

        name = name.Trim();
        return string.IsNullOrEmpty(name) ? "Basis Icon" : $"{name} Icon";
    }

    /// <summary>
    /// Opens the save panel next to whatever the content lives in — its prefab, or its scene — so
    /// the icon lands beside the thing it depicts instead of wherever the panel was last used.
    /// </summary>
    private static string DefaultDirectory(BasisContentBase content)
    {
        if (content == null)
        {
            return "Assets";
        }

        string source = AssetDatabase.GetAssetPath(content);
        if (string.IsNullOrEmpty(source) && PrefabUtility.IsPartOfPrefabInstance(content))
        {
            source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(content.gameObject);
        }
        if (string.IsNullOrEmpty(source))
        {
            source = content.gameObject.scene.path;
        }
        if (string.IsNullOrEmpty(source))
        {
            return "Assets";
        }

        string directory = Path.GetDirectoryName(source);
        if (string.IsNullOrEmpty(directory))
        {
            return "Assets";
        }

        directory = directory.Replace('\\', '/');
        // The save panel only writes inside the project, and package folders are read-only for
        // most installs — anything outside Assets falls back to the project root.
        return directory.StartsWith("Assets", StringComparison.Ordinal) ? directory : "Assets";
    }
}
