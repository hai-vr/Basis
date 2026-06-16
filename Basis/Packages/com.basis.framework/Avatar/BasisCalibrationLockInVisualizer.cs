using System.Collections.Generic;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

namespace Basis.Scripts.Avatar
{
    /// <summary>
    /// Calibration "lock-in" aid. While the player holds the T-pose waiting to pull the triggers,
    /// each enabled full-body role draws a proximity sphere at its prime spot that shrinks (amber ->
    /// green) as the nearest tracker closes on it, and each foot adds a forward guide line that
    /// greens when the foot points body-forward and sits flat. It helps line trackers up before the
    /// offset/rotation gets baked at the trigger press.
    ///
    /// Unlike the debug calibration spheres in BasisLocalBoneDriver, this is NOT gated by the
    /// ShowGizmos master toggle: it owns its own AfterSimulateOnRender subscription, live only
    /// between <see cref="Begin"/> and <see cref="End"/> (driven by the calibration flow). The spot
    /// targets reuse the same body frame + priors as the debug spheres
    /// (<see cref="BasisAvatarIKStageCalibration.TryGetCalibrationVisualizationFrame"/>); all
    /// proximity/alignment math lives in BasisCalibrationLockInCore.
    ///
    /// Foot-rotation note: the guide measures the TRACKER's forward, which equals the foot's forward
    /// only for conventionally-mounted pucks. It is an alignment aid (point both feet the same way as
    /// the body, flat), not an absolute foot-direction readout.
    /// </summary>
    public static class BasisCalibrationLockInVisualizer
    {
        /// <summary>Session toggle from the calibration panel. On by default.</summary>
        public static bool Enabled = true;

        // One slot after the bone-driver gizmos (250) so tracker transforms have settled.
        private const int RenderPriority = 251;

        // Tuning as fractions of world eye height so the aid scales with avatar size.
        private const float CaptureFrac = 0.035f;     // within this -> locked (min size, green)
        private const float FalloffFrac = 0.22f;      // beyond this the ball hides (no tracker near)
        private const float MinDiameterFrac = 0.015f;
        private const float MaxDiameterFrac = 0.06f;
        private const float FootLineLengthFrac = 0.16f;
        private const float FootLineWidthFrac = 0.004f;

        private static readonly Color FarColor = new Color(1f, 0.45f, 0.12f, 1f);
        private static readonly Color LockedColor = new Color(0.15f, 1f, 0.35f, 1f);
        private static readonly Color FootMisalignedColor = new Color(1f, 0.22f, 0.13f, 1f);

        private static bool _active;
        private static bool _registered;

        private static readonly Dictionary<BasisBoneTrackedRole, int> _balls = new Dictionary<BasisBoneTrackedRole, int>();
        private static readonly Dictionary<BasisBoneTrackedRole, int> _footLines = new Dictionary<BasisBoneTrackedRole, int>();
        private static readonly List<BasisInput> _candidates = new List<BasisInput>(16);

        /// <summary>Start drawing the lock-in guides. Called when the final T-pose begins.</summary>
        public static void Begin()
        {
            _active = true;
            if (!_registered)
            {
                BasisLocalPlayer.AfterSimulateOnRender.AddAction(RenderPriority, OnRender);
                _registered = true;
            }
        }

        /// <summary>Stop and tear down the guides. Called on calibrate-complete and on cancel.</summary>
        public static void End()
        {
            _active = false;
            if (_registered)
            {
                BasisLocalPlayer.AfterSimulateOnRender.RemoveAction(RenderPriority, OnRender);
                _registered = false;
            }
            DestroyAll();
        }

        private static void OnRender()
        {
            if (!_active || !Enabled)
            {
                DestroyAll();
                return;
            }

            if (!BasisAvatarIKStageCalibration.TryGetCalibrationVisualizationFrame(
                    out Vector3 bodyOrigin,
                    out Quaternion bodyRot,
                    out float eyeHeight,
                    out IReadOnlyList<BasisAvatarIKStageCalibration.ConstellationDebug.DebugPrior> priors)
                || priors == null)
            {
                DestroyAll();
                return;
            }

            float capture = CaptureFrac * eyeHeight;
            float falloff = FalloffFrac * eyeHeight;
            float minDiam = MinDiameterFrac * eyeHeight;
            float maxDiam = MaxDiameterFrac * eyeHeight;
            float lineLen = FootLineLengthFrac * eyeHeight;
            float lineWidth = FootLineWidthFrac * eyeHeight;
            Vector3 bodyForward = bodyRot * Vector3.forward;

            CollectCandidates();

            for (int i = 0; i < priors.Count; i++)
            {
                BasisAvatarIKStageCalibration.ConstellationDebug.DebugPrior prior = priors[i];
                BasisBoneTrackedRole role = prior.Role;

                // Only the roles the user can actually fill: enabled, and a full-body tracker
                // (skips hands/head, which keep their roles and are never lock-in targets).
                if (!prior.Enabled || !BasisBoneTrackedRoleCommonCheck.CheckItsFBTracker(role))
                {
                    HideRole(role);
                    continue;
                }

                // Prime spot in body-local playspace: X lateral, Y vertical, Z on the body plane.
                Vector3 target = bodyOrigin + bodyRot * new Vector3(
                    prior.ExpectedLateral * eyeHeight,
                    prior.ExpectedHeight * eyeHeight,
                    0f);

                // A ball only appears once SOME candidate tracker is within range, so roles the
                // player has no tracker for stay out of the view instead of hanging far/red.
                if (!TryNearestCandidate(target, out Vector3 trackerPos, out Quaternion trackerRot, out float dist)
                    || dist > falloff)
                {
                    HideRole(role);
                    continue;
                }

                float weight = BasisCalibrationLockInCore.ProximityWeight(dist, capture, falloff);
                float diameter = BasisCalibrationLockInCore.ProximityRadius(weight, minDiam, maxDiam);
                EnsureBall(role, target, diameter, Color.Lerp(FarColor, LockedColor, weight));

                if (role == BasisBoneTrackedRole.LeftFoot || role == BasisBoneTrackedRole.RightFoot)
                {
                    Vector3 footForward = trackerRot * Vector3.forward;
                    float yaw = BasisCalibrationLockInCore.FootYawDegrees(bodyForward, footForward, Vector3.up);
                    float tilt = BasisCalibrationLockInCore.FootTiltDegrees(trackerRot * Vector3.up, Vector3.up);
                    float score = BasisCalibrationLockInCore.FootAlignmentScore(
                        yaw, tilt, BasisCalibrationLockInCore.DefaultMaxYawDeg, BasisCalibrationLockInCore.DefaultMaxTiltDeg);

                    Vector3 dir = footForward.sqrMagnitude > 1e-6f ? footForward.normalized : bodyForward.normalized;
                    EnsureFootLine(role, trackerPos, trackerPos + dir * lineLen, lineWidth,
                        Color.Lerp(FootMisalignedColor, LockedColor, score));
                }
                else
                {
                    HideFootLine(role);
                }
            }
        }

