using Basis.Scripts.Networking.Sync;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

[CustomEditor(typeof(BasisSyncedObject))]
[CanEditMultipleObjects]
public class BasisSyncedObjectInspector : BasisDocInspector_UI
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();

        root.Add(BasisSyncInspectorUI.Header(
            "Basis Synced Object",
            "Code-first value sync. Declare fields in Awake (RegisterPosition / RegisterFloat / RegisterColor / ...), then LocalSet on the owner and RemoteGet on every other client."));

        var note = new HelpBox(
            "Synced fields are declared in code, so they don't show here. This panel configures the networking shared by all of them. Use BasisSyncedTransform instead if you only need to sync a transform.",
            HelpBoxMessageType.Info);
        note.style.marginBottom = 8;
        root.Add(note);

        root.Add(BasisSyncInspectorUI.NetworkingCard(serializedObject));
        root.Add(BasisSyncInspectorUI.SmoothingCard(serializedObject));

        root.Bind(serializedObject);

        root.Add(BasisSyncInspectorUI.NetworkInfo(serializedObject.targetObject));

        var api = CreateApiReferenceFoldout();
        if (api != null) root.Add(api);
        return root;
    }
}
