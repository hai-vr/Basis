using System.Collections.Generic;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// Whatever the camera is filming, reduced to the handful of facts every solver needs.
    /// Resolved by the caller so the solvers never reach into player or network state.
    /// </summary>
    public struct BasisCameraSubject
    {
        public bool Valid;
        public Vector3 AnchorPos;
        public Vector3 LookPoint;
        public Vector3 GroundPos;
        public Quaternion Yaw;
        public float Scale;

        /// <summary>Bounding radius used when framing. One subject is roughly shoulder width.</summary>
        public float Radius;

        /// <summary>Pre-smoothed. A raw frame delta makes look-ahead jitter.</summary>
        public Vector3 Velocity;
    }

    /// <summary>
    /// Reports how far the camera is clear from the subject outward. Returning false means nothing
    /// is in the way. Supplied by the caller so the solvers stay free of <c>Physics</c> and testable.
    /// </summary>
    public delegate bool BasisCameraOcclusionProbe(Vector3 target, Vector3 desiredCameraPos, out float freeDistanceFromTarget);

    /// <summary>
    /// Reports how far the camera may travel along its own path before it meets something solid.
    /// Returning false means the way is clear. Separate from <see cref="BasisCameraOcclusionProbe"/>
    /// because that one only ever looks along the sight line to a subject, and a camera being flown
    /// by hand or carried by a track has no sight line to look along.
    /// </summary>
    public delegate bool BasisCameraSweepProbe(Vector3 origin, Vector3 direction, float distance, float radius,
        out float freeDistance);

    public struct BasisCameraSolveContext
    {
        public BasisCameraSubject Subject;
        public float Fov;
        public float Aspect;
        public float DeltaTime;
        public float Time;

        public IReadOnlyList<Vector3> DollyPoints;
        public bool DollyLooped;

        /// <summary>The pose the solve seeds from when it has no memory of its own yet.</summary>
        public Vector3 OperatorPosition;
        public Quaternion OperatorRotation;

        public Vector3 OperatorMove;
        public float OperatorYaw, OperatorPitch;

        public BasisCameraOcclusionProbe OcclusionProbe;
        public BasisCameraSweepProbe SweepProbe;
    }

    public struct BasisCameraPose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float Fov;

        public static BasisCameraPose Lerp(in BasisCameraPose a, in BasisCameraPose b, float t)
            => new BasisCameraPose
            {
                Position = Vector3.Lerp(a.Position, b.Position, t),
                Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t),
                Fov = Mathf.Lerp(a.Fov, b.Fov, t),
            };
    }
}
