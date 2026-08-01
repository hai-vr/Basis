using Basis.Scripts.Platform;
using UnityEditor;
using UnityEngine;

namespace Basis.Editor
{
    /// <summary>
    /// Editor play-mode stand-in for the Windows window hook, which cannot run inside the editor.
    /// Dropping a file onto the Scene view in play mode feeds it to
    /// <see cref="BasisDesktopFileDrop"/>, so image pickups and BEE library adds can be tested
    /// without making a build.
    ///
    /// The Scene view is also where Unity's own asset drags land, so the drag is only consumed when
    /// it carries a file some subscriber has claimed — dragging a prefab in still drops a prefab.
    /// </summary>
    [InitializeOnLoad]
    public static class BasisDesktopFileDropEditorBridge
    {
        static BasisDesktopFileDropEditorBridge()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        private static void OnSceneGui(SceneView view)
        {
            if (!Application.isPlaying) return;

            Event current = Event.current;
            if (current == null) return;
            if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform) return;
            if (!DragContainsAcceptedFile()) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (current.type != EventType.DragPerform) return;

            DragAndDrop.AcceptDrag();
            current.Use();

            BasisDesktopFileDrop.SubmitDroppedFiles(DragAndDrop.paths);
        }

        private static bool DragContainsAcceptedFile()
        {
            string[] paths = DragAndDrop.paths;
            if (paths == null) return false;

            for (int i = 0; i < paths.Length; i++)
            {
                if (BasisDesktopFileDrop.IsAcceptedFile(paths[i])) return true;
            }
            return false;
        }
    }
}
