using Basis.Scripts.BasisSdk.Players;
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

        private float3 _eyeFromHeadTpose;

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

            BasisLocalPlayer.Instance.OnVirtualData += OnSimulate;
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

            if (BasisLocalPlayer.Instance != null)
            {
                BasisLocalPlayer.Instance.OnVirtualData -= OnSimulate;
            }
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame -= OnHeightChanged;

            if (_solveState.IsCreated) _solveState.Dispose();

            _initialized = false;
        }

        private void OnHeightChanged(BasisHeightDriver.HeightModeChange _)
        {
            _lengthsDirty = true;

            if (_solveState.IsCreated)
            {
                BasisVirtualSpineCore.SpineSolveState s = _solveState[0];
                s.HeadBaselineInitialized = 0;
                _solveState[0] = s;
            }
        }

        public void OnSimulate()
        {
            var eye = BasisLocalBoneDriver.EyeControl;
            var head = BasisLocalBoneDriver.HeadControl;
            var neck = BasisLocalBoneDriver.NeckControl;
            var chest = BasisLocalBoneDriver.ChestControl;
            var spine = BasisLocalBoneDriver.SpineControl;
            var hips = BasisLocalBoneDriver.HipsControl;

            if (_lengthsDirty)
            {
                RecomputeSegmentLengths(eye, head, neck, chest, spine, hips);
                _lengthsDirty = false;
            }

            if (!BasisLocalPlayer.Instance.LocalBoneDriver.TryGetSimStates(out NativeArray<BasisBoneSimState> simStates))
            {
                return;
            }

            Matrix4x4 parentMatrix = BasisLocalPlayer.localToWorldMatrix;

            float torsoYawDeadzoneDeg = Basis.BasisUI.BasisSettingsDefaults.VSpineTorsoYawDeadzoneDeg.RawValue;
            if (BasisDeviceManagement.IsCurrentModeVR() && !Basis.BasisUI.BasisSettingsDefaults.VSpineTorsoYawPlayInVR.RawValue)
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
                EyeRot = eye.OutGoingData.rotation,

                HeadTargetPos = ResolveTargetPos(head),
                HeadTargetRot = ResolveTargetRot(head),
                NeckTargetPos = ResolveTargetPos(neck),
                NeckTargetRot = ResolveTargetRot(neck),
                ChestTargetPos = ResolveTargetPos(chest),
                ChestTargetRot = ResolveTargetRot(chest),
                SpineTargetPos = ResolveTargetPos(spine),
                SpineTargetRot = ResolveTargetRot(spine),

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
                TorsoYawDeadzoneDeg = torsoYawDeadzoneDeg,
                TorsoYawBlendSpeed = Basis.BasisUI.BasisSettingsDefaults.VSpineTorsoYawBlendSpeed.RawValue,

                HipsFreeze = (byte)(HipsFreezeToTpose ? 1 : 0),
                IsLocomoting = (byte)(BasisLocalPlayer.Instance.LocalCharacterDriver.MovementVector.sqrMagnitude > 0.001f ? 1 : 0),

                LenTotal = _lenTotal,
                TChest = _tChest,
                TSpine = _tSpine,

                StandingHipsLocalY = _standingHipsLocalY,
                StandingHeadLocalY = _standingHeadLocalY,
                EyePos = eye.OutGoingData.position,
                EyeFromHeadTpose = _eyeFromHeadTpose,
                GazeSwingRemoval = Basis.BasisUI.BasisSettingsDefaults.VSpineGazeSwingRemoval.RawValue,
                HipsAnchorOffsetLocal = _hipsFromEyeTposeXZ,
                HeadRestFromEyeLocal = _headFromEyeTposeXZ,
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
            }.Run();
        }

        private static float3 ResolveTargetPos(BasisLocalBoneControl c)
        {
            return ResolveTarget(c).OutGoingData.position;
        }

        private static quaternion ResolveTargetRot(BasisLocalBoneControl c)
        {
            return ResolveTarget(c).OutGoingData.rotation;
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
        }
    }
}
