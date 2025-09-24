#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.SceneManagement;

public partial class BasisProjectSetup : EditorWindow
{
    private enum PlatformChoice { Windows, Linux, Android }
    private enum FirstRunKind { None = 0, Avatar = 1, World = 2, Project = 3 }

    // EditorPrefs keys
    private const string PREF_LAST_PLATFORM = "BasisPlatformSwitcher_LastPlatform";
    private const string PREF_HAS_OPENED = "BasisPlatformSwitcher_HasOpened";
    private const string PREF_FIRST_RUN_KIND = "BasisPlatformSwitcher_FirstRunKind";

    // SessionState keys
    private const string SESSION_SHOW_FIRST_NOTICE = "BasisPlatformSwitcher_ShowFirstNotice";
    private const string SESSION_NEED_MODULE_RECHECK = "BasisPlatformSwitcher_NeedModuleRecheck";

    // Links
    private const string BASIS_SITE = "https://basisvr.org/";
    private const string BASIS_GETTING_STARTED = "https://docs.basisvr.org/docs/getting-started/";
    private const string BASIS_AVATARS = "https://docs.basisvr.org/docs/avatars/";
    private const string BASIS_WORLDS = "https://docs.basisvr.org/docs/worlds/";
    private const string BASIS_DONATE = "https://opencollective.com/basis";
    private const string UNITY_HUB_ADD_MODULES = "https://docs.unity3d.com/hub/manual/AddModules.html";

    // Package id we want gone on Linux
    private const string META_XR_CORE_PKG = "com.meta.xr.sdk.core";

    // Logo (Packages path)
    private const string BASIS_LOGO_PATH = "Packages/com.basis.sdk/Textures/BasisLogoTemp.png";
    private Texture2D _basisLogo;

    // Basis default scenes
    private const string SCENE_INIT = "Packages/com.basis.sdk/Scenes/initialization.unity";
    private const string SCENE_DEMO = "Packages/com.basis.examples/Scenes/DemoScene.unity";
    private const string SCENE_INTERACTABLES = "Packages/com.basis.examples/Scenes/InteractablesScene.unity";

    // Cached scene assets
    private SceneAsset _sceneInit;
    private SceneAsset _sceneDemo;
    private SceneAsset _sceneInteractables;

    // UI state
    private PlatformChoice _choice;
    private bool _showFirstRunNotice;
    private FirstRunKind _firstRunKind;

    // Enforce IL2CPP when applying
    private bool _enforceIl2cpp = true;

    // Cached module checks (session only)
    private bool? _hasWin;
    private bool? _hasLinux;
    private bool? _hasAndroid;

    private bool? _hasIl2cppStandalone; // Windows/Linux share Standalone group for backend availability
    private bool? _hasIl2cppAndroid;

    // Quality presets (1=Desktop, 2=Quest/Android)
    private const int QUALITY_DESKTOP = 1;
    private const int QUALITY_ANDROID = 2;

    // Package manager state
    private bool? _metaXrInstalled;        // null = unknown, true/false = known
    private ListRequest _pkgListReq;        // scanning
    private RemoveRequest _pkgRemoveReq;    // removing
    private string _pkgStatus;              // short status string

    [MenuItem("Basis/ProjectSetup")]
    public static void ShowWindow()
    {
        var window = GetWindow<BasisProjectSetup>("Basis Project Setup");
        window.minSize = new Vector2(560, 500);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        DrawHeader();
        EditorGUILayout.Space(8);

        if (_showFirstRunNotice)
        {
            EditorGUILayout.HelpBox(
                "First time here! Choose what you’re setting up, verify build modules (including IL2CPP), " +
                "and pick your target platform before building or pressing Play.",
                MessageType.Warning);
        }

        // Documentation + First-run selector
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Documentation", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Jump straight into the right docs.", EditorStyles.wordWrappedLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Avatar Docs")) Application.OpenURL(BASIS_AVATARS);
                if (GUILayout.Button("World Docs")) Application.OpenURL(BASIS_WORLDS);
                if (GUILayout.Button("Project – Getting Started")) Application.OpenURL(BASIS_GETTING_STARTED);
                if (GUILayout.Button("basisvr.org")) Application.OpenURL(BASIS_SITE);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("What are you setting up today?", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawFirstRunRadio(FirstRunKind.Avatar, "Avatar");
                DrawFirstRunRadio(FirstRunKind.World, "World");
                DrawFirstRunRadio(FirstRunKind.Project, "Project");
            }

            if (GUI.changed)
                EditorPrefs.SetInt(PREF_FIRST_RUN_KIND, (int)_firstRunKind);

            if (_firstRunKind == FirstRunKind.Avatar || _firstRunKind == FirstRunKind.World)
            {
                EditorGUILayout.HelpBox(
                    "For Avatars/Worlds you should install Windows, Linux, and Android Build Support via Unity Hub.\n" +
                    "Use IL2CPP for best compatibility/performance (required on Android).",
                    MessageType.Info);
            }
        }

