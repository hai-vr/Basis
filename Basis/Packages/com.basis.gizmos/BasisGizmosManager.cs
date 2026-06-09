using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public static class BasisGizmoManager
{
    public static Dictionary<int, BasisGizmos> Gizmos = new Dictionary<int, BasisGizmos>();
    public static Dictionary<int, BasisLineGizmos> GizmosLine = new Dictionary<int, BasisLineGizmos>();
    public static Dictionary<int, BasisTextGizmos> GizmosText = new Dictionary<int, BasisTextGizmos>();
    private static int _nextID = 0; // Counter for unique IDs.

    public static string MaterialGizmo = "GizmoMaterial";
    public static string GameobjectGizmo = "Packages/com.basis.gizmos/Gizmo.prefab";
    public static string GameobjectGizmoLine = "Packages/com.basis.gizmos/GizmoLine.prefab";
    public static Action<bool> OnUseGizmosChanged; // Callback delegate.
    public static GameObject LoadedLineGizmo;
    public static GameObject LoadedGizmo;
    private static Material _lineGizmoMaterial;

    private static Material GetLineGizmoMaterial()
    {
        if (_lineGizmoMaterial == null)
        {
            _lineGizmoMaterial = new Material(Shader.Find("Sprites/Default"));
        }
        return _lineGizmoMaterial;
    }
    public static GameObject Parent;
    public static void TryCreateParent()
    {
        if (Parent == null)
        {
            Parent = new GameObject("Parent Of Debug Data");
        }
    }
    public static void DestroyParent()
    {
        if(Parent != null)
        {
            GameObject.Destroy(Parent);
            Parent = null;
        }
    }
    /// <summary>
    /// Creates a new sphere gizmo.
    /// </summary>
    public static bool CreateSphereGizmo(string GizmoName,out int linkedID, Vector3 position, float size, Color color)
    {
        TryCreateParent();
        linkedID = CreateNewID();

        if (Gizmos.ContainsKey(linkedID))
        {
            BasisDebug.LogError($"Gizmo with ID {linkedID} already exists. Use UpdateSphereGizmo to modify it.", BasisDebug.LogTag.Gizmo);
            return false;
        }

        if (LoadedGizmo == null)
        {
            AsyncOperationHandle<GameObject> Loadable = Addressables.LoadAssetAsync<GameObject>(GameobjectGizmo);
            LoadedGizmo = Loadable.WaitForCompletion();
        }
        if (LoadedGizmo == null)
        {
            BasisDebug.LogError($"Failed to load Gizmo prefab from {GameobjectGizmo}", BasisDebug.LogTag.Gizmo);
            return false;
        }

        GameObject tempSphere = UnityEngine.Object.Instantiate(LoadedGizmo, Parent.transform);
        tempSphere.name = $"SphereGizmo_{linkedID}";

        AsyncOperationHandle<Material> materialLoad = Addressables.LoadAssetAsync<Material>(MaterialGizmo);
        Material material = materialLoad.WaitForCompletion();
        if (material == null)
        {
            BasisDebug.LogError($"Failed to load Gizmo material from {MaterialGizmo}", BasisDebug.LogTag.Gizmo);
            UnityEngine.Object.Destroy(tempSphere);
            return false;
        }

        if (tempSphere.TryGetComponent(out BasisGizmos basisGizmos))
        {
            basisGizmos.ConfigureMeshGizmo(material, Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx"), color);
            Gizmos[linkedID] = basisGizmos;
            tempSphere.name = GizmoName;
            basisGizmos.UpdatePosition(position);
            basisGizmos.UpdateSize(Vector3.one * size);

            BasisDebug.Log($"Created SphereGizmo with ID {linkedID}", BasisDebug.LogTag.Gizmo);
            return true;
        }
        else
        {
            BasisDebug.LogError($"Prefab missing BasisGizmos component.", BasisDebug.LogTag.Gizmo);
            UnityEngine.Object.Destroy(tempSphere);
            return false;
        }
    }

    /// <summary>
    /// Updates an existing sphere gizmo.
    /// </summary>
    public static bool UpdateSphereGizmo(int linkedID, Vector3 position,Vector3 Scale)
    {
        if (!Gizmos.TryGetValue(linkedID, out BasisGizmos gizmo))
        {
            BasisDebug.LogError($"No SphereGizmo found with ID {linkedID}. Use CreateSphereGizmo first.", BasisDebug.LogTag.Gizmo);
            return false;
        }

        gizmo.UpdatePosition(position);
        gizmo.UpdateSize(Scale);
        return true;
    }

    /// <summary>
    /// Updates an existing sphere gizmo with rotation included. Use this when
    /// the gizmo is anchored to a bone whose orientation matters
    /// (e.g. calibration spheres so they roll with the avatar's bone instead
    /// of staying axis-aligned in world space).
    /// </summary>
    public static bool UpdateSphereGizmo(int linkedID, Vector3 position, Quaternion rotation, Vector3 Scale)
    {
        if (!Gizmos.TryGetValue(linkedID, out BasisGizmos gizmo))
        {
            BasisDebug.LogError($"No SphereGizmo found with ID {linkedID}. Use CreateSphereGizmo first.", BasisDebug.LogTag.Gizmo);
            return false;
        }

        gizmo.UpdatePositionAndRotation(position, rotation);
        gizmo.UpdateSize(Scale);
        return true;
    }
    public static bool CreateLineGizmo(string GizmoName,int linkedID, Vector3 start, Vector3 end, float width, Color color,GameObject Reference)
    {
        TryCreateParent();
        GameObject gizmoObject = UnityEngine.Object.Instantiate(Reference,Parent.transform);
        if (gizmoObject.TryGetComponent(out BasisLineGizmos basisGizmos))
        {
            LineRenderer lineRenderer = basisGizmos.LineRenderer;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPositions(new[] { start, end });
            lineRenderer.startWidth = width;
            lineRenderer.endWidth = width;
            lineRenderer.sharedMaterial = GetLineGizmoMaterial();
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.name = GizmoName;
            GizmosLine[linkedID] = basisGizmos;

            BasisDebug.Log($"Created LineGizmo with ID {linkedID}", BasisDebug.LogTag.Gizmo);
            return true;
        }
        else
        {
            BasisDebug.LogError($"Prefab missing BasisLineGizmos component.", BasisDebug.LogTag.Gizmo);
            UnityEngine.Object.Destroy(gizmoObject);
            return false;
        }
    }
    public static bool CreateLineGizmo(string GizmoName,out int linkedID, Vector3 start, Vector3 end, float width, Color color)
    {
        linkedID = CreateNewID();
        if (GizmosLine.ContainsKey(linkedID))
        {
            BasisDebug.LogError($"LineGizmo with ID {linkedID} already exists. Use UpdateLineGizmo to modify it.", BasisDebug.LogTag.Gizmo);
            return false;
        }
        if (LoadedLineGizmo == null)
        {
            AsyncOperationHandle<GameObject> LoadAble = Addressables.LoadAssetAsync<GameObject>(GameobjectGizmoLine);
            LoadedLineGizmo = LoadAble.WaitForCompletion();
        }
        if (LoadedLineGizmo == null)
        {
            BasisDebug.LogError($"Failed to load LineGizmo prefab from {GameobjectGizmoLine}", BasisDebug.LogTag.Gizmo);
            return false;
        }
        return CreateLineGizmo(GizmoName,linkedID, start, end, width, color, LoadedLineGizmo);
    }

    /// <summary>
    /// Updates an existing line gizmo.
    /// </summary>
    public static bool UpdateLineGizmo(int linkedID, Vector3 start, Vector3 end)
    {
        if (!GizmosLine.TryGetValue(linkedID, out BasisLineGizmos gizmo))
        {
            BasisDebug.LogError($"No LineGizmo found with ID {linkedID}. Use CreateLineGizmo first.", BasisDebug.LogTag.Gizmo);
            return false;
        }
        if (gizmo == null || gizmo.LineRenderer == null)
        {
            return false;
        }
        gizmo.LineRenderer.SetPosition(0, start);
        gizmo.LineRenderer.SetPosition(1, end);
        return true;
    }

    /// <summary>
    /// Creates a multi-point line gizmo. Set <paramref name="loop"/> = true to close the
    /// polyline back to its first point — useful for drawing circles or wireframe caps
    /// with a single LineRenderer rather than N edge segments.
    /// </summary>
    public static bool CreateLineGizmo(string GizmoName, out int linkedID, Vector3[] positions, float width, Color color, bool loop = false)
    {
        linkedID = CreateNewID();
        if (GizmosLine.ContainsKey(linkedID))
        {
            BasisDebug.LogError($"LineGizmo with ID {linkedID} already exists. Use UpdateLineGizmo to modify it.", BasisDebug.LogTag.Gizmo);
            return false;
        }
        if (LoadedLineGizmo == null)
        {
            AsyncOperationHandle<GameObject> LoadAble = Addressables.LoadAssetAsync<GameObject>(GameobjectGizmoLine);
            LoadedLineGizmo = LoadAble.WaitForCompletion();
        }
        if (LoadedLineGizmo == null)
        {
            BasisDebug.LogError($"Failed to load LineGizmo prefab from {GameobjectGizmoLine}", BasisDebug.LogTag.Gizmo);
            return false;
        }

        TryCreateParent();
        GameObject gizmoObject = UnityEngine.Object.Instantiate(LoadedLineGizmo, Parent.transform);
        if (gizmoObject.TryGetComponent(out BasisLineGizmos basisGizmos))
        {
            LineRenderer lineRenderer = basisGizmos.LineRenderer;
            lineRenderer.positionCount = positions.Length;
            lineRenderer.SetPositions(positions);
            lineRenderer.startWidth = width;
            lineRenderer.endWidth = width;
            lineRenderer.sharedMaterial = GetLineGizmoMaterial();
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.loop = loop;
            lineRenderer.name = GizmoName;
            GizmosLine[linkedID] = basisGizmos;

            BasisDebug.Log($"Created LineGizmo (multi-point) with ID {linkedID}, count={positions.Length}, loop={loop}", BasisDebug.LogTag.Gizmo);
            return true;
        }
        else
        {
            BasisDebug.LogError($"Prefab missing BasisLineGizmos component.", BasisDebug.LogTag.Gizmo);
            UnityEngine.Object.Destroy(gizmoObject);
            return false;
        }
    }

    /// <summary>
    /// Updates an existing multi-point line gizmo. Reuses the existing LineRenderer's
    /// position buffer, only resizing if the point count actually changed.
    /// </summary>
    public static bool UpdateLineGizmo(int linkedID, Vector3[] positions)
    {
        if (!GizmosLine.TryGetValue(linkedID, out BasisLineGizmos gizmo))
        {
            BasisDebug.LogError($"No LineGizmo found with ID {linkedID}. Use CreateLineGizmo first.", BasisDebug.LogTag.Gizmo);
            return false;
        }
        if (gizmo == null || gizmo.LineRenderer == null)
        {
            return false;
        }
        LineRenderer lr = gizmo.LineRenderer;
        if (lr.positionCount != positions.Length)
        {
            lr.positionCount = positions.Length;
        }
        lr.SetPositions(positions);
        return true;
    }

    /// <summary>
    /// Replaces the line gizmo's color sampling with a Gradient — vertices interpolate
    /// across it by their normalized position. Use for multi-point lines that should
    /// fade through per-segment colors (skeleton chains with one stop per bone).
    /// </summary>
    public static bool SetLineGizmoGradient(int linkedID, Gradient gradient)
    {
        if (!GizmosLine.TryGetValue(linkedID, out BasisLineGizmos gizmo))
        {
            BasisDebug.LogError($"No LineGizmo found with ID {linkedID}. Use CreateLineGizmo first.", BasisDebug.LogTag.Gizmo);
            return false;
        }
        if (gizmo == null || gizmo.LineRenderer == null)
        {
            return false;
        }
        gizmo.LineRenderer.colorGradient = gradient;
        return true;
    }

    /// <summary>
    /// Creates a world-space text label. Built in code (no prefab); the TMP component
    /// picks up the project's default font. Drive it each frame with
    /// <see cref="UpdateTextGizmo"/> (which billboards + recolors only on change).
    /// </summary>
    public static bool CreateTextGizmo(string GizmoName, out int linkedID, Vector3 position, string text, Color color)
    {
        TryCreateParent();
        linkedID = CreateNewID();

        GameObject go = new GameObject(GizmoName);
        go.transform.SetParent(Parent.transform, false);
        go.transform.position = position;

        TMPro.TextMeshPro tmp = go.AddComponent<TMPro.TextMeshPro>();

        // AddComponent does NOT reliably assign a font at runtime — without one the
        // mesh is empty and the label is invisible. Assign the default explicitly.
        TMPro.TMP_FontAsset font = GetGizmoFont();
        if (font != null)
        {
            tmp.font = font;
        }

        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.enableAutoSizing = false;
        tmp.fontSize = 36;
        tmp.color = color;
        tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        tmp.overflowMode = TMPro.TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        tmp.isOrthographic = false;
        tmp.text = text;
        if (tmp.TryGetComponent(out RectTransform rt))
        {
            rt.sizeDelta = new Vector2(8f, 2f);
        }

        // Like the sphere/line gizmos, labels must draw ON TOP of the avatar/world
        // rather than be depth-occluded inside the body. TMP's default material
        // depth-tests; swap this instance to the Overlay variant (ZTest Always).
        Shader overlay = GetTextOverlayShader();
        if (overlay != null && tmp.fontMaterial != null)
        {
            tmp.fontMaterial.shader = overlay;
        }

        BasisTextGizmos holder = go.AddComponent<BasisTextGizmos>();
        holder.Text = tmp;
        holder.Initialize(text, color);
        GizmosText[linkedID] = holder;

        BasisDebug.Log($"Created TextGizmo with ID {linkedID}", BasisDebug.LogTag.Gizmo);
        return true;
    }

    private static TMPro.TMP_FontAsset _gizmoFont;
    private static bool _gizmoFontResolved;
    private static Shader _textOverlayShader;
    private static bool _textOverlayResolved;

    private static TMPro.TMP_FontAsset GetGizmoFont()
    {
        if (_gizmoFontResolved)
        {
            return _gizmoFont;
        }
        _gizmoFont = TMPro.TMP_Settings.defaultFontAsset;
        if (_gizmoFont == null)
        {
            _gizmoFont = Resources.Load<TMPro.TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }
        if (_gizmoFont == null)
        {
            _gizmoFont = Resources.Load<TMPro.TMP_FontAsset>("LiberationSans SDF");
        }
        if (_gizmoFont == null)
        {
            BasisDebug.LogError("TextGizmo: no TMP font asset found — labels will be invisible. Import TMP Essentials / set a default font asset.", BasisDebug.LogTag.Gizmo);
        }
        _gizmoFontResolved = true;
        return _gizmoFont;
    }

    private static Shader GetTextOverlayShader()
    {
        if (_textOverlayResolved)
        {
            return _textOverlayShader;
        }
        _textOverlayShader = Shader.Find("TextMeshPro/Distance Field Overlay");
        _textOverlayResolved = true;
        return _textOverlayShader;
    }

    /// <summary>
    /// Updates a text label's pose, scale, text and color in one call. Text and color
    /// are only re-applied when they actually changed, so a static label costs just a
    /// transform write (cheap billboard) per frame. <paramref name="rotation"/> is
    /// typically a billboard facing the listener camera.
    /// </summary>
    public static bool UpdateTextGizmo(int linkedID, Vector3 position, Quaternion rotation, float scale, string text, Color color)
    {
        if (!GizmosText.TryGetValue(linkedID, out BasisTextGizmos gizmo) || gizmo == null)
        {
            return false;
        }
        gizmo.Apply(position, rotation, scale);
        gizmo.SetText(text);
        gizmo.SetColor(color);
        return true;
    }

    /// <summary>
    /// Rotation that faces a world point toward a camera, upright. Shared by every
    /// gizmo system that billboards a text label so the math lives in one place.
    /// </summary>
    public static Quaternion BillboardRotation(Vector3 worldPos, Vector3 cameraPos)
    {
        Vector3 dir = worldPos - cameraPos;
        if (dir.sqrMagnitude < 1e-6f)
        {
            dir = Vector3.forward;
        }
        return Quaternion.LookRotation(dir, Vector3.up);
    }

    /// <summary>
    /// Recolors an existing gizmo (sphere or line) in place. Sphere gizmos own an
    /// Instantiated material so mutating its color is safe; line gizmos carry color
    /// as per-renderer vertex colors (startColor/endColor), independent of the shared
    /// line material. Use for gizmos whose color encodes a live value each frame
    /// (e.g. audio gain) — cheaper and alloc-free compared to SetLineGizmoGradient.
    /// </summary>
    public static bool UpdateGizmoColor(int linkedID, Color color)
    {
        if (Gizmos.TryGetValue(linkedID, out BasisGizmos gizmo) && gizmo != null)
        {
            if (gizmo.MeshRenderer != null && gizmo.MeshRenderer.sharedMaterial != null)
            {
                gizmo.MeshRenderer.sharedMaterial.color = color;
            }
            return true;
        }
        if (GizmosLine.TryGetValue(linkedID, out BasisLineGizmos lineGizmo) && lineGizmo != null && lineGizmo.LineRenderer != null)
        {
            lineGizmo.LineRenderer.startColor = color;
            lineGizmo.LineRenderer.endColor = color;
            return true;
        }
        if (GizmosText.TryGetValue(linkedID, out BasisTextGizmos textGizmo) && textGizmo != null)
        {
            textGizmo.SetColor(color);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Toggles a gizmo's GameObject visibility without destroying it. Used by
    /// sub-toggles that hide/show subsets of gizmos under the master ShowGizmos.
    /// </summary>
    public static void SetGizmoActive(int linkedID, bool active)
    {
        if (Gizmos.TryGetValue(linkedID, out BasisGizmos gizmo) && gizmo != null)
        {
            gizmo.gameObject.SetActive(active);
            return;
        }
        if (GizmosLine.TryGetValue(linkedID, out BasisLineGizmos lineGizmo) && lineGizmo != null)
        {
            lineGizmo.gameObject.SetActive(active);
            return;
        }
        if (GizmosText.TryGetValue(linkedID, out BasisTextGizmos textGizmo) && textGizmo != null)
        {
            textGizmo.gameObject.SetActive(active);
        }
    }

    /// <summary>
    /// Destroys a gizmo with the specified ID.
    /// </summary>
    public static void DestroyGizmo(int linkedID)
    {
        if (Gizmos.Remove(linkedID, out BasisGizmos gizmo))
        {
            UnityEngine.Object.Destroy(gizmo.gameObject);
            BasisDebug.Log($"Destroyed SphereGizmo with ID {linkedID}", BasisDebug.LogTag.Gizmo);
        }
        else if (GizmosLine.Remove(linkedID, out BasisLineGizmos lineGizmo))
        {
            UnityEngine.Object.Destroy(lineGizmo.gameObject);
            BasisDebug.Log($"Destroyed LineGizmo with ID {linkedID}", BasisDebug.LogTag.Gizmo);
        }
        else if (GizmosText.Remove(linkedID, out BasisTextGizmos textGizmo))
        {
            UnityEngine.Object.Destroy(textGizmo.gameObject);
            BasisDebug.Log($"Destroyed TextGizmo with ID {linkedID}", BasisDebug.LogTag.Gizmo);
        }
        else
        {
            BasisDebug.LogWarning($"No Gizmo found with ID {linkedID} to destroy.", BasisDebug.LogTag.Gizmo);
        }
    }

    /// <summary>
    /// Creates a new unique ID for a gizmo.
    /// </summary>
    private static int CreateNewID()
    {
        return ++_nextID;
    }
}
