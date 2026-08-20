using UnityEngine;

/// <summary>
/// The rigid move that carries a camera pose from where its anchor was last frame to where the
/// anchor is now.
///
/// <para>Every pose the camera remembers — the operator's, the published one, and the solver's
/// history — is world space. An anchor that moves therefore cannot be applied by handing the pin
/// constraint a different source: that would recompose a world pose through a transform it is
/// already expressed in, which is the shape of the bug the playspace pin carried. Instead the
/// remembered poses are transported, and the constraint keeps the identity source it uses for a
/// world pin. Everything downstream — fly, the modifier stack, shake, the gizmos — keeps working in
/// world space and needs to know nothing about anchoring.</para>
/// </summary>
public static class BasisCameraAnchorMath
{
    /// <summary>The rotation taking <paramref name="from"/> onto <paramref name="to"/>.</summary>
    public static Quaternion Delta(Quaternion from, Quaternion to) => to * Quaternion.Inverse(from);

    /// <summary>A world point, carried through the anchor's move.</summary>
    public static Vector3 TransportPoint(
        Vector3 point, Vector3 fromPosition, Quaternion fromRotation, Vector3 toPosition, Quaternion toRotation)
        => toPosition + Delta(fromRotation, toRotation) * (point - fromPosition);

    /// <summary>A world direction or velocity, which turns with the anchor but does not translate.</summary>
    public static Vector3 TransportDirection(Vector3 direction, Quaternion fromRotation, Quaternion toRotation)
        => Delta(fromRotation, toRotation) * direction;

    /// <summary>A world rotation, carried through the anchor's move.</summary>
    public static Quaternion TransportRotation(Quaternion rotation, Quaternion fromRotation, Quaternion toRotation)
        => Delta(fromRotation, toRotation) * rotation;

    /// <summary>
    /// A heading in degrees about world up, carried through the anchor's move.
    ///
    /// <para>Only the yaw of the move applies: an orbit heading is an angle around its subject, and
    /// a boat that pitches over a wave has not swung the shot around the person on it.</para>
    /// </summary>
    public static float TransportHeading(float headingDegrees, Quaternion fromRotation, Quaternion toRotation)
        => headingDegrees + Mathf.DeltaAngle(YawDegrees(fromRotation), YawDegrees(toRotation));

    /// <summary>Square of the flattened forward below which it no longer names a heading: about 8° off vertical.</summary>
    private const float VerticalForwardEpsilon = 0.02f;

    /// <summary>
    /// The heading of a rotation about world up, continuous through vertical.
    ///
    /// <para>Flattened forward, falling back to the up axis only once forward is genuinely near
    /// vertical — where a plain projection reverses by 180°, which an anchor on a lift or rolling
    /// through a swell does reach.</para>
    ///
    /// <para>Deliberately NOT the rule <c>FlattenToYaw</c> uses on a subject, which takes whichever
    /// of the two axes projects longer. That is right for a head or a body, whose roll stays near
    /// zero, so the swap only ever happens near vertical. An anchor rolls freely: at 45° of pitch
    /// and 20° of roll the up axis already projects longer, and taking it there reported 27° of yaw
    /// on a frame that had turned by none — a boat riding a wave would have swung the operator's
    /// heading round with it.</para>
    /// </summary>
    public static float YawDegrees(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        Vector3 flat = new Vector3(forward.x, 0f, forward.z);

        if (flat.sqrMagnitude < VerticalForwardEpsilon)
        {
            // Past vertical the frame is upside down relative to its heading, so the flattened up
            // axis points backwards along it; the sign of forward's tilt is which side it is on.
            Vector3 up = rotation * Vector3.up;
            flat = new Vector3(up.x, 0f, up.z) * (forward.y > 0f ? -1f : 1f);
        }

        return flat.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// Whether an anchor has moved far enough to be worth carrying anything through.
    ///
    /// <para>An anchor that is standing still still reports micrometre jitter off its own solve, and
    /// transporting on it would fold that jitter into the camera's remembered poses every frame.</para>
    /// </summary>
    public static bool HasMoved(
        Vector3 fromPosition, Quaternion fromRotation, Vector3 toPosition, Quaternion toRotation)
        => (toPosition - fromPosition).sqrMagnitude > 1e-10f || Quaternion.Angle(fromRotation, toRotation) > 1e-4f;
}
