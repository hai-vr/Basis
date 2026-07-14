using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.Scripts.Avatar
{
    /// <summary>
    /// Continuous tracker calibration refresh. While the player stands still in roughly the pose they
    /// calibrated in (all gates in BasisContinuousCalibrationCore), each full-body tracker's scale-free
    /// calibration snapshot (BasisInput.CalibratedUnscaledPosition) is blended a small, capped amount
    /// toward where the tracker actually sits, and the position inverse offsets are re-derived through
    /// the standard ReprojectTrackerOffsetsForCurrentAvatar path — so strap creep is absorbed without a
    /// manual recalibration, and a later scale/avatar re-resolve reproduces the correction instead of
    /// reverting it. Rotation drift is measured and flagged but never auto-corrected: the offset
    /// rotation references the bone-sim body frame at ritual-calibration time, which is not reliably
    /// reconstructable mid-session (same reason ReprojectTrackerOffsetsForCurrentAvatar is
    /// position-only). Everything is gated behind BasisSettingsDefaults.ContinuousCalibration
    /// (off by default); when the toggle is off the per-frame hook is a single early-out.
    ///
    /// Baselines are captured at the end of every FullBodyCalibration. Each tracker is adopted in the
    /// body frame of ITS OWN capture-time head anchor (BasisInput.CalibratedUnscaledHead*) — the frame
    /// reprojection rebuilds that tracker from — so a ritual tracker anchors to the ritual frame and a
    /// mid-session recapture (reconnect restore, matcher-pinned tracker) anchors to its own capture
    /// frame, never to wherever the player happens to be standing when it comes online. A tracker
    /// whose snapshot changes underneath us is re-adopted the same way with a fresh correction budget.
    /// </summary>
    public static class BasisContinuousCalibration
    {
        public enum TrackerStatus
        {
            InPose,
            OutOfPose,
            Adjusting,
            Drifted,
        }

        public class TrackerState
        {
            public BasisInput Input;
            public BasisBoneTrackedRole Role;
            /// <summary>Body frame the snapshot's home was adopted in — the frame snapshot writes are expressed in.</summary>
            public Vector3 AnchorOrigin;
            public Quaternion AnchorRotation;
            /// <summary>Snapshot body-local pose at adoption: the drift reference and the correction-cap centre.</summary>
            public Vector3 HomeRelPosition;
            public Quaternion HomeRelRotation;
            public Vector3 LastWrittenSnapshot;
            public Vector3 LastUnscaledPosition;
            public bool HasLastSample;
            public Vector3 CurrentRelPosition;
            public TrackerStatus Status = TrackerStatus.InPose;
            /// <summary>Raw drift home → live (metres); persists even after the correction absorbed it.</summary>
            public float PositionDriftMeters;
            public float RotationDriftDegrees;
            /// <summary>Uncorrected remainder snapshot → live (metres); what is still visually wrong.</summary>
            public float ResidualMeters;
            public bool HasLoggedDrift;
        }

        private const int RenderPriority = 252;

        private static readonly List<TrackerState> s_states = new List<TrackerState>(8);
        private static bool s_registered;
        private static float s_dwell;
        private static Vector3 s_lastHeadUnscaled;
        private static bool s_hasLastHead;

        public static IReadOnlyList<TrackerState> States => s_states;
        public static bool CorrectionActive { get; private set; }
        public static float TotalCorrectedMeters { get; private set; }

        /// <summary>
        /// Rebase every calibrated FBT tracker's home on the just-captured calibration. Called at the
        /// end of FullBodyCalibration; wrapped so a failure here can never break calibration itself.
        /// </summary>
        public static void CaptureBaseline()
        {
            try
            {
                s_states.Clear();
                s_dwell = 0f;
                s_hasLastHead = false;
                CorrectionActive = false;
                TotalCorrectedMeters = 0f;

                BasisObservableList<BasisInput> devices = BasisDeviceManagement.Instance != null ? BasisDeviceManagement.Instance.AllInputDevices : null;
                if (devices != null)
                {
                    int count = devices.Count;
                    for (int Index = 0; Index < count; Index++)
                    {
                        TryAdopt(devices[Index]);
                    }
                }
                EnsureRegistered();
            }
            catch (System.Exception e)
            {
                BasisDebug.LogError($"Continuous calibration baseline capture failed: {e}", BasisDebug.LogTag.Input);
            }
        }

        private static void EnsureRegistered()
        {
            if (s_registered)
            {
                return;
            }
            BasisLocalPlayer.AfterSimulateOnRender.AddAction(RenderPriority, OnRender);
            s_registered = true;
        }

        private static bool IsEligible(BasisInput input, out BasisBoneTrackedRole role)
        {
            role = default;
            return input != null
                && input.TryGetRole(out role)
                && BasisBoneTrackedRoleCommonCheck.CheckItsFBTracker(role)
                && input.HasCalibratedOffsetSnapshot
                && input.HasControl
                && input.Control != null
                && input.Control.UseInverseOffset;
        }

        // Adoption frame = the body frame of the head anchor THIS tracker's snapshot was captured
        // against (a ritual tracker gets the ritual frame; a mid-session recapture gets its own capture
        // frame). Adopting against the LIVE frame instead expressed a ritual-era snapshot in wherever
        // the player currently stands, which read as permanent out-of-pose drift and deadlocked the
        // shared gates. Refuses when the anchor's yaw frame is degenerate (captured pitched >60°).
        private static void TryAdopt(BasisInput input)
        {
            if (!IsEligible(input, out BasisBoneTrackedRole role))
            {
                return;
            }
            for (int Index = 0; Index < s_states.Count; Index++)
            {
                if (ReferenceEquals(s_states[Index].Input, input))
                {
                    return;
                }
            }
            if (!BasisContinuousCalibrationCore.TryComputeBodyFrame(input.CalibratedUnscaledHeadPosition, input.CalibratedUnscaledHeadRotation, out Vector3 origin, out Quaternion rotation))
            {
                return;
            }
            TrackerState state = new TrackerState
            {
                Input = input,
                Role = role,
                AnchorOrigin = origin,
                AnchorRotation = rotation,
                LastWrittenSnapshot = input.CalibratedUnscaledPosition,
            };
            BasisContinuousCalibrationCore.ToBodyLocal(origin, rotation, input.CalibratedUnscaledPosition, input.CalibratedUnscaledRotation, out state.HomeRelPosition, out state.HomeRelRotation);
            s_states.Add(state);
        }

        private static bool ReAdopt(TrackerState state)
        {
            if (!BasisContinuousCalibrationCore.TryComputeBodyFrame(state.Input.CalibratedUnscaledHeadPosition, state.Input.CalibratedUnscaledHeadRotation, out Vector3 origin, out Quaternion rotation))
            {
                return false;
            }
            state.AnchorOrigin = origin;
            state.AnchorRotation = rotation;
            state.LastWrittenSnapshot = state.Input.CalibratedUnscaledPosition;
            state.HasLoggedDrift = false;
            BasisContinuousCalibrationCore.ToBodyLocal(origin, rotation, state.Input.CalibratedUnscaledPosition, state.Input.CalibratedUnscaledRotation, out state.HomeRelPosition, out state.HomeRelRotation);
            return true;
        }

        private static void OnRender()
        {
            if (!BasisSettingsDefaults.ContinuousCalibration.RawValue)
            {
                s_dwell = 0f;
                s_hasLastHead = false;
                CorrectionActive = false;
                return;
            }

            BasisLocalPlayer player = BasisLocalPlayer.Instance;
            BasisLocalBoneControl headControl = BasisLocalBoneDriver.HeadControl;
            if (player == null || headControl == null || BasisLocalAvatarDriver.CurrentlyTposing)
            {
                s_dwell = 0f;
                s_hasLastHead = false;
                CorrectionActive = false;
                return;
            }

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            // Same head source + unscale the ritual snapshot uses, so live body frames and the stored
            // calibration frame describe the same point on the body.
            BasisCalibratedCoords headOut = headControl.OutGoingData;
            BasisCalibrationMath.UnscaleDeviceCoord(headOut.position, headOut.rotation, BasisHeightDriver.DeviceScale,
                BasisInput.OffsetCoords.position, BasisInput.OffsetCoords.rotation, out Vector3 headUnscaled, out Quaternion headUnscaledRot);

            bool frameValid = BasisContinuousCalibrationCore.TryComputeBodyFrame(headUnscaled, headUnscaledRot, out Vector3 origin, out Quaternion rotation);

            float headSpeed = s_hasLastHead ? (headUnscaled - s_lastHeadUnscaled).magnitude / deltaTime : float.MaxValue;
            s_lastHeadUnscaled = headUnscaled;
            s_hasLastHead = true;

            for (int Index = s_states.Count - 1; Index >= 0; Index--)
            {
                TrackerState state = s_states[Index];
                if (!IsEligible(state.Input, out BasisBoneTrackedRole role) || role != state.Role)
                {
                    s_states.RemoveAt(Index);
                    continue;
                }
                // Snapshot rewritten underneath us (reconnect heal, fresh recapture): re-adopt in the
                // new snapshot's own capture frame; drop the state when that frame is degenerate.
                if ((state.Input.CalibratedUnscaledPosition - state.LastWrittenSnapshot).magnitude > BasisContinuousCalibrationCore.ExternalSnapshotEpsilonMeters
                    && !ReAdopt(state))
                {
                    s_states.RemoveAt(Index);
                }
            }
            BasisObservableList<BasisInput> devices = BasisDeviceManagement.Instance != null ? BasisDeviceManagement.Instance.AllInputDevices : null;
            if (devices != null)
            {
                int count = devices.Count;
                for (int Index = 0; Index < count; Index++)
                {
                    TryAdopt(devices[Index]);
                }
            }
            if (s_states.Count == 0)
            {
                s_dwell = 0f;
                CorrectionActive = false;
                return;
            }

            // The standing gate references the ritual head snapshot (same head CONTROL, same unscale),
            // so the avatar's eye-to-head-bone offset cancels. Gating against PlayerEyeHeight compared
            // the head BONE to the raw EYE, sitting the band off-center by that offset — often the
            // entire band, which kept the gate shut while standing perfectly still.
            bool gates = frameValid
                && BasisAvatarIKStageCalibration.TryGetCalibrationHeadSnapshot(out Vector3 calibrationHeadUnscaled, out _)
                && BasisContinuousCalibrationCore.IsStandingHeight(headUnscaled.y, calibrationHeadUnscaled.y, BasisHeightDriver.PlayerEyeHeight)
                && headSpeed <= BasisContinuousCalibrationCore.HeadStillSpeedMetersPerSecond;

            for (int Index = 0; Index < s_states.Count; Index++)
            {
                TrackerState state = s_states[Index];

                Vector3 unscaled = state.Input.UnscaledDeviceCoord.position;
                float speed = state.HasLastSample ? (unscaled - state.LastUnscaledPosition).magnitude / deltaTime : float.MaxValue;
                state.LastUnscaledPosition = unscaled;
                state.HasLastSample = true;

                if (!frameValid)
                {
                    gates = false;
                    continue;
                }

                BasisContinuousCalibrationCore.ToBodyLocal(origin, rotation, unscaled, state.Input.UnscaledDeviceCoord.rotation, out Vector3 relNow, out Quaternion relNowRot);
                state.CurrentRelPosition = relNow;
                state.PositionDriftMeters = (relNow - state.HomeRelPosition).magnitude;
                state.RotationDriftDegrees = Quaternion.Angle(relNowRot, state.HomeRelRotation);

                if (state.PositionDriftMeters > BasisContinuousCalibrationCore.PoseGateMeters)
                {
                    state.Status = TrackerStatus.OutOfPose;
                    gates = false;
                }
                else if (speed > BasisContinuousCalibrationCore.TrackerStillSpeedMetersPerSecond)
                {
                    gates = false;
                }
            }

            if (!gates)
            {
                s_dwell = 0f;
                CorrectionActive = false;
                return;
            }

            s_dwell += deltaTime;
            if (s_dwell < BasisContinuousCalibrationCore.DwellSeconds)
            {
                CorrectionActive = false;
                return;
            }

            float fraction = BasisContinuousCalibrationCore.StepFraction(deltaTime, BasisContinuousCalibrationCore.CorrectionTauSeconds);
            bool dirty = false;
            for (int Index = 0; Index < s_states.Count; Index++)
            {
                TrackerState state = s_states[Index];

                BasisContinuousCalibrationCore.ToBodyLocal(state.AnchorOrigin, state.AnchorRotation, state.Input.CalibratedUnscaledPosition, state.Input.CalibratedUnscaledRotation, out Vector3 storedRel, out _);
                state.ResidualMeters = (state.CurrentRelPosition - storedRel).magnitude;

                float step = BasisContinuousCalibrationCore.BlendWithCap(state.HomeRelPosition, storedRel, state.CurrentRelPosition, fraction,
                    BasisContinuousCalibrationCore.CorrectionCapMeters, out Vector3 blendedRel);
                if (step >= BasisContinuousCalibrationCore.MinAppliedStepMeters)
                {
                    Vector3 newSnapshot = BasisContinuousCalibrationCore.FromBodyLocalPosition(state.AnchorOrigin, state.AnchorRotation, blendedRel);
                    state.Input.CalibratedUnscaledPosition = newSnapshot;
                    state.LastWrittenSnapshot = newSnapshot;
                    TotalCorrectedMeters += step;
                    dirty = true;
                }

                UpdateStatus(state, step);
            }

            if (dirty)
            {
                BasisAvatarIKStageCalibration.ReprojectTrackerOffsetsForCurrentAvatar();
            }
            CorrectionActive = dirty;
        }

        private static void UpdateStatus(TrackerState state, float appliedStep)
        {
            bool flagged = state.PositionDriftMeters > BasisContinuousCalibrationCore.FlagPositionMeters
                || state.RotationDriftDegrees > BasisContinuousCalibrationCore.FlagRotationDegrees;
            if (flagged)
            {
                state.Status = TrackerStatus.Drifted;
                if (!state.HasLoggedDrift)
                {
                    state.HasLoggedDrift = true;
                    BasisDebug.Log($"Continuous calibration: {state.Role} has drifted {state.PositionDriftMeters * 100f:0.0}cm / {state.RotationDriftDegrees:0.0}° from its calibration — a recalibration is recommended.", BasisDebug.LogTag.Input);
                }
                return;
            }
            if (state.HasLoggedDrift
                && state.PositionDriftMeters < BasisContinuousCalibrationCore.FlagPositionMeters * 0.5f
                && state.RotationDriftDegrees < BasisContinuousCalibrationCore.FlagRotationDegrees * 0.5f)
            {
                state.HasLoggedDrift = false;
            }
            if (appliedStep >= BasisContinuousCalibrationCore.MinAppliedStepMeters
                || state.ResidualMeters > BasisContinuousCalibrationCore.SettledResidualMeters)
            {
                state.Status = TrackerStatus.Adjusting;
                return;
            }
            state.Status = TrackerStatus.InPose;
        }
    }
}
