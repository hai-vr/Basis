using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Device_Management;
using Basis.Scripts.TransformBinders.BoneControl;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Playables;

namespace Basis.Scripts.Drivers
{
    /// <summary>
    /// Local rig driver that wires up Unity Animation Rigging constraints for a player avatar,
    /// filters tracker noise (One Euro Filter), and manually evaluates the rig graph each frame.
    /// Sets up spine, head, hands, feet, and toes, and toggles layers based on available rigs.
    /// </summary>
    [Serializable]
    public class BasisLocalRigDriver
    {
        [Header("Smoothing (One Euro Filter)")]
        [Tooltip("Lower = more smoothing; Higher = more responsive.")]
        [Range(0.01f, 10f)]
        public float MinCutoff = 5.5f;

        [Tooltip("How much to raise cutoff when motion is fast (reduces lag during quick moves).")]
        [Range(0f, 10f)]
        public float Beta = 3.25f;

        [Tooltip("Cutoff for derivative smoothing.")]
        [Range(0.01f, 10f)]
        public float DerivativeCutoff = 3f;

        public RigBuilder Builder;
        public List<RigTransform> AdditionalTransforms = new List<RigTransform>();
        public PlayableGraph PlayableGraph;

        private BasisLocalPlayer localPlayer;
        private BasisTransformMapping references;

        private readonly Dictionary<BasisBoneTrackedRole, OneEuroFilterVector3> posFilters = new();
        private float _timeAccumulator;

        public HumanBodyBones[] HumanBones;
        public bool[] isActive;

        public Rig MainRig;
        public RigLayer RigLayer;
        public BasisFullBodyIK BasisFullIKConstraint;

        private BasisConstraintSlotIndex[] _boneToSlot; // size = (int)HumanBodyBones.LastBone

        // ------------------------------------------
        // Filters
        // ------------------------------------------
        private OneEuroFilterVector3 GetPosFilter(BasisBoneTrackedRole role)
        {
            if (!posFilters.TryGetValue(role, out var f))
            {
                f = new OneEuroFilterVector3(MinCutoff, Beta, DerivativeCutoff);
                posFilters[role] = f;
            }
            else
            {
                // keep runtime params in sync
                f.minCutoff = MinCutoff;
                f.beta = Beta;
                f.dCutoff = DerivativeCutoff;
            }
            return f;
        }

        // ------------------------------------------
        // Lifecycle
        // ------------------------------------------
        public void Initialize(BasisLocalPlayer localPlayer, BasisTransformMapping references)
        {
            this.localPlayer = localPlayer;
            this.references = references;
            _timeAccumulator = 0f;
        }

        public void BuildBuilder()
        {
            if (localPlayer?.BasisAvatar?.Animator == null || Builder == null)
                return;

            PlayableGraph = localPlayer.BasisAvatar.Animator.playableGraph;
            PlayableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            Builder.Build(PlayableGraph);
        }

        public void OnTPose() => OnTPose(BasisLocalAvatarDriver.CurrentlyTposing);

        public void OnTPose(bool currentlyTposing)
        {
            if (Builder == null) return;

            if (currentlyTposing)
            {
                foreach (var layer in Builder.layers) layer.active = false;
                return;
            }

            // Notify controls when exiting T-pose
            var driver = BasisLocalPlayer.Instance?.LocalBoneDriver;
            if (driver?.Controls == null) return;

            foreach (var control in driver.Controls)
            {
                control?.OnHasRigChanged?.Invoke();
            }
        }

        public void CleanupBeforeContinue()
        {
            if (MainRig != null)
            {
                GameObject.Destroy(MainRig.gameObject);
                MainRig = null;
                RigLayer = default;
            }
        }

