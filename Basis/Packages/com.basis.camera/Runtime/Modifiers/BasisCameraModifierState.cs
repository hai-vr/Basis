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

        /// <summary>The settled subject position, which the raw one is corrected toward.</summary>
        public Vector3 SteadyAnchor;
        public bool HasSteadyAnchor;

        /// <summary>
        /// Where the solve left the camera last frame, which is what a collision sweep travels from.
        /// Written after every solve rather than by the modifier that moved it, so it is the whole
        /// frame's movement that gets swept whichever modifier produced it.
        /// </summary>
        public Vector3 PreviousPosition;
        public bool HasPreviousPosition;

        /// <summary>
        /// Subject size to hold, as the product of distance and the tangent of the half angle. Taken
        /// from the shot as it stood when the effect was fitted, so a vertigo move starts from the
        /// framing already set up rather than from an authored number.
        /// </summary>
        public float DollyZoomReference;
        public bool HasDollyZoomReference;

        public Quaternion RigWeightRotation = Quaternion.identity;
        public Vector3 RigWeightVelocity;
        public bool HasRigWeight;

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

            PreviousPosition = position;
            HasPreviousPosition = true;
            RigWeightRotation = rotation;
            RigWeightVelocity = Vector3.zero;
            HasRigWeight = true;
            HasDollyZoomReference = false;

            ResetSubjectHistory();
            ResetSubjectSmoothing();
        }

        /// <summary>
        /// Carry the camera's own remembered poses through a rigid move of the frame they were
        /// measured in, so a solve running on an anchored camera continues from where the anchor
        /// has taken it rather than pulling back to where the world was.
        ///
        /// <para>Only the camera's poses move. <see cref="LastAnchor"/> and
        /// <see cref="SteadyAnchor"/> are subject history, and the subject is resolved fresh in
        /// world space every frame — a subject standing on the same moving thing has already
        /// travelled with it, so transporting those as well would count the move twice.</para>
        /// </summary>
        public void Transport(Vector3 fromPosition, Quaternion fromRotation, Vector3 toPosition, Quaternion toRotation)
        {
            if (!Initialized) return;

            Position = BasisCameraAnchorMath.TransportPoint(Position, fromPosition, fromRotation, toPosition, toRotation);
            Rotation = BasisCameraAnchorMath.TransportRotation(Rotation, fromRotation, toRotation);

            if (HasPreviousPosition)
            {
                PreviousPosition = BasisCameraAnchorMath.TransportPoint(
                    PreviousPosition, fromPosition, fromRotation, toPosition, toRotation);
            }

            if (HasRigWeight)
            {
                RigWeightRotation = BasisCameraAnchorMath.TransportRotation(RigWeightRotation, fromRotation, toRotation);
                RigWeightVelocity = BasisCameraAnchorMath.TransportDirection(RigWeightVelocity, fromRotation, toRotation);
            }
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
            HasPreviousPosition = false;
            HasDollyZoomReference = false;
            HasRigWeight = false;
            ResetSubjectHistory();
            ResetSubjectSmoothing();
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

        /// <summary>
        /// Forget the settled subject position. Deliberately not part of
        /// <see cref="ResetSubjectHistory"/>, which the position modifiers call every frame: the
        /// smoothing runs before they do, so clearing it there would wipe the filter each frame and
        /// leave the effect fitted and doing nothing.
        /// </summary>
        public void ResetSubjectSmoothing() => HasSteadyAnchor = false;
    }
}