        EditorGUILayout.Space();

        // Linux-only package warning/controls
        DrawLinuxMetaXrNotice();

        // Build modules + IL2CPP check
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Build Targets / Modules / IL2CPP", EditorStyles.boldLabel);

            if (!_hasWin.HasValue || !_hasLinux.HasValue || !_hasAndroid.HasValue || !_hasIl2cppStandalone.HasValue || !_hasIl2cppAndroid.HasValue)
                RecheckBuildModulesAndBackendsRow();
            else
                DrawModuleAndBackendStatusRow();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Build Settings")) EditorWindow.GetWindow(typeof(BuildPlayerWindow));
#if UNITY_2021_2_OR_NEWER
                if (EditorGUILayout.LinkButton("How to add modules in Unity Hub")) Application.OpenURL(UNITY_HUB_ADD_MODULES);
#else
                if (GUILayout.Button("How to add modules in Unity Hub", EditorStyles.linkLabel)) Application.OpenURL(UNITY_HUB_ADD_MODULES);
#endif
            }
        }

        EditorGUILayout.Space();

        // Platform & Quality
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Platform & Quality", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPlatformRadio(PlatformChoice.Windows, "Windows (Standalone)");
                DrawPlatformRadio(PlatformChoice.Linux, "Linux (Standalone)");
                DrawPlatformRadio(PlatformChoice.Android, "Android (Quest)");
            }

            _enforceIl2cpp = EditorGUILayout.ToggleLeft(
                "Enforce IL2CPP scripting backend when applying",
                _enforceIl2cpp);

            EditorGUILayout.HelpBox("Quality presets: 1 = Desktop (Windows/Linux), 2 = Android/Quest", MessageType.None);

            bool modulesOk = AreRequiredModulesOkForCurrentSelection();
            using (new EditorGUI.DisabledScope(!modulesOk && _enforceIl2cpp))
            {
                if (GUILayout.Button(modulesOk ? "Apply & Switch Platform" : "Apply & Switch Platform (modules missing)"))
                {
                    if (!modulesOk && _enforceIl2cpp)
                    {
                        EditorUtility.DisplayDialog(
                            "Missing Modules / IL2CPP",
                            "Required build modules or IL2CPP are missing for the current selection. See the warnings above.",
                            "Got it");
                    }
                    else
                    {
                        ApplyPlatformAndQuality(_choice, _enforceIl2cpp);
                        _showFirstRunNotice = false;
                    }
                }
            }
        }

        EditorGUILayout.Space();

        // Initial Scene & Build Setup
        DrawInitialSceneAndBuildSetup();
        EditorGUILayout.Space();

        // About
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("About Basis", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Creator-First, Creative Freedom — Basis lets you set up your own VR games with ease.\n" +
                "Open-Source (MIT). Designed for creators. Strong systems for networking, user input, and user presence.",
                EditorStyles.wordWrappedLabel);
#if UNITY_2021_2_OR_NEWER
            if (EditorGUILayout.LinkButton("Visit basisvr.org")) Application.OpenURL(BASIS_SITE);
#else
            if (GUILayout.Button("Visit basisvr.org", EditorStyles.linkLabel)) Application.OpenURL(BASIS_SITE);