        // ------------------------------------------
        // Per-frame simulation
        // ------------------------------------------
        public void SimulateIKDestinations(float deltaTime)
        {
            if (BasisFullIKConstraint == null) return;

            _timeAccumulator += Mathf.Max(deltaTime, 1e-6f);
            var Hips = BasisLocalBoneDriver.HipsControl;
            // Hips (filtered)
            var hipsCoords = Hips.OutgoingWorldData;
            var hipsPos = GetPosFilter(BasisBoneTrackedRole.Hips).Filter(hipsCoords.position, _timeAccumulator);

            var d = BasisFullIKConstraint.data;
            d.TargetPositionHips = hipsPos;
            d.TargetRotationEulerHips = hipsCoords.rotation;

            // Global hint direction (knee/neck)
            d.m_HintDirection = Hips.OutgoingWorldData.rotation * Vector3.right;

            // Head
            var data = BasisLocalBoneDriver.HeadControl.OutgoingWorldData;
            d.TargetPositionHead = data.position;
            d.TargetRotationHead = data.rotation;

            // Feet
            data = BasisLocalBoneDriver.LeftFootControl.OutgoingWorldData;
            d.LeftFootTargetPosition = data.position;
            d.LeftFootTargetRotation = data.rotation;

            data = BasisLocalBoneDriver.RightFootControl.OutgoingWorldData;
            d.RightFootTargetPosition = data.position;
            d.RightFootTargetRotation = data.rotation;

            // Chest (as head hint)
            data = BasisLocalBoneDriver.ChestControl.OutgoingWorldData;
            d.HintPositionHead = data.position;
            d.HintRotationHead = data.rotation;

            // Leg hints
            data = BasisLocalBoneDriver.LeftLowerLegControl.OutgoingWorldData;
            d.HintPositionLeftLowerLeg = data.position;
            d.HintRotationLeftLowerLeg = data.rotation;

            data = BasisLocalBoneDriver.RightLowerLegControl.OutgoingWorldData;
            d.HintPositionRightLowerLeg = data.position;
            d.HintRotationRightLowerLeg = data.rotation;

            // Hands (targets)
            BasisAnimationRiggingHelper.SetHandCollisionScale(BasisFullIKConstraint, localPlayer.CurrentHeight.SelectedAvatarToAvatarDefaultScale);

            var leftHand = BasisLocalBoneDriver.LeftHandControl.OutgoingWorldData;
            d.TargetPositionLeftHand = leftHand.position;
            d.TargetRotationLeftHand = leftHand.rotation;

            var rightHand = BasisLocalBoneDriver.RightHandControl.OutgoingWorldData;
            d.TargetPositionRightHand = rightHand.position;
            d.TargetRotationRightHand = rightHand.rotation;

            // Hand hints (forearms)
            var leftLowerArm = BasisLocalBoneDriver.LeftLowerArmControl.OutgoingWorldData;
            d.HintPositionLeftHand = leftLowerArm.position;
            d.HintRotationLeftHand = leftLowerArm.rotation;

            var rightLowerArm = BasisLocalBoneDriver.RightLowerArmControl.OutgoingWorldData;
            d.HintPositionRightHand = rightLowerArm.position;
            d.HintRotationRightHand = rightLowerArm.rotation;

            // Toes (pass-through outgoing data)
            var outRightToe = BasisLocalBoneDriver.RightToeControl.OutgoingWorldData;
            d.OutGoingRightToePosition = outRightToe.position;
            d.OutGoingRightToeRotation = outRightToe.rotation;

            var outLeftToe = BasisLocalBoneDriver.LeftToeControl.OutgoingWorldData;
            d.OutGoingLeftToePosition = outLeftToe.position;
            d.OutGoingLeftToeRotation = outLeftToe.rotation;

            BasisFullIKConstraint.data = d;

            // Manual evaluation
            if (Builder != null)
            {
                Builder.SyncLayers();
                PlayableGraph.Evaluate(deltaTime);
            }
        }

