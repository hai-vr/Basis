using UnityEngine;

/// <summary>
/// Shared, read-only baked motion for a <see cref="BasisAuthoredMotion.Movement"/> of kind
/// <c>Sequence</c>: an AnimationClip's curves sampled to a fixed-rate, blittable buffer so the
/// batched Burst job can interpolate without touching a managed <c>AnimationCurve</c>. One asset
/// is referenced by every instance of an avatar; per-instance runtime state is just a playhead.
/// Rotations bake as absolute local rotations and the job writes them straight to the bone, so a
/// converted clip reproduces its authored pose exactly regardless of the avatar's rest pose.
/// </summary>
[CreateAssetMenu(fileName = "BasisMotionClip", menuName = "Basis/Authored Motion Clip")]
public class BasisMotionClip : ScriptableObject
{
    [Tooltip("Samples per second the curves were baked at.")]
    public float frameRate = 30f;

    [Tooltip("Number of frames per driven transform.")]
    public int frameCount;

    [Tooltip("Number of driven transforms this clip covers.")]
    public int transformCount;

    [Tooltip("Transform path each row drives, relative to the sequence root. One per transform.")]
    public string[] paths;

    // Flattened samples, laid out [transform0_frame0..frameN-1, transform1_...].
    // Index a (transform, frame) as transformIndex * frameCount + frame.
    public Vector4[] rotationSamples;   // absolute local rotation, xyzw
    public Vector3[] positionSamples;   // reserved (unused in v1)
    public Vector3[] scaleSamples;      // reserved (unused in v1)

    /// <summary>Clip length in seconds (<c>frameCount / frameRate</c>).</summary>
    public float Length => frameRate > 0f ? frameCount / frameRate : 0f;
}