        private static void CollectCandidates()
        {
            _candidates.Clear();
            BasisDeviceManagement manager = BasisDeviceManagement.Instance;
            if (manager == null)
            {
                return;
            }
            BasisObservableList<BasisInput> devices = manager.AllInputDevices;
            int count = devices.Count;
            for (int i = 0; i < count; i++)
            {
                BasisInput input = devices[i];
                if (input == null)
                {
                    continue;
                }
                // A candidate puck is either unassigned (no role captured yet) or already on a
                // full-body role. Hands / HMD / neck keep their roles and are excluded.
                if (input.TryGetRole(out BasisBoneTrackedRole role) && !BasisBoneTrackedRoleCommonCheck.CheckItsFBTracker(role))
                {
                    continue;
                }
                _candidates.Add(input);
            }
        }

        private static bool TryNearestCandidate(Vector3 target, out Vector3 pos, out Quaternion rot, out float dist)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            dist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < _candidates.Count; i++)
            {
                BasisInput input = _candidates[i];
                Transform t = input != null ? input.transform : null;
                if (t == null)
                {
                    continue;
                }
                float d = Vector3.Distance(t.position, target);
                if (d < dist)
                {
                    dist = d;
                    pos = t.position;
                    rot = t.rotation;
                    found = true;
                }
            }
            return found;
        }

        // Create-or-update, robust to the debug gizmo pool being wiped (ShowGizmos toggled off).
        private static void EnsureBall(BasisBoneTrackedRole role, Vector3 pos, float diameter, Color color)
        {
            if (_balls.TryGetValue(role, out int id) && BasisGizmoManager.Gizmos.ContainsKey(id))
            {
                BasisGizmoManager.UpdateSphereGizmo(id, pos, Vector3.one * diameter);
                BasisGizmoManager.UpdateGizmoColor(id, color);
                return;
            }
            if (BasisGizmoManager.CreateSphereGizmo($"LockIn_{role}", out id, pos, diameter, color))
            {
                _balls[role] = id;
            }
        }

        private static void EnsureFootLine(BasisBoneTrackedRole role, Vector3 start, Vector3 end, float width, Color color)
        {
            if (_footLines.TryGetValue(role, out int id) && BasisGizmoManager.GizmosLine.ContainsKey(id))
            {
                BasisGizmoManager.UpdateLineGizmo(id, start, end);
                BasisGizmoManager.UpdateGizmoColor(id, color);
                return;
            }
            if (BasisGizmoManager.CreateLineGizmo($"LockInFoot_{role}", out id, start, end, width, color))
            {
                _footLines[role] = id;
            }
        }

        private static void HideRole(BasisBoneTrackedRole role)
        {
            if (_balls.TryGetValue(role, out int id))
            {
                SafeDestroy(id);
                _balls.Remove(role);
            }
            HideFootLine(role);
        }

        private static void HideFootLine(BasisBoneTrackedRole role)
        {
            if (_footLines.TryGetValue(role, out int id))
            {
                SafeDestroy(id);
                _footLines.Remove(role);
            }
        }

        private static void DestroyAll()
        {
            if (_balls.Count > 0)
            {
                foreach (KeyValuePair<BasisBoneTrackedRole, int> kvp in _balls)
                {
                    SafeDestroy(kvp.Value);
                }
                _balls.Clear();
            }
            if (_footLines.Count > 0)
            {
                foreach (KeyValuePair<BasisBoneTrackedRole, int> kvp in _footLines)
                {
                    SafeDestroy(kvp.Value);
                }
                _footLines.Clear();
            }
        }

        // The debug ShowGizmos teardown wipes the manager's pools out from under our cached IDs;
        // only call DestroyGizmo when the ID is still live so we don't log spurious warnings.
        private static void SafeDestroy(int id)
        {
            if (BasisGizmoManager.Gizmos.ContainsKey(id) || BasisGizmoManager.GizmosLine.ContainsKey(id))
            {
                BasisGizmoManager.DestroyGizmo(id);
            }
        }
    }
}
