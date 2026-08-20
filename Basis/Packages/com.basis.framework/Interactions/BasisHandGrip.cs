using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

namespace Basis.Scripts.BasisSdk.Interactions
{
    public struct BasisHandFrame
    {
        public Vector3 Position;

        public Vector3 WristPosition;

        public Quaternion Rotation;

        public float HandLength;

        public bool Canonical;
    }

    public static class BasisHandGrip
    {
        public const float FallbackHandLength = 0.09f;

        private const float k_MinHandLength = 1e-5f;
        private const float k_MinBasisArea = 1e-8f;

        public static bool TryGetLocalFrame(BasisLocalBoneControl bone, bool left, out BasisHandFrame frame)
        {
            frame = default;
            if (bone == null || !bone.HasIKWorldData)
            {
                return false;
            }
            BasisCalibratedCoords ik = bone.IKWorldData;
            return TryGetFrame(BasisLocalAvatarDriver.Mapping, left, ik.position, ik.rotation, out frame);
        }

        public static bool TryGetPlayerFrame(IBasisPlayer player, bool left, out BasisHandFrame frame)
        {
            frame = default;
            BasisTransformMapping mapping;
            switch (player)
            {
                case BasisRemotePlayer remote:
                    mapping = remote.RemoteAvatarDriver?.References;
                    break;
                case BasisLocalPlayer local:
                    // Anchor on the POST-IK pose, which is where a held object is actually welded, not on the
                    // live bone transform. Those are different poses for most of the frame: the animator graph
                    // runs in GameTime mode (BasisLocalRigDriver.EngineDrivenAnimatorEvaluate), so the engine
                    // rewrites the avatar's bones in PreLateUpdate, and the full-body solve does not land until
                    // FinishSimulate at the end of LateUpdate. Anything reading the bone in between — the
                    // networked hold's transmit among them — sees the ANIMATED arm while the object is sitting
                    // on the SOLVED one. Measuring a grip against a hand the object is not on ships an offset
                    // no observer can undo, and the holder cannot see it because it never decodes what it sent.
                    if (TryGetLocalFrame(left ? BasisLocalBoneDriver.LeftHandControl : BasisLocalBoneDriver.RightHandControl,
                            left, out frame))
                    {
                        return true;
                    }
                    // Before the first solve there is nothing published yet; the bone transform is all there is.
                    // The player we were handed, not BasisLocalPlayer.Instance — same object in practice, and
                    // this way a stale player can never resolve to the live local rig.
                    mapping = local.LocalRigDriver?.basisTransformMapping;
                    break;
                default:
                    return false;
            }
            Transform wrist = Wrist(mapping, left);
            if (wrist == null)
            {
                return false;
            }
            wrist.GetPositionAndRotation(out Vector3 wristPos, out Quaternion wristRot);
            return TryGetFrame(mapping, left, wristPos, wristRot, out frame);
        }

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
            Transform wrist = left ? mapping.leftHand : mapping.rightHand;
            return wrist != null ? wrist : null;
        }

        private static Transform Proximal(Transform[] finger, bool[] has)
        {
            if (finger == null || finger.Length == 0 || has == null || has.Length == 0 || !has[0])
            {
                return null;
            }
            // The Has flags are latched when the mapping is detected; the transform behind one can be
            // destroyed later by an avatar swap, and reading a destroyed bone's position throws.
            Transform proximal = finger[0];
            return proximal != null ? proximal : null;
        }
    }
}
