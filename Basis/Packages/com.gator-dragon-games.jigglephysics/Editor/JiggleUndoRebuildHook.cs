#if UNITY_EDITOR
using UnityEditor;

namespace GatorDragonGames.JigglePhysics {

/// <summary>
/// Undo/redo reverts bone transforms but not the simulation's cached point state, which would
/// otherwise keep posing bones from the pre-undo state until the sim converges. Rebuilding the
/// trees reseeds the verlet state at the post-undo pose.
/// </summary>
[InitializeOnLoad]
public static class JiggleUndoRebuildHook {
    static JiggleUndoRebuildHook() {
        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        Undo.undoRedoPerformed += OnUndoRedoPerformed;
    }

    private static void OnUndoRedoPerformed() {
        if (!EditorApplication.isPlaying) {
            return;
        }
        JigglePhysics.SetAllTreesDirty();
    }
}

}
#endif
