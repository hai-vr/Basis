using Basis.Scripts.BasisSdk.Helpers.Editor;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(BasisProp))]
public class BasisPropSDKInspector : Editor
{
    public VisualTreeAsset visualTree;
    public BasisProp BasisProp;
    public VisualElement rootElement;
    public VisualElement uiElementsRoot;
    private Label resultLabel; // Store the result label for later clearing
    public BasisAssetBundleObject assetBundleObject;
    public static Texture2D Icon;
    public void OnEnable()
    {
        visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(BasisSDKConstants.PropuxmlPath);
        BasisProp = (BasisProp)target;
    }
    public override VisualElement CreateInspectorGUI()
    {
        BasisProp = (BasisProp)target;
        rootElement = new VisualElement();

        // Draw default inspector elements first
        InspectorElement.FillDefaultInspector(rootElement, serializedObject, this);

        if (visualTree != null)
        {
            uiElementsRoot = visualTree.CloneTree();
            rootElement.Add(uiElementsRoot);
            BasisSDKCommonInspector.CreateBuildTargetOptions(uiElementsRoot);
            BasisSDKCommonInspector.CreateBuildOptionsDropdown(uiElementsRoot);

            BasisAssetBundleObject assetBundleObject = AssetDatabase.LoadAssetAtPath<BasisAssetBundleObject>(BasisAssetBundleObject.AssetBundleObject);

            ObjectField PropIconField = uiElementsRoot.Q<ObjectField>(BasisSDKConstants.PropIcon);
            PropIconField.objectType = typeof(Texture2D);
            PropIconField.allowSceneObjects = true;
            PropIconField.value = Icon;
            PropIconField.RegisterCallback<ChangeEvent<UnityEngine.Object>>(OnIconFieldChanged);

            Button BuildButton = BasisHelpersGizmo.Button(uiElementsRoot, BasisSDKConstants.BuildButton);
            BuildButton.clicked += () => Build(BuildButton, assetBundleObject.selectedTargets, Icon);
        }
        else
        {
            Debug.LogError("VisualTree is null. Make sure the UXML file is assigned correctly.");
        }

        return rootElement;
    }

    private void OnIconFieldChanged(ChangeEvent<UnityEngine.Object> evt)
    {
        Icon = evt.newValue as Texture2D;
        BasisDebug.Log($"Setting to {Icon}");
    }

    private async void Build(Button buildButton, List<BuildTarget> targets, Texture2D Image)
    {
        if (targets == null || targets.Count == 0)
        {
            Debug.LogError("No build targets selected.");
            return;
        }
        if (Image == null)
        {
            Image = AssetPreview.GetAssetPreview(BasisProp.gameObject);
        }
        string ImageBytes = null;
        if (Image != null)
        {
            ImageBytes = BasisTextureCompression.ToPngBytes(Image);
        }
        Debug.Log($"Building Gameobject Bundles for: {string.Join(", ", targets.ConvertAll(t => BasisSDKConstants.targetDisplayNames[t]))}");
        BasisAssetBundleObject assetBundleObject = AssetDatabase.LoadAssetAtPath<BasisAssetBundleObject>(BasisAssetBundleObject.AssetBundleObject);
        (bool success, string message) = await BasisBundleBuild.GameObjectBundleBuild(ImageBytes, BasisProp, targets, assetBundleObject.UseCustomPassword, assetBundleObject.UserSelectedPassword);
        EditorUtility.ClearProgressBar();
        // Clear any previous result label
        ClearResultLabel();

        // Display new result in the UI
        resultLabel = new Label
        {
            style = { fontSize = 14 }
        };

        if (success)
        {
            resultLabel.text = "Build successful";
            resultLabel.style.backgroundColor = Color.green;
            resultLabel.style.color = Color.black; // Success message color
        }
        else
        {
            resultLabel.text = $"Build failed: {message}";
            resultLabel.style.backgroundColor = Color.red;
            resultLabel.style.color = Color.black; // Error message color
        }

        // Add the result label to the UI
        uiElementsRoot.Add(resultLabel);
       // BuildReportViewerWindow.ShowWindow();
    }

    // Method to clear the result label
    private void ClearResultLabel()
    {
        if (resultLabel != null)
        {
            uiElementsRoot.Remove(resultLabel);  // Remove the label from the UI
            resultLabel = null; // Optionally reset the reference to null
        }
    }
}
