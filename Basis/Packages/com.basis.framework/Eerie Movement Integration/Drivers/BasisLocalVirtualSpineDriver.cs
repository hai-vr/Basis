using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.IK
{
    [System.Serializable]
    public class BasisLocalVirtualSpineDriver
    {
        private bool _initialized;

        private float _lenNeckToChest;

        private float _lenChestToSpine;

        private float _lenSpineToHips;

        private float _lenTotal;

        private float _tChest;

        private float _tSpine;

        private float _standingHipsLocalY;
        private float _standingHeadLocalY;

        private float3 _hipsFromEyeTposeXZ;

        private float3 _headFromEyeTposeXZ;

        private float3 _yawPivotFromEyeTposeXZ;

        private float3 _eyeFromHeadTpose;

        private float _tposeNeckMinusEyeY;

        private readonly BasisNodPivotSampler _nodPivot = new BasisNodPivotSampler(30);

        private float3 _gazeSwingLever;

        private bool _lengthsDirty = true;

        private NativeArray<BasisVirtualSpineCore.SpineSolveState> _solveState;

        public bool HipsFreezeToTpose = false;

        public void Initialize()
        {
            if (_initialized) return;

            BasisLocalBoneDriver.HeadControl.HasVirtualOverride = true;
            BasisLocalBoneDriver.NeckControl.HasVirtualOverride = true;
            BasisLocalBoneDriver.ChestControl.HasVirtualOverride = true;
            BasisLocalBoneDriver.SpineControl.HasVirtualOverride = true;
            BasisLocalBoneDriver.HipsControl.HasVirtualOverride = true;

            BasisLocalPlayer.OnPlayersHeightChangedNextFrame += OnHeightChanged;

            _solveState = new NativeArray<BasisVirtualSpineCore.SpineSolveState>(1, Allocator.Persistent);
            _solveState[0] = default;

            _lengthsDirty = true;
            _initialized = true;
        }

        public void DeInitialize()
        {
            if (!_initialized) return;

            BasisLocalBoneDriver.HeadControl.HasVirtualOverride = false;
            BasisLocalBoneDriver.NeckControl.HasVirtualOverride = false;
            BasisLocalBoneDriver.ChestControl.HasVirtualOverride = false;
            BasisLocalBoneDriver.SpineControl.HasVirtualOverride = false;
            BasisLocalBoneDriver.HipsControl.HasVirtualOverride = false;

            BasisLocalPlayer.OnPlayersHeightChangedNextFrame -= OnHeightChanged;

            if (_solveState.IsCreated) _solveState.Dispose();

            _initialized = false;
        }

        private void OnHeightChanged(BasisHeightDriver.HeightModeChange _)
        {
            _lengthsDirty = true;

            // The learned arm is measured against a particular avatar scale, so a rescale invalidates it
            // along with the T-pose it was seeded from.
            _nodPivot.Reset();
            _gazeSwingLever = float3.zero;

            if (_solveState.IsCreated)
            {
                BasisVirtualSpineCore.SpineSolveState s = _solveState[0];
                s.HeadBaselineInitialized = 0;
                _solveState[0] = s;
            }
        }

        public void Simulate()
        {
            if (!_initialized) return;

            var eye = BasisLocalBoneDriver.EyeControl;
            var head = BasisLocalBoneDriver.HeadControl;
            var neck = BasisLocalBoneDriver.NeckControl;
            var chest = BasisLocalBoneDriver.ChestControl;
            var spine = BasisLocalBoneDriver.SpineControl;
            var hips = BasisLocalBoneDriver.HipsControl;

            // This frame's eye, exactly as the bone sim will publish it a stage later (its tracked
            // branch snaps to incoming, through the inverse offset when one is calibrated).
            BasisCalibratedCoords freshEye;
            if (eye.HasTracked == BasisHasTracked.HasTracker)
            {
                freshEye = eye.IncomingData;
                if (eye.UseInverseOffset)
                {
                    var off = eye.InverseOffsetFromBone;
                    freshEye.position += freshEye.rotation * off.position;
                    freshEye.rotation *= off.rotation;
                }
            }
            else
            {
                freshEye = eye.OutGoingData;
            }

            if (_lengthsDirty)
            {
                RecomputeSegmentLengths(eye, head, neck, chest, spine, hips);
                _gazeSwingLever = _eyeFromHeadTpose;
                _lengthsDirty = false;
            }

            // Only a real HMD traces an arc worth fitting -- BasisDesktopEye synthesises its eye pose from
            // the same swing model, so fitting that would just be reading our own output back.
            if (BasisDeviceManagement.IsCurrentModeVR() &&
                Basis.BasisUI.BasisSettingsDefaults.VSpineNodPivotEstimate.RawValue)
            {
                BasisNodPivotSettings pivotSettings = BasisNodPivotEstimatorCore.Defaults();
                pivotSettings.Scale = BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;
                _gazeSwingLever = _nodPivot.Update(freshEye.position, freshEye.rotation, Time.deltaTime,
                    _eyeFromHeadTpose, in pivotSettings);
            }
            else
            {
                _gazeSwingLever = _eyeFromHeadTpose;
            }

            if (!BasisLocalPlayer.Instance.LocalBoneDriver.TryGetSimStates(out NativeArray<BasisBoneSimState> simStates))
            {
                return;
            }

            Matrix4x4 parentMatrix = BasisLocalPlayer.localToWorldMatrix;

            bool isVR = BasisDeviceManagement.IsCurrentModeVR();

            float torsoYawDeadzoneDeg = Basis.BasisUI.BasisSettingsDefaults.VSpineTorsoYawDeadzoneDeg.RawValue;
            if (isVR && !Basis.BasisUI.BasisSettingsDefaults.VSpineTorsoYawPlayInVR.RawValue)
            {
                torsoYawDeadzoneDeg = 0f;
            }
            // Prone: the whole lying body is the yaw follower (ApplyProneBodyYaw swings it to this
            // yaw), and the relocking anchor would let the head point sideways while the body stays
            // straight. Deadzone-free follow, still smoothed by TorsoYawBlendSpeed.
            if (BasisLocalPlayer.Instance.LocalCharacterDriver.IsProne)
            {
                torsoYawDeadzoneDeg = 0f;
            }

            var leftFoot = BasisLocalBoneDriver.LeftFootControl;
            var rightFoot = BasisLocalBoneDriver.RightFootControl;
            bool leftFootTracked = leftFoot != null && leftFoot.HasTracked == BasisHasTracked.HasTracker;
            bool rightFootTracked = rightFoot != null && rightFoot.HasTracked == BasisHasTracked.HasTracker;

            BasisVirtualSpineCore.SpineSolveParams p = new BasisVirtualSpineCore.SpineSolveParams
            {
                Dt = Time.deltaTime,
                Scale = BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale,
                TrackingLiftY = BasisLocalPlayspaceMover.VerticalOffset * BasisHeightDriver.DeviceScale,
                ParentMatrix = parentMatrix,
                ParentRotation = parentMatrix.rotation,
                EyeRot = freshEye.rotation,

                HeadTargetPos = ResolveTargetPos(head, eye, in freshEye),
                HeadTargetRot = ResolveTargetRot(head, eye, in freshEye),
                NeckTargetPos = ResolveTargetPos(neck, eye, in freshEye),
                NeckTargetRot = ResolveTargetRot(neck, eye, in freshEye),
                ChestTargetPos = ResolveTargetPos(chest, eye, in freshEye),
                ChestTargetRot = ResolveTargetRot(chest, eye, in freshEye),
                SpineTargetPos = ResolveTargetPos(spine, eye, in freshEye),
                SpineTargetRot = ResolveTargetRot(spine, eye, in freshEye),

                HeadScaledOffset = head.ScaledOffset,
                NeckScaledOffset = neck.ScaledOffset,
                ChestScaledOffset = chest.ScaledOffset,
                SpineScaledOffset = spine.ScaledOffset,

                ChestTposeY = chest.TposeLocalScaled.position.y,
                SpineTposeY = spine.TposeLocalScaled.position.y,
                TposeHips = hips.TposeLocalScaled.position,

                LeftFootPos = leftFootTracked ? (float3)leftFoot.OutGoingData.position : float3.zero,
                RightFootPos = rightFootTracked ? (float3)rightFoot.OutGoingData.position : float3.zero,
                LeftFootTracked = (byte)(leftFootTracked ? 1 : 0),
                RightFootTracked = (byte)(rightFootTracked ? 1 : 0),

                ChestPitchFrac = Basis.BasisUI.BasisSettingsDefaults.VSpineChestPitchFrac.RawValue,
                ChestRollFrac = Basis.BasisUI.BasisSettingsDefaults.VSpineChestRollFrac.RawValue,
                SpinePitchFrac = Basis.BasisUI.BasisSettingsDefaults.VSpineSpinePitchFrac.RawValue,
                SpineRollFrac = Basis.BasisUI.BasisSettingsDefaults.VSpineSpineRollFrac.RawValue,
                NeckRotationSpeed = Basis.BasisUI.BasisSettingsDefaults.VSpineNeckRotationSpeed.RawValue,
                ChestRotationSpeed = Basis.BasisUI.BasisSettingsDefaults.VSpineChestRotationSpeed.RawValue,
                SpineRotationSpeed = Basis.BasisUI.BasisSettingsDefaults.VSpineSpineRotationSpeed.RawValue,
                HipsRotationSpeed = Basis.BasisUI.BasisSettingsDefaults.VSpineHipsRotationSpeed.RawValue,
                HipsForwardBias = Basis.BasisUI.BasisSettingsDefaults.VSpineHipsForwardBias.RawValue,

                NeckExtensionDamp = Basis.BasisUI.BasisSettingsDefaults.FBIKNeckExtensionDamp.RawValue,
                NeckFlexionDamp = Basis.BasisUI.BasisSettingsDefaults.FBIKNeckFlexionDamp.RawValue,
                TorsoYawDeadzoneDeg = torsoYawDeadzoneDeg,
                TorsoYawBlendSpeed = Basis.BasisUI.BasisSettingsDefaults.VSpineTorsoYawBlendSpeed.RawValue,

                HipsFreeze = (byte)(HipsFreezeToTpose ? 1 : 0),
                IsLocomoting = (byte)(BasisLocalPlayer.Instance.LocalCharacterDriver.MovementVector.sqrMagnitude > 0.001f ? 1 : 0),

                LenTotal = _lenTotal,
                TChest = _tChest,
                TSpine = _tSpine,

                StandingHipsLocalY = _standingHipsLocalY,
                StandingHeadLocalY = _standingHeadLocalY,
                EyePos = freshEye.position,
                GazeSwingLever = _gazeSwingLever,
                TposeNeckMinusEyeY = _tposeNeckMinusEyeY,
                GazeSwingRemoval = Basis.BasisUI.BasisSettingsDefaults.VSpineGazeSwingRemoval.RawValue,
                HipsAnchorOffsetLocal = _hipsFromEyeTposeXZ,
                HeadRestFromEyeLocal = _headFromEyeTposeXZ,
                // BasisDesktopEye already pins its simulated eye onto the yaw axis, so only a real HMD --
                // a physical object out on the head's lever arm -- has an arc to remove.
                YawPivotFromEyeLocal = isVR ? _yawPivotFromEyeTposeXZ : float3.zero,
                PostureModel = (byte)(Basis.BasisUI.BasisSettingsDefaults.VSpinePostureModel.RawValue ? 1 : 0),
                HipsCompressionStrength = Basis.BasisUI.BasisSettingsDefaults.VSpineHipsCompressionStrength.RawValue,
                HipsMaxDropMeters = Basis.BasisUI.BasisSettingsDefaults.VSpineHipsMaxDropMeters.RawValue * BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale,
            };

            new BasisVirtualSpineCore.BasisVirtualSpineSolveJob
            {
                States = simStates,
                State = _solveState,
                P = p,
                IdxHead = head.Index,
                IdxNeck = neck.Index,
                IdxChest = chest.Index,
                IdxSpine = spine.Index,
                IdxHips = hips.Index,
                SkipHead = TrackerOwned(head),
                SkipNeck = TrackerOwned(neck),
                SkipChest = TrackerOwned(chest),
                SkipSpine = TrackerOwned(spine),
                SkipHips = TrackerOwned(hips),
            }.Run();
        }

        private static byte TrackerOwned(BasisLocalBoneControl c)
        {
            return (byte)(c.HasTracked == BasisHasTracked.HasTracker ? 1 : 0);
        }

        // Targets resolving to the eye read the fresh pose computed above — the sim has not
        // published this frame's eye yet when this solve runs. Every other target is virtual
        // self-state, which is exactly what it was on the previous write.
        private static float3 ResolveTargetPos(BasisLocalBoneControl c, BasisLocalBoneControl eye, in BasisCalibratedCoords freshEye)
        {
            BasisLocalBoneControl target = ResolveTarget(c);
            return ReferenceEquals(target, eye) ? (float3)freshEye.position : (float3)target.OutGoingData.position;
        }

        private static quaternion ResolveTargetRot(BasisLocalBoneControl c, BasisLocalBoneControl eye, in BasisCalibratedCoords freshEye)
        {
            BasisLocalBoneControl target = ResolveTarget(c);
            return ReferenceEquals(target, eye) ? (quaternion)freshEye.rotation : (quaternion)target.OutGoingData.rotation;
        }

        private static BasisLocalBoneControl ResolveTarget(BasisLocalBoneControl c)
        {
            return c.TargetIndex >= 0 ? c.Owner.Controls[c.TargetIndex] : c;
        }

        private void RecomputeSegmentLengths(BasisLocalBoneControl eye, BasisLocalBoneControl head, BasisLocalBoneControl neck, BasisLocalBoneControl chest, BasisLocalBoneControl spine, BasisLocalBoneControl hips)
        {
            float3 pHead = head.TposeLocalScaled.position;
            float3 pNeck = neck.TposeLocalScaled.position;
            float3 pChest = chest.TposeLocalScaled.position;
            float3 pSpine = spine.TposeLocalScaled.position;
            float3 pHips = hips.TposeLocalScaled.position;

            _lenNeckToChest = math.distance(pNeck, pChest);
            _lenChestToSpine = math.distance(pChest, pSpine);
            _lenSpineToHips = math.distance(pSpine, pHips);
            _lenTotal = math.max(1e-4f, _lenNeckToChest + _lenChestToSpine + _lenSpineToHips);
            _tChest = math.saturate(_lenNeckToChest / _lenTotal);
            _tSpine = math.saturate((_lenNeckToChest + _lenChestToSpine) / _lenTotal);

            _standingHipsLocalY = pNeck.y - _lenTotal;

            _standingHeadLocalY = math.max(pHead.y, 1e-3f);

            float3 pEye = eye.TposeLocalScaled.position;
            _eyeFromHeadTpose = pEye - pHead;
            _hipsFromEyeTposeXZ = new float3(pHips.x - pEye.x, 0f, pHips.z - pEye.z);
            _headFromEyeTposeXZ = new float3(pHead.x - pEye.x, 0f, pHead.z - pEye.z);
            _yawPivotFromEyeTposeXZ = new float3(pNeck.x - pEye.x, 0f, pNeck.z - pEye.z);
            _tposeNeckMinusEyeY = pNeck.y - pEye.y;
        }
    }
}