#endif
        }

        EditorGUILayout.Space();

        // Funding
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("How We Are Funded", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "BasisVR is sustained by a mix of community donations and collaborations with companies who build on the framework. " +
                "Funds are pooled to tackle shared problems (e.g., networking, embodiment, tooling) that benefit everyone.",
                EditorStyles.wordWrappedLabel);
#if UNITY_2021_2_OR_NEWER
            if (EditorGUILayout.LinkButton("Support Basis on Open Collective")) Application.OpenURL(BASIS_DONATE);
#else
            if (GUILayout.Button("Support Basis on Open Collective", EditorStyles.linkLabel)) Application.OpenURL(BASIS_DONATE);
#endif
        }

        GUILayout.FlexibleSpace();
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close")) Close();
        }

        if (GUI.changed)
        {
            EditorPrefs.SetInt(PREF_LAST_PLATFORM, (int)_choice);
        }
    }

    // Header
    private void DrawHeader()
    {
        var rect = GUILayoutUtility.GetRect(10, 86, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color32(16, 15, 39, 255)); // #100f27
        var accent = new Rect(rect.x, rect.y, rect.width, 4);
        EditorGUI.DrawRect(accent, new Color32(239, 18, 55, 255)); // #ef1237

        float ppp = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
        float pad = 12f;
        float logoSize = 56f * ppp;

        var title = new Rect(rect.x + pad, rect.y + 10, rect.width - (logoSize + pad * 2f), 28);
        var subtitle = new Rect(rect.x + pad, rect.y + 40, rect.width - (logoSize + pad * 2f), 40);

        var tStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16,
            normal = { textColor = Color.white }
        };

        var subtitleColor = EditorGUIUtility.isProSkin
            ? new Color(0.85f, 0.85f, 0.9f)
            : new Color(0.15f, 0.15f, 0.2f);

        var sStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 11,
            wordWrap = true,
            normal = { textColor = subtitleColor }
        };

        GUI.Label(title, "Basis Project Wizard", tStyle);
        GUI.Label(subtitle,
            "Creator-First • Creative Freedom\n" +
            "Open-Source (MIT) • Networking • Input • Presence",
            sStyle);

        if (_basisLogo != null)
        {
            var logoRect = new Rect(
                rect.xMax - logoSize - pad,
                rect.y + (rect.height - logoSize) * 0.5f,
                logoSize,
                logoSize);

            var border = new Rect(logoRect.x - 2, logoRect.y - 2, logoRect.width + 4, logoRect.height + 4);
            EditorGUI.DrawRect(border, new Color(1, 1, 1, 0.05f));

            GUI.DrawTexture(logoRect, _basisLogo, ScaleMode.ScaleToFit, true);
        }
    }

    // Radios
    private void DrawFirstRunRadio(FirstRunKind value, string label)
    {
        var isSelected = _firstRunKind == value;
        if (GUILayout.Toggle(isSelected, label, EditorStyles.radioButton))
            _firstRunKind = value;
    }

    private void DrawPlatformRadio(PlatformChoice value, string label)
    {
        var isSelected = _choice == value;
        if (GUILayout.Toggle(isSelected, label, EditorStyles.radioButton))
            _choice = value;
    }

    // Scenes block
    private void DrawInitialSceneAndBuildSetup()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Initial Scene & Build Setup", EditorStyles.boldLabel);

            EditorGUILayout.Space(4);

            DrawSceneRow("Initialization", SCENE_INIT, ref _sceneInit, makeFirst: true);
            DrawSceneRow("Demo Scene", SCENE_DEMO, ref _sceneDemo);
            DrawSceneRow("Interactables Scene", SCENE_INTERACTABLES, ref _sceneInteractables);
        }
    }

    private void DrawSceneRow(string label, string path, ref SceneAsset cached, bool makeFirst = false)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, GUILayout.Width(130));
            EditorGUILayout.SelectableLabel(path, EditorStyles.textField, GUILayout.Height(16));

            GUI.enabled = ScenePathExists(path);
            if (GUILayout.Button("Open", GUILayout.Width(70)))
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    EditorSceneManager.OpenScene(path);
            }
            GUI.enabled = true;
        }
    }
}
#endif
