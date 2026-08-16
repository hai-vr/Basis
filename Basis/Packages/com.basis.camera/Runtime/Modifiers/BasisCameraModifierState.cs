using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// Everything the solve has to remember between frames. Held by the camera rather than the
    /// stack so a saved stack is pure configuration and carries no runtime pose with it.
    /// </summary>
    public sealed class BasisCameraModifierState
    {
        public bool Initialized;

        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public float Fov = 40f;

        public float Heading;
        public float VerticalAxis;
        public float DollyPosition;

        /// <summary>
        /// Set when a play-through reaches the end of an open track. The camera clears the
        /// transport when it sees this — the solver only ever writes its own state, so it says
        /// the move is over rather than reaching into the stack to stop it.
        /// </summary>
        public bool DollyCompleted;

        public float OcclusionDistance;
        public bool HasOcclusionDistance;

        public Vector3 LastAnchor;
        public bool HasLastAnchor;
        public float SmoothedLateralSpeed;

        /// <summary>
        /// Continue from an explicit pose — the stack being fitted, or the camera being taken back
        /// from something else, so it eases from where it actually is rather than cutting.
        /// </summary>
        public void Seed(Vector3 position, Quaternion rotation, float fov)
        {
            Position = position;
            Rotation = rotation;
            Fov = fov;
            Initialized = true;
            HasOcclusionDistance = false;
            ResetSubjectHistory();
        }

        /// <summary>
        /// Drop everything derived so the next solve re-derives from the subject. The teleport case,
        /// and the opposite of <see cref="Seed"/>: easing from the old pose after the subject jumps
        /// a hundred metres is the sweep being avoided, not the behaviour wanted.
        /// </summary>
        public void Reseed()
        {
            Initialized = false;
            HasOcclusionDistance = false;
            DollyCompleted = false;
            ResetSubjectHistory();
        }

        /// <summary>
        /// Forget the strafe history. Carried across a change of subject the gap between the two
        /// reads as a single-frame strafe of tens of metres per second, and the camera lurches
        /// sideways before easing back out.
        /// </summary>
        public void ResetSubjectHistory()
        {
            HasLastAnchor = false;
            SmoothedLateralSpeed = 0f;
        }
    }
}