        // ------------------------------------------
        // Rig setup
        // ------------------------------------------
        public void SetBodySettings(BasisLocalBoneDriver driver)
        {
            var rigGO = CreateOrGetRig("Main IK", true, out MainRig, out RigLayer);

            // Spine chain selection
            ChooseSpine(out var root0, out var mid0, out var tip0);

            // Build arrays for legs and head chain
            var roots = new[] { root0, references.LeftUpperLeg, references.RightUpperLeg };
            var middles = new[] { mid0, references.LeftLowerLeg, references.RightLowerLeg };
            var tips = new[] { tip0, references.leftFoot, references.rightFoot };

            var roles = new[]
            {
                BasisBoneTrackedRole.Head,
                BasisBoneTrackedRole.LeftFoot,
                BasisBoneTrackedRole.RightFoot
            };

            var hintRoles = new[]
            {
                BasisBoneTrackedRole.Chest,
                BasisBoneTrackedRole.LeftLowerLeg,
                BasisBoneTrackedRole.RightLowerLeg
            };

            BasisAnimationRiggingHelper.CreateMainIKRIG(
                localPlayer,
                rigGO,
                roots, middles, tips, roles, hintRoles, out BasisFullIKConstraint,
                references.Hips, BasisBoneTrackedRole.Hips,
                references.leftToes, references.rightToes,
                references.chest, references.neck,
                references.leftUpperArm, references.leftLowerArm, references.leftHand,
                references.RightUpperArm, references.RightLowerArm, references.rightHand
            );

            // Base enables
            var d = BasisFullIKConstraint.data;
            d.enabledHead = true;
            d.enabledHips = true;

            // Legs enabled by presence
            if (driver.FindBone(out BasisLocalBoneControl leftFoot, BasisBoneTrackedRole.LeftFoot))
            {
                leftFoot.OnHasRigChanged += () =>
                {
                    var dd = BasisFullIKConstraint.data;
                    dd.EnableLeftLeg = leftFoot.HasRigLayer == BasisHasRigLayer.HasRigLayer;
                    BasisFullIKConstraint.data = dd;
                };
                d.EnableLeftLeg = leftFoot.HasRigLayer == BasisHasRigLayer.HasRigLayer;
            }

            if (driver.FindBone(out BasisLocalBoneControl rightFoot, BasisBoneTrackedRole.RightFoot))
            {
                rightFoot.OnHasRigChanged += () =>
                {
                    var dd = BasisFullIKConstraint.data;
                    dd.EnableRightLeg = rightFoot.HasRigLayer == BasisHasRigLayer.HasRigLayer;
                    BasisFullIKConstraint.data = dd;
                };
                d.EnableRightLeg = rightFoot.HasRigLayer == BasisHasRigLayer.HasRigLayer;
            }

            // Head-driven layer activity
            if (driver.FindBone(out BasisLocalBoneControl head, BasisBoneTrackedRole.Head))
            {
                head.OnHasRigChanged += () =>
                {
                    RigLayer.active = head.HasRigLayer == BasisHasRigLayer.HasRigLayer;
                };
                RigLayer.active = head.HasRigLayer == BasisHasRigLayer.HasRigLayer;
            }

            // Toes (fixed: left controls left, right controls right)
            if (driver.FindBone(out BasisLocalBoneControl leftToes, BasisBoneTrackedRole.LeftToes))
            {
                leftToes.OnHasRigChanged += () =>
                {
                    var dd = BasisFullIKConstraint.data;
                    dd.LeftToeEnabled = leftToes.HasRigLayer == BasisHasRigLayer.HasRigLayer;
                    BasisFullIKConstraint.data = dd;
                };
                d.LeftToeEnabled = leftToes.HasRigLayer == BasisHasRigLayer.HasRigLayer;
            }

            if (driver.FindBone(out BasisLocalBoneControl rightToes, BasisBoneTrackedRole.RightToes))
            {
                rightToes.OnHasRigChanged += () =>
                {
                    var dd = BasisFullIKConstraint.data;
                    dd.RightToeEnabled = rightToes.HasRigLayer == BasisHasRigLayer.HasRigLayer;
                    BasisFullIKConstraint.data = dd;
                };
                d.RightToeEnabled = rightToes.HasRigLayer == BasisHasRigLayer.HasRigLayer;
            }

            // Hands
            if (driver.FindBone(out BasisLocalBoneControl leftHand, BasisBoneTrackedRole.LeftHand))
            {
                leftHand.OnHasRigChanged += () =>
                {
                    var dd = BasisFullIKConstraint.data;
                    dd.enabledLeftHand = leftHand.HasRigLayer == BasisHasRigLayer.HasRigLayer;
                    BasisFullIKConstraint.data = dd;
                };
                d.enabledLeftHand = leftHand.HasRigLayer == BasisHasRigLayer.HasRigLayer;
            }

            if (driver.FindBone(out BasisLocalBoneControl rightHand, BasisBoneTrackedRole.RightHand))
            {
                rightHand.OnHasRigChanged += () =>
                {
                    var dd = BasisFullIKConstraint.data;
                    dd.enabledRightHand = rightHand.HasRigLayer == BasisHasRigLayer.HasRigLayer;
                    BasisFullIKConstraint.data = dd;
                };
                d.enabledRightHand = rightHand.HasRigLayer == BasisHasRigLayer.HasRigLayer;
            }

            BasisFullIKConstraint.data = d;

            // Collect usable bones that exist on the avatar
            var activeList = new List<bool>(55);
            var boneList = new List<HumanBodyBones>(55);

            foreach (HumanBodyBones bone in (HumanBodyBones[])Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (!UseableBodyBone(bone)) continue;

                if (ResolveHumanoidBoneTransform(bone) != null)
                {
                    boneList.Add(bone);
                    activeList.Add(false);
                }
            }

            HumanBones = boneList.ToArray();
            isActive = activeList.ToArray();

            // Direct slot lookup table (initialize with sentinel -1)
            _boneToSlot = new BasisConstraintSlotIndex[(int)HumanBodyBones.LastBone];
            for (int i = 0; i < _boneToSlot.Length; i++)
            {
                _boneToSlot[i].Slot = -1;
            }

            // Create batches
            int count = HumanBones.Length;
            int per = BasisFullBodyData.Count;
            BasisFullBodyTargetBinder.InitReflectionCache();

            int boneIdx = 0;
            d = BasisFullIKConstraint.data;

            for (int slot = 0; slot < per && boneIdx < count; slot++, boneIdx++)
            {
                var bone = HumanBones[boneIdx];
                var t = ResolveHumanoidBoneTransform(bone);
                if (t == null) { slot--; continue; } // keep slot usage tight if a bone vanishes

                // write private m_targetN quickly
                BasisFullBodyTargetBinder.SetTargetTransform(ref d, slot, t);

                // default disabled; keep current orientation as offset
                d.SetWeight(slot, false);
                d.SetOffsetRotation(slot, t.rotation);
                d.SetTargetRotation(slot, t.rotation);

                // map bone -> slot
                _boneToSlot[(int)bone] = new BasisConstraintSlotIndex { Slot = (short)slot };
            }

            BasisFullIKConstraint.data = d;

            // Ensure a RigTransform exists on hips
            if (!references.Hips.gameObject.TryGetComponent<RigTransform>(out _))
            {
                references.Hips.gameObject.AddComponent<RigTransform>();
            }

            BasisLocalBoneControl.HasEvents = true;
        }

