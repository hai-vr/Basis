using Basis.Scripts.BasisSdk.Players;
using UnityEngine;

/// <summary>
/// Drop this on any GameObject in the scene to visualize the foot driver state.
/// Shows planted/stepping feet, step arcs, body direction, velocity, knee hints.
/// Only draws in play mode when a local player with an initialized foot driver exists.
/// </summary>
[AddComponentMenu("Basis/Debug/Foot Driver Gizmos")]
public class BasisFootDriverGizmos : MonoBehaviour
{
    [Tooltip("Draw gizmos even when this object is not selected (OnDrawGizmos vs OnDrawGizmosSelected).")]
    public bool drawAlways = true;

    private void OnDrawGizmos()
    {
        if (drawAlways) Draw();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawAlways) Draw();
    }

    private void Draw()
    {
        if (!Application.isPlaying) return;
        var player = BasisLocalPlayer.Instance;
        if (player == null) return;
        var fd = player.BasisLocalFootDriver;
        if (fd == null || !fd.IsInitialized) return;
        fd.DrawGizmos();
    }
}
