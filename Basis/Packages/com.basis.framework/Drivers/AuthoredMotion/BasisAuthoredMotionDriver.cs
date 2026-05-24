using Unity.Jobs;
using UnityEngine;

/// <summary>
/// Per-frame pump for <see cref="BasisAuthoredMotionSystem"/>. A single hidden, persistent object
/// schedules and completes the batched authored-motion pass each frame. The negative execution
/// order makes its <c>LateUpdate</c> run before <c>JiggleUpdateExample</c> (default order 0), so
/// the driven transforms are written and the job is completed before jiggle physics samples them
/// — authored motion is the animated base, jiggle layers on top.
/// </summary>
[DefaultExecutionOrder(-100)]
public class BasisAuthoredMotionDriver : MonoBehaviour
{
    static BasisAuthoredMotionDriver sInstance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (sInstance != null) return;
        var go = new GameObject("[BasisAuthoredMotionDriver]") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        sInstance = go.AddComponent<BasisAuthoredMotionDriver>();
    }

    private void LateUpdate()
    {
        // Complete here (not just schedule) so the writes land before the jiggle updater's
        // LateUpdate samples the bones. TODO (perf): split to schedule-early / complete-here once
        // the kinds are validated, to overlap the job with other main-thread work.
        JobHandle handle = BasisAuthoredMotionSystem.Schedule();
        BasisAuthoredMotionSystem.Complete(handle);
    }

    private void OnDestroy()
    {
        if (sInstance != this) return;
        BasisAuthoredMotionSystem.Dispose();
        sInstance = null;
    }
}
