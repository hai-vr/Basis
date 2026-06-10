using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Assembles and wires the handheld camera's layer-visibility multi-select dropdown
/// directly onto the Player Held Camera prefab, then assigns the references the runtime
/// UI expects. Re-running rebuilds the panel in place.
/// Run via: Basis ▸ Camera ▸ Build Layer Visibility Dropdown.
/// </summary>
public static class BasisCameraLayerDropdownBuilder
{
    private const string PrefabPath = "Packages/com.basis.sdk/Prefabs/UI/Camera Prefab/Player Held Camera.prefab";
    private const string PanelName = "LayerDropdownPanel";

    [MenuItem("Basis/Camera/Build Layer Visibility Dropdown")]
    private static void Build()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            EditorUtility.DisplayDialog("Layer Dropdown", $"Could not load prefab at:\n{PrefabPath}", "OK");
            return;
        }

        try
        {
            BasisHandHeldCamera camera = root.GetComponentInChildren<BasisHandHeldCamera>(true);
            if (camera == null || camera.HandHeld == null)
            {
                EditorUtility.DisplayDialog("Layer Dropdown", "BasisHandHeldCamera (or its HandHeld UI) was not found in the prefab.", "OK");
                return;
            }

            BasisHandHeldCameraUI ui = camera.HandHeld;

            Button header = ResolveHeaderButton(root, ui);
            if (header == null)
            {
                EditorUtility.DisplayDialog("Layer Dropdown",
                    "Could not find the header button (legacy 'Nameplates'/'Layers' button). Assign LayerDropdownButton manually and re-run.", "OK");
                return;
            }

            ui.LayerDropdownButton = header;
            header.gameObject.name = "Layers";

            Transform stale = header.transform.Find(PanelName);
            if (stale != null)
                Object.DestroyImmediate(stale.gameObject);

            BuildPanel(header, ui);

            EditorUtility.SetDirty(camera);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[Basis] Layer visibility dropdown built and wired on Player Held Camera prefab.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Button ResolveHeaderButton(GameObject root, BasisHandHeldCameraUI ui)
    {
        if (ui.LayerDropdownButton != null)
            return ui.LayerDropdownButton;

        foreach (string name in new[] { "Layers", "Nameplates" })
        {
            Transform t = FindDeep(root.transform, name);
            if (t != null && t.TryGetComponent(out Button b))
                return b;
        }
        return null;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        if (parent.name == name)
            return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDeep(parent.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    private static void BuildPanel(Button header, BasisHandHeldCameraUI ui)
    {
        int layer = header.gameObject.layer;

        Sprite background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        Sprite box = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        Sprite checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;

        RectTransform panel = NewRect(PanelName, header.transform, layer);
        panel.anchorMin = new Vector2(0.5f, 1f);
        panel.anchorMax = new Vector2(0.5f, 1f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, 20f);
        panel.sizeDelta = new Vector2(360f, 0f);

        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = background;
        panelImage.type = Image.Type.Sliced;
        panelImage.color = new Color(0.10f, 0.10f, 0.12f, 0.95f);

        VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform titleRect = NewRect("Title", panel, layer);
        TextMeshProUGUI title = titleRect.gameObject.AddComponent<TextMeshProUGUI>();
        title.text = "Layers";
        title.fontSize = 30f;
        title.alignment = TextAlignmentOptions.Left;
        title.color = Color.white;
        title.raycastTarget = false;
        if (font != null) title.font = font;
        AddFixedHeight(titleRect.gameObject, 44f);

        Toggle template = BuildToggleTemplate(panel, layer, box, checkmark, font);
        template.gameObject.SetActive(false);

        ui.LayerDropdownPanel = panel.gameObject;
        ui.LayerToggleContent = panel;
        ui.LayerToggleTemplate = template;
        ui.LayerDropdownLabel = title;

        panel.gameObject.SetActive(false);
    }

    private static Toggle BuildToggleTemplate(RectTransform parent, int layer, Sprite box, Sprite checkmark, TMP_FontAsset font)
    {
        RectTransform row = NewRect("LayerToggleTemplate", parent, layer);
        Toggle toggle = row.gameObject.AddComponent<Toggle>();
        AddFixedHeight(row.gameObject, 44f);

        Image rowGraphic = row.gameObject.AddComponent<Image>();
        rowGraphic.color = new Color(1f, 1f, 1f, 0.04f);

        RectTransform boxRect = NewRect("Background", row, layer);
        boxRect.anchorMin = new Vector2(0f, 0.5f);
        boxRect.anchorMax = new Vector2(0f, 0.5f);
        boxRect.pivot = new Vector2(0f, 0.5f);
        boxRect.sizeDelta = new Vector2(32f, 32f);
        boxRect.anchoredPosition = new Vector2(6f, 0f);
        Image boxImage = boxRect.gameObject.AddComponent<Image>();
        boxImage.sprite = box;
        boxImage.type = Image.Type.Sliced;
        boxImage.color = Color.white;
        boxImage.raycastTarget = false;

        RectTransform checkRect = NewRect("Checkmark", boxRect, layer);
        Stretch(checkRect);
        Image checkImage = checkRect.gameObject.AddComponent<Image>();
        checkImage.sprite = checkmark;
        checkImage.color = new Color(0.20f, 0.85f, 0.35f, 1f);
        checkImage.raycastTarget = false;

        RectTransform labelRect = NewRect("Label", row, layer);
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(48f, 0f);
        labelRect.offsetMax = new Vector2(-8f, 0f);
        TextMeshProUGUI label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = "Layer";
        label.fontSize = 26f;
        label.alignment = TextAlignmentOptions.Left;
        label.color = Color.white;
        label.raycastTarget = false;
        if (font != null) label.font = font;

        toggle.targetGraphic = rowGraphic;
        toggle.graphic = checkImage;
        toggle.isOn = true;
        toggle.toggleTransition = Toggle.ToggleTransition.Fade;

        return toggle;
    }

    private static RectTransform NewRect(string name, Transform parent, int layer)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = layer;
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.localPosition = Vector3.zero;
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void AddFixedHeight(GameObject go, float height)
    {
        LayoutElement element = go.AddComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
    }
}
