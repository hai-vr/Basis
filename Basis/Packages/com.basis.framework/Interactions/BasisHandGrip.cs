using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

namespace Basis.Scripts.BasisSdk.Interactions
{
    /// <summary>
    /// The hand frame a held object is welded to, and the units a grip offset is carried in.
    /// </summary>
    public struct BasisHandFrame
    {
        /// <summary>World position of the palm — the point an object sits on when it is in the hand.</summary>
        public Vector3 Position;

        /// <summary>
        /// The hand bone pose this frame was carried onto — the wrist, where a naive weld would put the
        /// object. Exposed so a debug view draws the offset the frame actually applied rather than
        /// re-deriving one that could disagree with it.
        /// </summary>
        public Vector3 WristPosition;

        /// <summary>World orientation of the canonical hand basis: forward down the hand, up out the back of it.</summary>
        public Quaternion Rotation;

        /// <summary>
        /// Wrist to middle knuckle, in metres. Grip offsets travel as multiples of this, so an observer
        /// showing a differently sized hand holds the object proportionally rather than at the holder's
        /// absolute reach.
        /// </summary>
        public float HandLength;

        /// <summary>
        /// False when the avatar lacked the finger bones to build the basis and the frame fell back to the
        /// wrist bone's own transform, which is rig-specific and only reconstructs against the same avatar.
        /// </summary>
        public bool Canonical;
    }

    /// <summary>
    /// Builds a hand frame from bone POSITIONS rather than from the hand bone's transform.
    ///
    /// The humanoid hand bone sits at the wrist and carries whatever bind orientation the avatar happened
    /// to be rigged with, so a grip expressed in it belongs to that one avatar: it seats objects behind the
    /// hand, and it does not survive being reconstructed against anybody else's rig. Joint positions carry
    /// no such convention — the palm, the direction down the hand and the axis across the knuckles mean the
    /// same thing on every humanoid. So a grip authored against this frame works on any avatar, and an
    /// observer showing a substitute avatar (fallback, still loading, performance level) reconstructs a held
    /// object into the hand it is actually drawing rather than into the geometry of the holder's.
    /// </summary>
    public static class BasisHandGrip
    {
        /// <summary>
        /// Stand-in wrist-to-knuckle length for an avatar with no fingers to measure, so a normalised offset
        /// still decodes to something hand-sized instead of collapsing onto the palm.
        /// </summary>
        public const float FallbackHandLength = 0.09f;

        private const float k_MinHandLength = 1e-5f;
        private const float k_MinBasisArea = 1e-8f;

        /// <summary>
        /// The local player's hand frame, anchored on the post-IK wrist (<see cref="BasisLocalBoneControl.IKWorldData"/>)
        /// so it tracks the hand the player is actually looking at rather than the target the solver aims for.
        /// False before the first solve or for an avatar with no hand bone.
        /// </summary>
        public static bool TryGetLocalFrame(BasisLocalBoneControl bone, bool left, out BasisHandFrame frame)
        {
            frame = default;
            if (bone == null)
            {
                return false;
            }
            BasisCalibratedCoords ik = bone.IKWorldData;
            if (ik.rotation.x == 0f && ik.rotation.y == 0f && ik.rotation.z == 0f && ik.rotation.w == 0f)
            {
                return false;
            }
            return TryGetFrame(BasisLocalAvatarDriver.Mapping, left, ik.position, ik.rotation, out frame);
        }

        /// <summary>
        /// Any player's hand frame off that player's own cached bone mapping — the local player's rig driver
        /// or a remote's avatar driver, both of which the avatar drivers rebuild on every avatar change.
        /// Used by both ends of a networked hold so they agree without either sending rig data.
        /// </summary>
        public static bool TryGetPlayerFrame(IBasisPlayer player, bool left, out BasisHandFrame frame)
        {
            frame = default;
            if (player == null)
            {
                return false;
            }
            BasisTransformMapping mapping = player is BasisRemotePlayer remote
                ? remote.RemoteAvatarDriver?.References
                : BasisLocalPlayer.Instance?.LocalRigDriver?.basisTransformMapping;
            Transform wrist = Wrist(mapping, left);
            if (wrist == null)
            {
                return false;
            }
            wrist.GetPositionAndRotation(out Vector3 wristPos, out Quaternion wristRot);
            return TryGetFrame(mapping, left, wristPos, wristRot, out frame);
        }

        /// <summary>
        /// Builds the frame against a supplied wrist pose. Every axis is measured in the LIVE wrist bone's
        /// local space and then carried onto that pose, so a caller passing a solved pose read a frame apart
        /// from the transforms still gets a rigid frame instead of position sheared against rotation. The
        /// bones it measures are rig-fixed: curling a finger rotates a proximal bone, it does not move its origin.
        /// </summary>
        public static bool TryGetFrame(BasisTransformMapping mapping, bool left, Vector3 wristPos, Quaternion wristRot, out BasisHandFrame frame)
        {
            frame = new BasisHandFrame
            {
                Position = wristPos,
                WristPosition = wristPos,
                Rotation = wristRot,
                HandLength = FallbackHandLength,
                Canonical = false,
            };

            Transform wrist = Wrist(mapping, left);
            if (wrist == null)
            {
                return false;
            }

            wrist.GetPositionAndRotation(out Vector3 bonePos, out Quaternion boneRot);
            Quaternion inverseBone = Quaternion.Inverse(boneRot);

            Transform knuckle = Proximal(left ? mapping.LeftMiddle : mapping.RightMiddle, left ? mapping.HasLeftMiddle : mapping.HasRightMiddle);
            if (knuckle == null)
            {
                return true;
            }

            Vector3 toKnuckle = knuckle.position - bonePos;
            float handLength = toKnuckle.magnitude;
            if (handLength < k_MinHandLength)
            {
                return true;
            }

            frame.HandLength = handLength;
            frame.Position = wristPos + wristRot * (inverseBone * (toKnuckle * 0.5f));

            Transform index = Proximal(left ? mapping.LeftIndex : mapping.RightIndex, left ? mapping.HasLeftIndex : mapping.HasRightIndex);
            Transform little = Proximal(left ? mapping.LeftLittle : mapping.RightLittle, left ? mapping.HasLeftLittle : mapping.HasRightLittle);
            if (index == null || little == null)
            {
                return true;
            }

            // Mirrored so both hands produce the same frame relative to the hand's anatomy: index sits medial
            // on the right and lateral on the left, so the raw across-the-knuckles vector points opposite ways.
            Vector3 across = index.position - little.position;
            if (left)
            {
                across = -across;
            }

            Vector3 forward = inverseBone * (toKnuckle / handLength);
            Vector3 side = inverseBone * across;
            Vector3 up = Vector3.Cross(forward, side);
            if (up.sqrMagnitude < k_MinBasisArea)
            {
                return true;
            }

            frame.Rotation = wristRot * Quaternion.LookRotation(forward, up);
            frame.Canonical = true;
            return true;
        }

        private static Transform Wrist(BasisTransformMapping mapping, bool left)
        {
            if (mapping == null)
            {
                return null;
            }
            return left ? mapping.leftHand : mapping.rightHand;
        }

        private static Transform Proximal(Transform[] finger, bool[] has)
        {
            if (finger == null || finger.Length == 0 || has == null || has.Length == 0 || !has[0])
            {
                return null;
            }
            return finger[0];
        }
    }
}
