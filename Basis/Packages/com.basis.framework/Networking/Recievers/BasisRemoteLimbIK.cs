using System.Runtime.CompilerServices;
using Unity.Mathematics;

/// <summary>
/// Pure analytic two-bone IK for remote-avatar end-effector anchoring.
///
/// The remote skeleton is posed by forward kinematics off the interpolated hips, so a hand/foot at
/// the end of a chain accumulates every joint's error and drifts. When the sender marks an effector
/// "anchored" (a held controller/tracker or a planted foot) it also ships the tip's WORLD target +
/// an elbow/knee swivel; the receiver then bends that one limb so the tip lands exactly on the target.
///
/// The solve AIMS the current FK bones with minimal rotations (no avatar-specific bind-pose axes
/// needed): rotate the upper bone so the joint reaches the analytic elbow/knee position, then rotate
/// the lower bone so the tip reaches the target. Result is exact (tip == target, joint == the
/// law-of-cosines point on the swivel circle). Burst-friendly, no Unity refs beyond Unity.Mathematics.
/// </summary>
public static class BasisRemoteLimbIK
{
    /// <summary>Robust minimal rotation taking <paramref name="from"/> onto <paramref name="to"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static quaternion FromToSafe(float3 from, float3 to)
    {
        float3 f = math.normalizesafe(from, new float3(0, 0, 1));
        float3 t = math.normalizesafe(to, new float3(0, 0, 1));
        float d = math.dot(f, t);
        if (d > 0.999999f) return quaternion.identity;
        if (d < -0.999999f) return quaternion.AxisAngle(PerpAny(f), math.PI);   // antiparallel
        float3 c = math.cross(f, t);
        return math.normalize(new quaternion(c.x, c.y, c.z, 1f + d));            // half-angle form
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float3 PerpAny(float3 dir)
    {
        float3 c = math.abs(dir.y) < 0.9f ? math.cross(dir, new float3(0, 1, 0)) : math.cross(dir, new float3(1, 0, 0));
        return math.normalizesafe(c, new float3(1, 0, 0));
    }

    /// <summary>
    /// Joint (elbow/knee) world position: the law-of-cosines point at <paramref name="upperLen"/> from
    /// <paramref name="root"/> and <paramref name="lowerLen"/> from the (reach-clamped) target, placed
    /// on the swivel circle toward <paramref name="poleHint"/>.
    /// </summary>
    public static float3 SolveJoint(float3 root, float3 target, float upperLen, float lowerLen, float3 poleHint)
    {
        float3 toT = target - root;
        float d = math.length(toT);
        float dClamped = math.clamp(d, math.abs(upperLen - lowerLen) + 1e-4f, upperLen + lowerLen - 1e-4f);
        float3 dir = math.normalizesafe(toT, new float3(0, 0, 1));
        float a = (upperLen * upperLen - lowerLen * lowerLen + dClamped * dClamped) / (2f * dClamped);
        float h = math.sqrt(math.max(0f, upperLen * upperLen - a * a));
        float3 poleVec = poleHint - root;
        float3 perp = poleVec - dir * math.dot(poleVec, dir);      // pole component perpendicular to the axis
        perp = math.normalizesafe(perp, PerpAny(dir));
        return root + dir * a + perp * h;
    }

    /// <summary>
    /// Signed swivel angle of the joint around the root→tip axis, measured from a reference direction
    /// (the body-up projected perpendicular to the axis). Sender and receiver share the synced hips
    /// orientation as <paramref name="refUp"/>, so they agree on the zero. Range (-π, π].
    /// </summary>
    public static float EncodeSwivel(float3 root, float3 joint, float3 tip, float3 refUp)
    {
        float3 a = math.normalizesafe(tip - root, new float3(0, 0, 1));
        float3 r = RefPerp(a, refUp);
        float3 e = math.normalizesafe((joint - root) - a * math.dot(joint - root, a), r);
        return math.atan2(math.dot(math.cross(r, e), a), math.dot(r, e));
    }

    /// <summary>
    /// Reconstructs a pole-hint point from a swivel angle: rotate the reference perpendicular by the
    /// angle around the root→tip axis. Feed the result to <see cref="Solve"/> as poleHint.
    /// </summary>
    public static float3 PoleFromSwivel(float3 root, float3 tip, float3 refUp, float swivel)
    {
        float3 a = math.normalizesafe(tip - root, new float3(0, 0, 1));
        float3 r = RefPerp(a, refUp);
        float3 e = r * math.cos(swivel) + math.cross(a, r) * math.sin(swivel);   // r ⊥ a, so Rodrigues collapses
        return root + e;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float3 RefPerp(float3 axis, float3 refUp)
    {
        float3 r = refUp - axis * math.dot(refUp, axis);
        return math.length(r) < 1e-4f ? PerpAny(axis) : math.normalize(r);   // refUp ∥ axis → any perpendicular
    }

    /// <summary>
    /// Two-bone solve. Given the current FK world positions/rotations of the upper (root..joint) and
    /// lower (joint..tip) bones plus a tip target and pole hint, outputs the new WORLD rotations for the
    /// upper and lower bones that place the tip exactly on the target with the joint on the swivel circle.
    /// The tip's own orientation is applied separately by the caller from the sent tip rotation.
    /// </summary>
    public static void Solve(
        float3 root, float3 joint, float3 tip,
        quaternion upperRot, quaternion lowerRot,
        float3 target, float3 poleHint,
        out quaternion newUpperRot, out quaternion newLowerRot, out float3 newJoint)
    {
        float upperLen = math.distance(root, joint);
        float lowerLen = math.distance(joint, tip);
        newJoint = SolveJoint(root, target, upperLen, lowerLen, poleHint);

        // Aim the upper bone: root->joint onto root->newJoint (rigid about root; upper & lower lengths preserved).
        quaternion a1 = FromToSafe(joint - root, newJoint - root);
        newUpperRot = math.mul(a1, upperRot);

        // Where the tip lands after a1, then aim the lower bone: newJoint->tip onto newJoint->target.
        float3 tipA1 = root + math.mul(a1, tip - root);
        quaternion a2 = FromToSafe(tipA1 - newJoint, target - newJoint);
        newLowerRot = math.mul(a2, math.mul(a1, lowerRot));
    }
}