        private void ChooseSpine(out Transform root, out Transform middle, out Transform tip)
        {
            if (references.HasUpperchest)
            {
                root = references.Upperchest;
                middle = references.neck;
                tip = references.head;
            }
            else if (references.Haschest)
            {
                root = references.chest;
                middle = references.neck;
                tip = references.head;
            }
            else
            {
                root = references.spine;
                middle = references.neck;
                tip = references.head;
            }
        }

        // ------------------------------------------
        // Bone selection
        // ------------------------------------------
        public static bool UseableBodyBone(HumanBodyBones bone)
        {
            switch (bone)
            {
                case HumanBodyBones.LastBone:
                case HumanBodyBones.LeftEye:
                case HumanBodyBones.RightEye:
                    return false;

                // Exclude all fingers
                case HumanBodyBones.LeftThumbProximal:
                case HumanBodyBones.LeftThumbIntermediate:
                case HumanBodyBones.LeftThumbDistal:
                case HumanBodyBones.LeftIndexProximal:
                case HumanBodyBones.LeftIndexIntermediate:
                case HumanBodyBones.LeftIndexDistal:
                case HumanBodyBones.LeftMiddleProximal:
                case HumanBodyBones.LeftMiddleIntermediate:
                case HumanBodyBones.LeftMiddleDistal:
                case HumanBodyBones.LeftRingProximal:
                case HumanBodyBones.LeftRingIntermediate:
                case HumanBodyBones.LeftRingDistal:
                case HumanBodyBones.LeftLittleProximal:
                case HumanBodyBones.LeftLittleIntermediate:
                case HumanBodyBones.LeftLittleDistal:
                case HumanBodyBones.RightThumbProximal:
                case HumanBodyBones.RightThumbIntermediate:
                case HumanBodyBones.RightThumbDistal:
                case HumanBodyBones.RightIndexProximal:
                case HumanBodyBones.RightIndexIntermediate:
                case HumanBodyBones.RightIndexDistal:
                case HumanBodyBones.RightMiddleProximal:
                case HumanBodyBones.RightMiddleIntermediate:
                case HumanBodyBones.RightMiddleDistal:
                case HumanBodyBones.RightRingProximal:
                case HumanBodyBones.RightRingIntermediate:
                case HumanBodyBones.RightRingDistal:
                case HumanBodyBones.RightLittleProximal:
                case HumanBodyBones.RightLittleIntermediate:
                case HumanBodyBones.RightLittleDistal:
                    return false;

                default:
                    return true;
            }
        }

