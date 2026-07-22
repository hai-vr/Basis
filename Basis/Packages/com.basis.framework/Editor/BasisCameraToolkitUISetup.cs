using Basis.Scripts.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Basis.Editor
{
    /// <summary>
    /// Attaches the UI Toolkit camera surface to the handheld camera prefab.
    ///
    /// Additive on purpose: the existing uGUI canvas is left in place and simply deactivated,
    /// so the change is reversible and a problem with the new panel cannot regress a working
    /// camera. Delete the old canvas by hand once the Toolkit surface is signed off.
    /// </summary>
    public static class BasisCameraToolkitUISetup
    {
        private const string PrefabPath = "Packages/com.basis.sdk/Prefabs/UI/Camera Prefab/Player Held Camera.prefab";
        private const string ToolkitFolder = "Packages/com.basis.sdk/Prefabs/UI/Camera Prefab/UIToolkit";
        private const string ThemePath = ToolkitFolder + "/CameraRuntimeTheme.tss";
        private const string UxmlPath = ToolkitFolder + "/BasisCameraToolkit.uxml";
        private const string PanelSettingsPath = ToolkitFolder + "/CameraPanelSettings.asset";
        private const string ToolkitChildName = "UIToolkit Camera UI";
        private const string LegacyCanvasName = "UI";

        // Matches the legacy canvas (3400x2400 local units under the prop root's 0.00015 scale),
        // so the Toolkit surface lands exactly where the uGUI one did and inherits avatar scaling.
        private static readonly Vector2 PanelLocalSize = new Vector2(3400f, 2400f);

        [MenuItem("Basis/Camera/Attach UI Toolkit Camera UI")]
        public static void Attach()
        {
            ThemeStyleSheet theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (theme == null || uxml == null)
            {
                Debug.LogError($"[CameraToolkitUI] Missing theme or UXML under {ToolkitFolder}. Reimport that folder and retry.");
                return;
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (prefabRoot == null)
            {
                Debug.LogError($"[CameraToolkitUI] Could not open {PrefabPath}.");
                return;
            }

            try
            {
                BasisHandHeldCamera camera = prefabRoot.GetComponentInChildren<BasisHandHeldCamera>(true);
                if (camera == null)
                {
                    Debug.LogError("[CameraToolkitUI] No BasisHandHeldCamera on the prefab; aborting.");
                    return;
                }

                PanelSettings panelSettings = GetOrCreatePanelSettings(theme);

                Transform existing = prefabRoot.transform.Find(ToolkitChildName);
                GameObject host = existing != null ? existing.gameObject : new GameObject(ToolkitChildName);
                if (existing == null)
                {
                    host.transform.SetParent(prefabRoot.transform, false);
                }

                Transform legacyCanvas = prefabRoot.transform.Find(LegacyCanvasName);
                if (legacyCanvas != null)
                {
                    host.transform.SetLocalPositionAndRotation(legacyCanvas.localPosition, legacyCanvas.localRotation);
                    host.transform.localScale = legacyCanvas.localScale;
                    legacyCanvas.gameObject.SetActive(false);
                }

                UIDocument document = GetOrAdd<UIDocument>(host);
                document.panelSettings = panelSettings;
                document.visualTreeAsset = uxml;
                document.worldSpaceSize = PanelLocalSize;
                SetFixedSizeMode(document);

                BasisUIToolkitPanel panel = GetOrAdd<BasisUIToolkitPanel>(host);
                panel.Document = document;

                BasisHandHeldCameraToolkitUI binder = GetOrAdd<BasisHandHeldCameraToolkitUI>(host);
                binder.Document = document;
                binder.HandHeldCamera = camera;

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log(
                    $"[CameraToolkitUI] Attached '{ToolkitChildName}' to the camera prefab and deactivated the legacy '{LegacyCanvasName}' canvas.\n" +
                    "The uGUI canvas is only disabled, not removed — re-enable it to fall back.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        [MenuItem("Basis/Camera/Revert To uGUI Camera UI")]
        public static void Revert()
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (prefabRoot == null)
            {
                return;
            }

            try
            {
                Transform toolkit = prefabRoot.transform.Find(ToolkitChildName);
                if (toolkit != null)
                {
                    Object.DestroyImmediate(toolkit.gameObject);
                }

                Transform legacyCanvas = prefabRoot.transform.Find(LegacyCanvasName);
                if (legacyCanvas != null)
                {
                    legacyCanvas.gameObject.SetActive(true);
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log("[CameraToolkitUI] Reverted the camera prefab to the uGUI canvas.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static PanelSettings GetOrCreatePanelSettings(ThemeStyleSheet theme)
        {
            PanelSettings existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            PanelSettings settings = existing;
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            }

            settings.renderMode = PanelRenderMode.WorldSpace;
            settings.themeStyleSheet = theme;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }

        private static void SetFixedSizeMode(UIDocument document)
        {
            SerializedObject serialized = new SerializedObject(document);
            SerializedProperty sizeMode = serialized.FindProperty("m_WorldSpaceSizeMode");
            if (sizeMode != null)
            {
                sizeMode.enumValueIndex = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[CameraToolkitUI] Could not set World-Space Size Mode; set it to Fixed on the UIDocument by hand.");
            }
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            return target.TryGetComponent(out T existing) ? existing : target.AddComponent<T>();
        }
    }
}
