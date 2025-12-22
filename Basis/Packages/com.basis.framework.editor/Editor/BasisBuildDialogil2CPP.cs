using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public class BasisBuildDialogil2CPP : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    // Platforms that effectively require IL2CPP (commonly true in modern Unity).
    // You can tweak this list for your project/Unity version.
    private static readonly HashSet<BuildTarget> Il2CppOnlyTargets = new HashSet<BuildTarget>
    {
        BuildTarget.Android,
        BuildTarget.iOS,
        BuildTarget.tvOS,
        BuildTarget.WebGL,
        // Consoles (these defines exist only if you have the platform module installed)
#if UNITY_2019_1_OR_NEWER
        BuildTarget.PS4,
        BuildTarget.XboxOne,
        BuildTarget.Switch,
#endif
    };

    // Platforms you want to force Mono (example: your Linux choice).
    // Note: Windows/macOS/Linux Standalone can do IL2CPP too (depending on Unity version),
    // so only put them here if you *want* Mono on purpose.
    private static readonly HashSet<BuildTarget> MonoOnlyTargets = new HashSet<BuildTarget>
    {
        BuildTarget.StandaloneLinux64,BuildTarget.LinuxHeadlessSimulation,
    };

    public void OnPreprocessBuild(BuildReport report)
    {
        var namedBuildTarget =
            UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(report.summary.platformGroup);

        var currentBackend = PlayerSettings.GetScriptingBackend(namedBuildTarget);
        var target = report.summary.platform;

        // 1) Force IL2CPP-only targets
        if (Il2CppOnlyTargets.Contains(target))
        {
            SetBackendIfNeeded(namedBuildTarget, currentBackend, ScriptingImplementation.IL2CPP);
            return;
        }

        // 2) Force Mono-only targets
        if (MonoOnlyTargets.Contains(target))
        {
            SetBackendIfNeeded(namedBuildTarget, currentBackend, ScriptingImplementation.Mono2x);
            return;
        }

        // 3) Ask for everything else
        bool useIl2Cpp = EditorUtility.DisplayDialog(
            "Scripting Backend",
            $"Build target: {target}\n\nUse IL2CPP for this build?",
            "Yes (IL2CPP)",
            "No (Mono)"
        );

        SetBackendIfNeeded(
            namedBuildTarget,
            currentBackend,
            useIl2Cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x
        );
    }

    private static void SetBackendIfNeeded(
        UnityEditor.Build.NamedBuildTarget namedBuildTarget,
        ScriptingImplementation current,
        ScriptingImplementation desired)
    {
        if (current == desired) return;
        PlayerSettings.SetScriptingBackend(namedBuildTarget, desired);
    }
}
