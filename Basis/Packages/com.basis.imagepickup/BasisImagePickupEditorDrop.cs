#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Basis.ImagePickup
{
    /// <summary>
    /// Editor play-mode drag-and-drop bridge. The Windows player intercepts OS file drops with a window hook,
    /// which cannot run in the editor, so in play mode drop a PNG, JPEG, or GIF onto the Scene view to spawn it.
    /// </summary>
    [InitializeOnLoad]
    public static class BasisImagePickupEditorDrop
    {
        private const BasisDebug.LogTag LogTag = BasisDebug.LogTag.Pickups;

        static BasisImagePickupEditorDrop()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        private static void OnSceneGui(SceneView view)
        {
            if (!Application.isPlaying) return;

            Event e = Event.current;
            if (e == null) return;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            if (!DragContainsSupportedImage()) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type != EventType.DragPerform) return;

            DragAndDrop.AcceptDrag();
            e.Use();

            BasisImagePickupManager manager = BasisImagePickupManager.Instance;
            if (manager == null)
            {
                BasisDebug.LogWarning("Image pickup: manager not ready.", LogTag);
                return;
            }

            manager.SpawnFromFiles(DragAndDrop.paths);
        }

        private static bool DragContainsSupportedImage()
        {
            string[] paths = DragAndDrop.paths;
            if (paths == null) return false;
            foreach (string path in paths)
            {
                if (BasisImageSecurity.HasSupportedImageExtension(path)) return true;
            }
            return false;
        }
    }
}
#endif
