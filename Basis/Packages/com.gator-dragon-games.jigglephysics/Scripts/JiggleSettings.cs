using Unity.Mathematics;
using UnityEngine;

namespace GatorDragonGames.JigglePhysics {

/// <summary>
/// Tunables for the jiggle broad phase and collider culling. Settings come in two kinds. Startup
/// settings are captured into the job structs the first time the system simulates and cannot change
/// after that, so set them from Awake, from a bootstrap script, or any other code that runs before
/// the first jiggle rig simulates; assigning one later logs an error and leaves the value alone.
/// Live settings are re-read every frame and can be driven from a debug slider at any time.
/// </summary>
public static class JiggleSettings {
    private static bool booted;

    private static float broadPhaseCellSize = 0.5f;
    private static float inverseBroadPhaseCellSize = 1f / 0.5f;
    private static int maxColliderCellSpan = 100;
    private static int maxTreeCellSpan = 4096;
    private static int cellStalenessFrames = 3;
    private static bool cullingEnabled = true;
    private static float cullNearKeepRadius = 2.5f;
    private static float cullFrustumMargin = 0.5f;
    private static float cullFrustumExpansion = 1f;
    private static int colliderCullBatchSize = 64;

    internal static void ResetBootLatch() => booted = false;

    internal static void MarkBooted() => booted = true;

    private static bool RejectLateChange(string settingName) {
        if (!booted) {
            return false;
        }
        Debug.LogError($"JiggleSettings.{settingName} is a startup setting and was changed after the jiggle system began simulating. The change was ignored; set it before the first jiggle rig simulates.");
        return true;
    }

    /// <summary>
    /// Startup setting. Edge length in meters of one broad phase grid cell. Smaller cells mean fewer
    /// colliders tested per point, but more cells built and walked per collider and per jiggle tree.
    /// </summary>
    public static float BroadPhaseCellSize {
        get => broadPhaseCellSize;
        set {
            if (RejectLateChange(nameof(BroadPhaseCellSize))) {
                return;
            }
            broadPhaseCellSize = math.max(value, 0.01f);
            inverseBroadPhaseCellSize = 1f / broadPhaseCellSize;
        }
    }

    public static float InverseBroadPhaseCellSize => inverseBroadPhaseCellSize;

    /// <summary>
    /// Startup setting. A collider whose bounds cover more grid cells than this is placed in the
    /// global cell and tested against every jiggle point instead of being inserted per cell. Scales
    /// with the square of <see cref="BroadPhaseCellSize"/>, so halving cell size quadruples the span
    /// a given collider reports.
    /// </summary>
    public static int MaxColliderCellSpan {
        get => maxColliderCellSpan;
        set {
            if (RejectLateChange(nameof(MaxColliderCellSpan))) {
                return;
            }
            maxColliderCellSpan = math.max(value, 1);
        }
    }

    /// <summary>
    /// Startup setting. Upper bound on how many grid cells a single jiggle tree may walk while
    /// looking for colliders. Trees reporting a larger extent skip the grid walk entirely, which
    /// keeps corrupt or runaway point positions from stalling the simulation for seconds.
    /// </summary>
    public static int MaxTreeCellSpan {
        get => maxTreeCellSpan;
        set {
            if (RejectLateChange(nameof(MaxTreeCellSpan))) {
                return;
            }
            maxTreeCellSpan = math.max(value, 1);
        }
    }

    /// <summary>
    /// Startup setting. How many simulation steps an empty grid cell is kept alive before its
    /// collider buffer is freed. Higher values trade memory for fewer allocations when colliders
    /// move back and forth across a cell boundary.
    /// </summary>
    public static int CellStalenessFrames {
        get => cellStalenessFrames;
        set {
            if (RejectLateChange(nameof(CellStalenessFrames))) {
                return;
            }
            cellStalenessFrames = math.max(value, 1);
        }
    }

    /// <summary>
    /// Startup setting. Colliders per batch in the parallel cull job that feeds the broad phase.
    /// Lower values spread the work over more worker threads at the cost of more scheduling
    /// overhead.
    /// </summary>
    public static int ColliderCullBatchSize {
        get => colliderCullBatchSize;
        set {
            if (RejectLateChange(nameof(ColliderCullBatchSize))) {
                return;
            }
            colliderCullBatchSize = math.max(value, 1);
        }
    }

    /// <summary>
    /// Live setting. Master switch for collider culling. When false every collider is kept, whatever
    /// was passed to <see cref="JigglePhysics.SetCollisionCulling"/>, so culling can be turned off
    /// and back on without having to restore the individual frustum and distance flags.
    /// </summary>
    public static bool CullingEnabled {
        get => cullingEnabled;
        set => cullingEnabled = value;
    }

    /// <summary>
    /// Live setting. Colliders within this distance of a culling camera are never culled, regardless
    /// of the frustum and distance settings passed to
    /// <see cref="JigglePhysics.SetCollisionCulling"/>.
    /// </summary>
    public static float CullNearKeepRadius {
        get => cullNearKeepRadius;
        set => cullNearKeepRadius = math.max(value, 0f);
    }

    /// <summary>
    /// Live setting. Extra radius in meters added to a collider when testing it against the culling
    /// frustum. A fixed slack, so it matters most for colliders close to the camera.
    /// </summary>
    public static float CullFrustumMargin {
        get => cullFrustumMargin;
        set => cullFrustumMargin = math.max(value, 0f);
    }

    /// <summary>
    /// Live setting. Widens each culling camera's projection matrix by this factor before the
    /// frustum planes are extracted, so the kept area grows with distance instead of by a fixed
    /// amount. 1 uses the camera's own matrix, 1.2 keeps colliders 20% outside the view. Read when
    /// <see cref="JigglePhysics.SetCullingCameras"/> builds its cameras, so a change only takes
    /// effect on the next call. Works with off center and oblique projections, including VR eye
    /// matrices.
    /// </summary>
    public static float CullFrustumExpansion {
        get => cullFrustumExpansion;
        set => cullFrustumExpansion = math.max(value, 1f);
    }
}

}