        // ------------------------------------------
        // Overrides API
        // ------------------------------------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetOverrideUsage(HumanBodyBones bone, bool enabled)
        {
            if (!TryGetSlot(bone, out int si)) return;

            int idx = Array.IndexOf(HumanBones, bone);
            if (idx >= 0) isActive[idx] = enabled;

            var d = BasisFullIKConstraint.data;
            d.SetWeight(si, enabled);
            BasisFullIKConstraint.data = d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetOverrideData(HumanBodyBones bone, in Vector3 position, in Quaternion rotation)
        {
            if (!TryGetSlot(bone, out int si)) return;

            var d = BasisFullIKConstraint.data;
            d.SetTargetPosition(si, position);
            d.SetTargetRotation(si, rotation);
            BasisFullIKConstraint.data = d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetSlot(HumanBodyBones bone, out int slot)
        {
            slot = -1;
            int raw = (int)bone;
            if (_boneToSlot == null || raw < 0 || raw >= _boneToSlot.Length) return false;

            var s = _boneToSlot[raw].Slot;
            if (s < 0) return false;

            slot = s;
            return true;
        }

        private Transform ResolveHumanoidBoneTransform(HumanBodyBones bone)
        {
            // Prefer references map if available
            if (BasisLocalAvatarDriver.References != null &&
                BasisLocalAvatarDriver.References.GetTransform(bone, out Transform refT))
                return refT;

            // Fallback to Animator
            var animator = localPlayer?.BasisAvatar?.Animator;
            return animator != null ? animator.GetBoneTransform(bone) : null;
        }

        // ------------------------------------------
        // Calibration / hints
        // ------------------------------------------
        public void CalibrateRoles()
        {
            // Clear all
            foreach (BasisBoneTrackedRole role in Enum.GetValues(typeof(BasisBoneTrackedRole)))
                ApplyHint(role, false);

            var dm = BasisDeviceManagement.Instance;
            if (dm?.AllInputDevices == null) return;

            for (int i = 0; i < dm.AllInputDevices.Count; i++)
            {
                var input = dm.AllInputDevices[i];
                if (input != null && input.TryGetRole(out BasisBoneTrackedRole role))
                    ApplyHint(role, true);
            }
        }

        public void ApplyHint(BasisBoneTrackedRole roleWithHint, bool weight)
        {
            try
            {
                var d = BasisFullIKConstraint.data;

                switch (roleWithHint)
                {
                    case BasisBoneTrackedRole.Chest:
                        d.hintWeightHead = weight;
                        break;

                    case BasisBoneTrackedRole.RightLowerLeg:
                        d.HintWeightRightLowerLeg = weight;
                        break;

                    case BasisBoneTrackedRole.LeftLowerLeg:
                        d.HintWeightLeftLowerLeg = weight;
                        break;

                    // Upper/lower arms both control the hand hint under the 3-bone model
                    case BasisBoneTrackedRole.RightUpperArm:
                    case BasisBoneTrackedRole.RightLowerArm:
                        d.hintWeightRightHand = weight;
                        break;

                    case BasisBoneTrackedRole.LeftUpperArm:
                    case BasisBoneTrackedRole.LeftLowerArm:
                        d.hintWeightLeftHand = weight;
                        break;

                    default:
                        break;
                }

                BasisFullIKConstraint.data = d;
            }
            catch (Exception e)
            {
                BasisDebug.Log($"{e.Message} {e.StackTrace}");
            }
        }

        // ------------------------------------------
        // Rig creation
        // ------------------------------------------
        public GameObject CreateOrGetRig(string role, bool enabled, out Rig rig, out RigLayer rigLayer)
        {
            rig = null;
            rigLayer = default;

            if (Builder != null)
            {
                foreach (var layer in Builder.layers)
                {
                    if (layer?.rig != null && layer.rig.name == $"Rig {role}")
                    {
                        rigLayer = layer;
                        rig = layer.rig;
                        return layer.rig.gameObject;
                    }
                }
            }

            var anim = localPlayer.BasisAvatar.Animator;

            GameObject rigGO = BasisAnimationRiggingHelper.CreateAndSetParent(anim.transform, $"Rig {role}");
            rig = BasisHelpers.GetOrAddComponent<Rig>(rigGO);
            rigLayer = new RigLayer(rig, enabled);

            if (Builder == null) Builder = BasisHelpers.GetOrAddComponent<RigBuilder>(anim.gameObject);
            Builder.layers.Add(rigLayer);

            return rigGO;
        }
    }
}
