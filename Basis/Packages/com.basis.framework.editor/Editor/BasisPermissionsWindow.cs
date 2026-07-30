#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Permissions, in one window.
///
/// The authored server rules and the live grant list used to be two separate windows that answered
/// two halves of the same question ("what should this user be allowed to do" / "what is this client
/// actually allowed to do"). They are tabs now, so you can author a node and immediately check
/// whether the connected client received it.
/// </summary>
public sealed class BasisPermissionsWindow : BasisTabbedEditorWindow
{
    protected override string HeaderTitle => "Permissions";

    protected override string HeaderSubtitle =>
        "Authored server rules, and what the connected client was actually granted.";

    [MenuItem("Basis/Settings/Permissions", false, 400)]
    public static void Open()
    {
        BasisPermissionsWindow w = GetWindow<BasisPermissionsWindow>();
        w.titleContent = new GUIContent("Permissions");
        w.minSize = new Vector2(920, 520);
        w.Show();
    }

    protected override BasisEditorTabPage[] BuildPages() => new BasisEditorTabPage[]
    {
        new BasisServerPermissionsTab(),
        new BasisLocalPermissionsTab(),
    };
}
#endif
