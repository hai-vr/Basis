using System;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>How a shot decides where the camera sits.</summary>
    public enum BasisCameraBodyMode
    {
        /// <summary>Hold an offset from the subject. The camera's original follow behaviour.</summary>
        Transposer = 0,
        /// <summary>Transposer that also dollies in or out to hold the subject at a chosen size in frame.</summary>
        Framing = 1,
        /// <summary>Ride a ring around the subject, sweeping between a low, waist and high rig.</summary>
        Orbital = 2,
        /// <summary>Ride the dolly track built from placed waypoints.</summary>
        Dolly = 3,
        /// <summary>Stay where it was put. Aim still solves, so this is a locked-off tripod.</summary>
        HardLock = 4,
    }

    /// <summary>How a shot decides where the camera points.</summary>
    public enum BasisCameraAimMode
    {
        /// <summary>Screen composition with a dead zone and a soft zone.</summary>
        Composer = 0,
        /// <summary>Subject dead centre every frame.</summary>
        HardLookAt = 1,
        /// <summary>Free look driven by the operator, ignoring the subject.</summary>
        Manual = 2,
        /// <summary>Copy the subject's own facing, for over-the-shoulder shots.</summary>
        MatchSubject = 3,
        /// <summary>Keep whatever rotation the body solve produced.</summary>
        None = 4,
    }

    /// <summary>Which frame a positional offset is authored in.</summary>
    public enum BasisCameraBindingMode
    {
        /// <summary>Offset turns with the subject, so it stays in front as they turn.</summary>
        SubjectYaw = 0,
        /// <summary>Offset is fixed to world axes; the subject can turn without moving the camera.</summary>
        WorldSpace = 1,
        /// <summary>Hold the distance but keep the current heading, so the camera never swings around behind.</summary>
        SimpleFollow = 2,
    }

    public enum BasisCameraBlendStyle
    {
        Cut = 0,
        Linear = 1,
        EaseInOut = 2,
        EaseIn = 3,
        EaseOut = 4,
        HardIn = 5,
        HardOut = 6,
    }

    /// <summary>
    /// One authored camera setup — a body rule, an aim rule, a lens and a noise character. The
    /// director keeps a list of these and blends between whichever is live, which is what makes a
    /// single physical camera behave like a rig of virtual ones.
    /// </summary>
    [Serializable]
    public class BasisCameraShot
    {
        /// <summary>Stable across reordering, so runtime solver state survives a list shuffle.</summary>
        public int id;

        public string name = "Shot";
        public bool enabled = true;

        [Tooltip("Highest enabled priority is live, unless a shot is picked explicitly.")]
        public int priority;

        public BasisCameraBodyMode bodyMode = BasisCameraBodyMode.Transposer;
        public BasisCameraAimMode aimMode = BasisCameraAimMode.Composer;
        public BasisCameraBindingMode bindingMode = BasisCameraBindingMode.SubjectYaw;

        [Tooltip("Offset from the subject in the binding frame, in metres at default avatar scale.")]
        public Vector3 positionOffset = new Vector3(0.5f, 0f, 1.4f);

        [Tooltip("Seconds to catch up per axis of the binding frame: X sideways, Y vertical, Z on approach.")]
        public Vector3 positionDamping = new Vector3(0.35f, 0.5f, 0.5f);

        [Tooltip("Seconds to catch up per rotation axis: X pitch, Y yaw, Z roll.")]
        public Vector3 rotationDamping = new Vector3(0.4f, 0.35f, 0.8f);

        public BasisComposerSettings composer = BasisComposerSettings.Default;
        public BasisCameraOrbitSettings orbit = BasisCameraOrbitSettings.Default;
        public BasisCameraNoiseSettings noise = BasisCameraNoiseSettings.Default;

        [Tooltip("Extra rotation applied after aiming, in degrees.")]
        public Vector3 rotationOffset = Vector3.zero;

        public bool overrideLens;
        [Range(5f, 120f)] public float lensFov = 40f;

        [Tooltip("Share of the frame the subject should fill in Framing mode.")]
        [Range(0.05f, 1f)] public float framingScreenFraction = 0.35f;
        public float framingMinDistance = 0.6f;
        public float framingMaxDistance = 12f;
        [Tooltip("Hold subject size by changing focal length instead of dollying.")]
        public bool framingUsesZoom;

        [Tooltip("Seconds of subject motion to lead by, so a moving subject is not dragged behind.")]
        public float lookAheadTime;
        public float lookAheadLimit = 2f;

        [Tooltip("Where on the dolly track this shot sits, in waypoints.")]
        public float dollyPosition;
        [Tooltip("Slide along the track to whichever point is nearest the subject.")]
        public bool dollyAutoTrack = true;
        [Tooltip("Seconds for the dolly to catch up to its target point on the track.")]
        public float dollyDamping = 0.5f;
        [Tooltip("Metres per second the shot travels along the track when not auto-tracking.")]
        public float dollySpeed;
        [Tooltip("Offset from the track, in the track's own frame.")]
        public Vector3 dollyOffset = Vector3.zero;

        public bool avoidOcclusion;
        public float occlusionPadding = 0.25f;
        public float occlusionMinDistance = 0.4f;
        [Tooltip("Seconds to ease back out once the shot is clear again. Pull-in is always instant.")]
        public float occlusionReturnDamping = 0.6f;

        [Tooltip("Seconds to blend into this shot when it becomes live.")]
        public float blendTime = 1f;
        public BasisCameraBlendStyle blendStyle = BasisCameraBlendStyle.EaseInOut;

        /// <summary>
        /// True when this shot sets the lens itself. The camera only writes field of view back when
        /// this holds, so an ordinary shot leaves the operator's FOV slider alone.
        /// </summary>
        public bool DrivesLens => overrideLens || (bodyMode == BasisCameraBodyMode.Framing && framingUsesZoom);

        public BasisCameraShot Clone()
            => (BasisCameraShot)MemberwiseClone();
    }

    public static class BasisCameraBlend
    {
        /// <summary>Shapes a 0..1 blend by its style. Cut jumps at the end so a zero-length blend still lands.</summary>
        public static float Evaluate(BasisCameraBlendStyle style, float t)
        {
            t = Mathf.Clamp01(t);
            switch (style)
            {
                case BasisCameraBlendStyle.Cut:
                    return t >= 1f ? 1f : 0f;
                case BasisCameraBlendStyle.Linear:
                    return t;
                case BasisCameraBlendStyle.EaseIn:
                    return 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
                case BasisCameraBlendStyle.EaseOut:
                    return Mathf.Sin(t * Mathf.PI * 0.5f);
                case BasisCameraBlendStyle.HardIn:
                    return t * t * t;
                case BasisCameraBlendStyle.HardOut:
                    float inverse = 1f - t;
                    return 1f - inverse * inverse * inverse;
                case BasisCameraBlendStyle.EaseInOut:
                default:
                    return t * t * (3f - 2f * t);
            }
        }
    }
}
