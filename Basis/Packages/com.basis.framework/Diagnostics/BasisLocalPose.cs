using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>The local-player transforms worth caching. Kept tiny — <see cref="BasisLocalPose.NotifyWrite"/>
/// scans this set on every write that goes through the funnel, so each slot costs a reference compare.</summary>
public enum BasisPoseSlot : byte
{
    /// <summary>The player root — what the character controller moves and what localToWorldMatrix is built from.</summary>
    PlayerRoot,
    /// <summary>The avatar transform (scaled, parented under the root).</summary>
    AvatarRoot,
    Hips,
    Head,
    Count
}

/// <summary>
/// A once-per-frame snapshot of the local player's hottest transforms, so repeat reads within a frame
/// cost a struct copy instead of an ICall into native code — and, more importantly, cannot each become
/// a sync point against in-flight transform jobs.
///
/// ── Every read passes its own Transform ──
/// The slot is only a cache index; the caller always supplies the transform it wants read. That is
/// deliberate, and it is the fix for the bug this class shipped with: an earlier version bound slots up
/// front and returned a zero/identity default when a slot was unbound. `BasisLocalRigDriver.Initialize`
/// runs BEFORE `BasisTransformMapping.AutoDetectReferences` fills in the bones, so the Hips slot bound
/// null and every hips read silently returned Vector3.zero — the foot IK then solved every target from
/// the world origin. Passing the transform makes the cache incapable of answering with something other
/// than that transform's value: on a miss, or when the slot points somewhere else, it rebinds and reads
/// live. Ordering can no longer produce wrong data.
///
/// ── Validity model ──
/// Everything hangs off a single version counter. <see cref="BeginFrame"/> bumps it at the top of the
/// local player's Simulate, so a cached value can never outlive the frame that produced it. Within a
/// frame, entries are invalidated by:
///   • any write through <see cref="BasisTransformAccess"/> (automatic, via <see cref="NotifyWrite"/>),
///   • <see cref="InvalidateAll"/> at each engine-side writer that bypasses the funnel.
///
/// ── The remaining hazard ──
/// A writer that is neither of those leaves this serving stale data for the rest of the frame. The known
/// bypassers are CharacterController.Move (PhysX writes the root directly), the animator's engine stage,
/// and the IK scatter — each has an explicit InvalidateAll at its call site. The risk is a FUTURE writer
/// added without one.
///
/// That is what <see cref="ValidateHits"/> is for: with it armed, every cache hit re-reads the real
/// Transform and compares. A mismatch is reported with the reading call site, so the failure mode is
/// loud in the editor instead of silent in a build. Run with it on after touching anything that writes
/// these transforms.
/// </summary>
public static class BasisLocalPose
{
    [System.Flags]
    enum Field : byte { None = 0, Position = 1, Rotation = 2, LossyScale = 4, LocalToWorld = 8 }

    struct Entry
    {
        public Transform T;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 LossyScale;
        public Matrix4x4 LocalToWorld;
        public Field Valid;
        public uint Version;
    }

    static readonly Entry[] sEntries = new Entry[(int)BasisPoseSlot.Count];
    static uint sVersion = 1;

