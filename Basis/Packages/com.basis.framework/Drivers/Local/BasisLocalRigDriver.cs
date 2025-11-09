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
        /// <summary>
        /// Lower = more smoothing; Higher = more responsive. (0.01f, 10f)
        /// </summary>
        public static float MinCutoff = 5.5f;

        /// <summary>
        /// How much to raise cutoff when motion is fast (reduces lag during quick moves). (0f, 10f)
        /// </summary>
        public static float Beta = 3.25f;

        /// <summary>
        /// Cutoff for derivative smoothing. (0.01f, 10f)
        /// </summary>
        public static float DerivativeCutoff = 3f;

        public RigBuilder Builder;
        public List<RigTransform> AdditionalTransforms = new List<RigTransform>();
        public PlayableGraph PlayableGraph;

        private BasisLocalPlayer localPlayer;
        private BasisTransformMapping BasisTransformMapping;

        private OneEuroFilterVector3 posFilters = new OneEuroFilterVector3(MinCutoff, Beta, DerivativeCutoff);
        private float _timeAccumulator;

        public Rig MainRig;
        public RigLayer RigLayer;
        public BasisFullBodyIK BasisFullIKConstraint;

        // ------------------------------------------
        // Lifecycle
        // ------------------------------------------
        public void Initialize(BasisLocalPlayer localPlayer, BasisTransformMapping references)
        {
            this.localPlayer = localPlayer;
            this.BasisTransformMapping = references;
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
            if (BasisFullIKConstraint == null || Builder == null)
            {
                return;
            }

            _timeAccumulator += Mathf.Max(deltaTime, 1e-6f);
            var Hips = BasisLocalBoneDriver.HipsControl;
            // Hips (filtered)
            var hipsCoords = Hips.OutgoingWorldData;
            posFilters.minCutoff = MinCutoff;
            posFilters.beta = Beta;
            posFilters.dCutoff = DerivativeCutoff;

            var hipspos = posFilters.Filter(hipsCoords.position, _timeAccumulator);

            var d = BasisFullIKConstraint.data;
            d.PositionHips = hipspos;
            d.RotationEulerHips = hipsCoords.rotation;

            // Global hint direction (knee/neck)
            d.m_HintDirection = Hips.OutgoingWorldData.rotation * Vector3.right;

            // Head
            var data = BasisLocalBoneDriver.HeadControl.OutgoingWorldData;
            d.PositionHead = data.position;
            d.RotationHead = data.rotation;

            // Feet
            data = BasisLocalBoneDriver.LeftFootControl.OutgoingWorldData;
            d.LeftFootPosition = data.position;
            d.LeftFootRotation = data.rotation;

            data = BasisLocalBoneDriver.RightFootControl.OutgoingWorldData;
            d.RightFootPosition = data.position;
            d.RightFootRotation = data.rotation;

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
            d.PositionLeftHand = leftHand.position;
            d.RotationLeftHand = leftHand.rotation;

            var rightHand = BasisLocalBoneDriver.RightHandControl.OutgoingWorldData;
            d.PositionRightHand = rightHand.position;
            d.RotationRightHand = rightHand.rotation;

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

            Builder.SyncLayers();
            PlayableGraph.Evaluate(deltaTime);
        }
        public void Spine(GameObject MainRig)
        {
            BasisAnimationRiggingHelper.CreateBasisFullBodyRIG(localPlayer, MainRig, BasisTransformMapping, out BasisFullIKConstraint);

            // Base enables
            var d = BasisFullIKConstraint.data;

            // Legs enabled by presence
            BasisLocalBoneDriver.LeftFootControl.OnHasRigChanged += () =>
            {
                var dd = BasisFullIKConstraint.data;
                dd.EnableLeftLeg = BasisLocalBoneDriver.LeftFootControl.HasRigLayer == BasisHasRigLayer.HasRigLayer;
                BasisFullIKConstraint.data = dd;
            };
            d.EnableLeftLeg = BasisLocalBoneDriver.LeftFootControl.HasRigLayer == BasisHasRigLayer.HasRigLayer;

            BasisLocalBoneDriver.RightFootControl.OnHasRigChanged += () =>
            {
                var dd = BasisFullIKConstraint.data;
                dd.EnableRightLeg = BasisLocalBoneDriver.RightFootControl.HasRigLayer == BasisHasRigLayer.HasRigLayer;
                BasisFullIKConstraint.data = dd;
            };
            d.EnableRightLeg = BasisLocalBoneDriver.RightFootControl.HasRigLayer == BasisHasRigLayer.HasRigLayer;

            // Head-driven layer activity
            BasisLocalBoneDriver.HeadControl.OnHasRigChanged += () =>
            {
                RigLayer.active = BasisLocalBoneDriver.HeadControl.HasRigLayer == BasisHasRigLayer.HasRigLayer;
            };
            RigLayer.active = BasisLocalBoneDriver.HeadControl.HasRigLayer == BasisHasRigLayer.HasRigLayer;

            // Toes (fixed: left controls left, right controls right)
            BasisLocalBoneDriver.LeftToeControl.OnHasRigChanged += () =>
            {
                var dd = BasisFullIKConstraint.data;
                dd.LeftToeEnabled = BasisLocalBoneDriver.LeftToeControl.HasRigLayer == BasisHasRigLayer.HasRigLayer;
                BasisFullIKConstraint.data = dd;
            };
            d.LeftToeEnabled = BasisLocalBoneDriver.LeftToeControl.HasRigLayer == BasisHasRigLayer.HasRigLayer;

            BasisLocalBoneDriver.RightToeControl.OnHasRigChanged += () =>
            {
                var dd = BasisFullIKConstraint.data;
                dd.RightToeEnabled = BasisLocalBoneDriver.RightToeControl.HasRigLayer == BasisHasRigLayer.HasRigLayer;
                BasisFullIKConstraint.data = dd;
            };
            d.RightToeEnabled = BasisLocalBoneDriver.RightToeControl.HasRigLayer == BasisHasRigLayer.HasRigLayer;

            // Hands
            BasisLocalBoneDriver.LeftHandControl.OnHasRigChanged += () =>
            {
                var dd = BasisFullIKConstraint.data;
                dd.enabledLeftHand = BasisLocalBoneDriver.LeftHandControl.HasRigLayer == BasisHasRigLayer.HasRigLayer;
                BasisFullIKConstraint.data = dd;
            };
            d.enabledLeftHand = BasisLocalBoneDriver.LeftHandControl.HasRigLayer == BasisHasRigLayer.HasRigLayer;

            BasisLocalBoneDriver.RightHandControl.OnHasRigChanged += () =>
            {
                var dd = BasisFullIKConstraint.data;
                dd.enabledRightHand = BasisLocalBoneDriver.RightHandControl.HasRigLayer == BasisHasRigLayer.HasRigLayer;
                BasisFullIKConstraint.data = dd;
            };
            d.enabledRightHand = BasisLocalBoneDriver.RightHandControl.HasRigLayer == BasisHasRigLayer.HasRigLayer;
            int per = BasisFullBodyData.Count;
            for (int slot = 0; slot < per; slot++)
            {
                var t = ResolveHumanoidBoneTransform((HumanBodyBones)slot);
                if (t != null)
                {                 // default disabled; keep current orientation as offset
                    d.SetWeight(slot, false);
                    d.SetOffsetRotation(slot, t.rotation);
                    d.SetTargetRotation(slot, t.rotation);
                } // keep slot usage tight if a bone vanishes
                else
                {
                    slot--; continue;
                } // keep slot usage tight if a bone vanishes
            }
            BasisFullIKConstraint.data = d;
        }
        // ------------------------------------------
        // Rig setup
        // ------------------------------------------
        public void SetBodySettings()
        {
            var rigGO = CreateOrGetRig("Main IK", true, out MainRig, out RigLayer);

            Spine(rigGO);
            // Ensure a RigTransform exists on hips
            if (!BasisTransformMapping.Hips.gameObject.TryGetComponent<RigTransform>(out _))
            {
                BasisTransformMapping.Hips.gameObject.AddComponent<RigTransform>();
            }

            BasisLocalBoneControl.HasEvents = true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetOverrideUsage(HumanBodyBones bone, bool enabled)
        {
            var d = BasisFullIKConstraint.data;
            d.SetWeight((int)bone, enabled);
            BasisFullIKConstraint.data = d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetOverrideData(HumanBodyBones bone, in Vector3 position, in Quaternion rotation)
        {
            var d = BasisFullIKConstraint.data;
            d.SetTargetPosition((int)bone, position);
            d.SetTargetRotation((int)bone, rotation);
            BasisFullIKConstraint.data = d;
        }
        private Transform ResolveHumanoidBoneTransform(HumanBodyBones bone)
        {
            // Prefer references map if available
            if (BasisLocalAvatarDriver.References != null && BasisLocalAvatarDriver.References.GetTransform(bone, out Transform refT))
            {
                return refT;
            }

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
            {
                ApplyHint(role, false);
            }

            var dm = BasisDeviceManagement.Instance;

            for (int i = 0; i < dm.AllInputDevices.Count; i++)
            {
                var input = dm.AllInputDevices[i];
                if (input != null && input.TryGetRole(out BasisBoneTrackedRole role))
                {
                    ApplyHint(role, true);
                }
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