    /// <summary>Slots currently holding a live transform. Diagnostic only — reads bind themselves.</summary>
    public static int BoundCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < sEntries.Length; i++) if (sEntries[i].T != null) n++;
            return n;
        }
    }

    /// <summary>The transform a slot currently points at, or null.</summary>
    public static Transform Bound(BasisPoseSlot slot) => sEntries[(int)slot].T;

    /// <summary>
    /// Start a new frame's snapshot. Called at the top of BasisLocalPlayer.Simulate. This is the floor
    /// on staleness: no cached value survives it, so a missed invalidation is a within-frame bug at
    /// worst, never a persistent one.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BeginFrame() => sVersion++;

    /// <summary>
    /// Drop every cached value. Call this immediately after anything that moves one of these transforms
    /// without going through <see cref="BasisTransformAccess"/> — CharacterController.Move, the animator
    /// stage, the IK scatter, teleports, reparents.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void InvalidateAll() => sVersion++;

    /// <summary>
    /// Drop the cache for one transform if it happens to be held. Called by every setter on
    /// <see cref="BasisTransformAccess"/>; a reference compare per slot, and no-op for the vast
    /// majority of writes, which are to transforms no slot holds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void NotifyWrite(Transform t)
    {
        for (int i = 0; i < sEntries.Length; i++)
        {
            if (ReferenceEquals(sEntries[i].T, t))
            {
                sEntries[i].Valid = Field.None;
                return;
            }
        }
    }

    /// <summary>
    /// Point the slot at <paramref name="t"/> and drop anything cached for it if that is a change, or if
    /// the frame has rolled. Returns false when there is no transform to read at all.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool Prepare(ref Entry e, Transform t)
    {
        if (!ReferenceEquals(e.T, t))
        {
            e.T = t;
            e.Valid = Field.None;
            e.Version = sVersion;
        }
        else if (e.Version != sVersion)
        {
            e.Valid = Field.None;
            e.Version = sVersion;
        }
        return t != null;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>Re-read the real Transform on every cache hit and report any disagreement. Costs more
    /// than not caching at all — this is a correctness check, not something to leave on.</summary>
    public static bool ValidateHits;

    /// <summary>Cache hits served since the counters were reset.</summary>
    public static int Hits { get; private set; }
    /// <summary>Reads that had to touch the real Transform.</summary>
    public static int Misses { get; private set; }
    /// <summary>Hits that disagreed with the live Transform while <see cref="ValidateHits"/> was armed.</summary>
    public static int StaleHits { get; private set; }
    /// <summary>Where the most recent stale read was observed.</summary>
    public static string LastStaleSite { get; private set; } = string.Empty;

    static readonly System.Collections.Generic.HashSet<string> sReportedStale = new System.Collections.Generic.HashSet<string>();

    public static void ResetStats()
    {
        Hits = 0; Misses = 0; StaleHits = 0;
        LastStaleSite = string.Empty;
        sReportedStale.Clear();
    }

    static void ReportStale(string file, int line, BasisPoseSlot slot, string field, string cached, string live)
    {
        StaleHits++;
        string site = $"{file}:{line} [{slot}.{field}]";
        LastStaleSite = site;
        if (!sReportedStale.Add(site)) return;
        Debug.LogError(
            $"[BasisLocalPose] STALE CACHE at {site}\n" +
            $"  cached: {cached}\n  live:   {live}\n" +
            $"Something moved {slot} without BasisLocalPose.InvalidateAll(). Add one at that writer.");
    }
#else
    public static void ResetStats() { }
#endif

    // ── Cached reads ────────────────────────────────────────────────────────────────────────────

    public static Vector3 GetPosition(BasisPoseSlot slot, Transform t,
        [CallerFilePath] string file = null, [CallerLineNumber] int line = 0)
    {
        ref Entry e = ref sEntries[(int)slot];
        if (!Prepare(ref e, t)) return Vector3.zero;

        if ((e.Valid & Field.Position) != 0)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Hits++;
            if (ValidateHits)
            {
                Vector3 live = t.position;
                if (live != e.Position) ReportStale(file, line, slot, "Position", e.Position.ToString("F6"), live.ToString("F6"));
            }
#endif
            return e.Position;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Misses++;
        BasisTransformAudit.Record(file, line, BasisTransformOp.GetPosition);
#endif
        e.Position = t.position;
        e.Valid |= Field.Position;
        return e.Position;
    }

    public static Quaternion GetRotation(BasisPoseSlot slot, Transform t,
        [CallerFilePath] string file = null, [CallerLineNumber] int line = 0)
    {
        ref Entry e = ref sEntries[(int)slot];
        if (!Prepare(ref e, t)) return Quaternion.identity;

        if ((e.Valid & Field.Rotation) != 0)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Hits++;
            if (ValidateHits)
            {
                Quaternion live = t.rotation;
                if (live != e.Rotation) ReportStale(file, line, slot, "Rotation", e.Rotation.ToString("F6"), live.ToString("F6"));
            }
#endif
            return e.Rotation;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Misses++;
        BasisTransformAudit.Record(file, line, BasisTransformOp.GetRotation);
#endif
        e.Rotation = t.rotation;
        e.Valid |= Field.Rotation;
        return e.Rotation;
    }

    /// <summary>
    /// Both halves of the pose. When neither is cached this is one native round trip via
    /// GetPositionAndRotation rather than two — worth preferring at call sites that want both.
    /// </summary>
    public static void GetPose(BasisPoseSlot slot, Transform t, out Vector3 position, out Quaternion rotation,
        [CallerFilePath] string file = null, [CallerLineNumber] int line = 0)
    {
        ref Entry e = ref sEntries[(int)slot];
        if (!Prepare(ref e, t)) { position = Vector3.zero; rotation = Quaternion.identity; return; }

        const Field both = Field.Position | Field.Rotation;
        if ((e.Valid & both) == both)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Hits++;
            if (ValidateHits)
            {
                t.GetPositionAndRotation(out Vector3 lp, out Quaternion lr);
                if (lp != e.Position) ReportStale(file, line, slot, "Position", e.Position.ToString("F6"), lp.ToString("F6"));
                if (lr != e.Rotation) ReportStale(file, line, slot, "Rotation", e.Rotation.ToString("F6"), lr.ToString("F6"));
            }
#endif
            position = e.Position;
            rotation = e.Rotation;
            return;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Misses++;
        BasisTransformAudit.Record(file, line, BasisTransformOp.GetPose);
#endif
        t.GetPositionAndRotation(out e.Position, out e.Rotation);
        e.Valid |= both;
        position = e.Position;
        rotation = e.Rotation;
    }

    /// <summary>lossyScale walks every ancestor natively, so this is the most valuable one to cache.</summary>
    public static Vector3 GetLossyScale(BasisPoseSlot slot, Transform t,
        [CallerFilePath] string file = null, [CallerLineNumber] int line = 0)
    {
        ref Entry e = ref sEntries[(int)slot];
        if (!Prepare(ref e, t)) return Vector3.one;

        if ((e.Valid & Field.LossyScale) != 0)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Hits++;
            if (ValidateHits)
            {
                Vector3 live = t.lossyScale;
                if (live != e.LossyScale) ReportStale(file, line, slot, "LossyScale", e.LossyScale.ToString("F6"), live.ToString("F6"));
            }
#endif
            return e.LossyScale;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Misses++;
        BasisTransformAudit.Record(file, line, BasisTransformOp.GetLossyScale);
#endif
        e.LossyScale = t.lossyScale;
        e.Valid |= Field.LossyScale;
        return e.LossyScale;
    }

    public static Matrix4x4 GetLocalToWorld(BasisPoseSlot slot, Transform t,
        [CallerFilePath] string file = null, [CallerLineNumber] int line = 0)
    {
        ref Entry e = ref sEntries[(int)slot];
        if (!Prepare(ref e, t)) return Matrix4x4.identity;

        if ((e.Valid & Field.LocalToWorld) != 0)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Hits++;
            if (ValidateHits)
            {
                Matrix4x4 live = t.localToWorldMatrix;
                if (live != e.LocalToWorld) ReportStale(file, line, slot, "LocalToWorld", e.LocalToWorld.ToString(), live.ToString());
            }
#endif
            return e.LocalToWorld;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Misses++;
        BasisTransformAudit.Record(file, line, BasisTransformOp.GetLocalToWorld);
#endif
        e.LocalToWorld = t.localToWorldMatrix;
        e.Valid |= Field.LocalToWorld;
        return e.LocalToWorld;
    }
}
