using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Basis.Scripts.Debugging
{
    /// <summary>
    /// Runtime visualization of the FullBody IK self-collision capsules
    /// (chest + hands) used to keep the hands from intersecting the torso.
    /// Built from BasisGizmoManager line gizmos so it renders in-game; the
    /// editor-only equivalent is BasisHandCollisionVisualizer (OnDrawGizmos).
    /// Driven from SMModuleDebugOptions when the developer-tab toggle is on.
    /// </summary>
    public static class BasisIKColliderGizmo
    {
        private const int CapSegments = 16;
        private const int LinesPerCapsule = 4 + (CapSegments * 2);
        private const int CapsuleCount = 3;
        private const int ChestBase = 0;
        private const int LeftHandBase = LinesPerCapsule;
        private const int RightHandBase = LinesPerCapsule * 2;
        private const float LineWidth = 0.003f;

        private static readonly Color ChestColor = new Color(0f, 1f, 0f, 0.9f);
        private static readonly Color HandColor = new Color(0f, 1f, 1f, 0.9f);

        private static readonly int[] _lineIds = new int[LinesPerCapsule * CapsuleCount];
        private static readonly int[] _pointSphereIds = new int[2] { -1, -1 };

        private static bool _created;
        private static bool _visible;
        private static bool _registered;

        public static void Tick(bool shouldShow, BasisFullBodyIK constraint)
        {
            EnsureMasterToggleHook();

            if (!shouldShow || constraint == null)
            {
                SetVisible(false);
                return;
            }

            BasisFullBodyData data = constraint.data;
            if (data.chest == null || data.neck == null)
            {
                SetVisible(false);
                return;
            }

            EnsureCreated();

            Vector3 playerUp = data.PlayerUp.sqrMagnitude > 1e-6f ? data.PlayerUp.normalized : Vector3.up;

            float chestR = Mathf.Max(0f, data.ChestRadius + data.CollisionSkin);
            UpdateCapsule(ChestBase, data.chest.position, data.neck.position, chestR, playerUp);
            SetCapsuleActive(ChestBase, true);

            float handR = Mathf.Max(0f, data.HandRadius + data.HandSkin);

            if (data.UseHandCapsule)
            {
                UpdateHandCapsule(LeftHandBase, data.LeftHand, data.leftLowerArm, handR, playerUp, _pointSphereIds[0]);
                UpdateHandCapsule(RightHandBase, data.RightHand, data.RightLowerArm, handR, playerUp, _pointSphereIds[1]);
            }
            else
            {
                SetCapsuleActive(LeftHandBase, false);
                SetCapsuleActive(RightHandBase, false);
                Vector3 sphereSize = Vector3.one * (handR * 2f);
                BasisGizmoManager.UpdateSphereGizmo(_pointSphereIds[0], data.PositionLeftHand, sphereSize);
                BasisGizmoManager.UpdateSphereGizmo(_pointSphereIds[1], data.PositionRightHand, sphereSize);
                BasisGizmoManager.SetGizmoActive(_pointSphereIds[0], true);
                BasisGizmoManager.SetGizmoActive(_pointSphereIds[1], true);
            }

            _visible = true;
        }

        public static void Shutdown()
        {
            if (!_created)
            {
                return;
            }
            for (int i = 0; i < _lineIds.Length; i++)
            {
                BasisGizmoManager.DestroyGizmo(_lineIds[i]);
            }
            BasisGizmoManager.DestroyGizmo(_pointSphereIds[0]);
            BasisGizmoManager.DestroyGizmo(_pointSphereIds[1]);
            ResetState();
        }

        private static void UpdateHandCapsule(int baseIdx, Transform handTip, Transform handBase, float radius, Vector3 playerUp, int paired_pointSphereId)
        {
            if (handTip == null || handBase == null)
            {
                SetCapsuleActive(baseIdx, false);
                BasisGizmoManager.SetGizmoActive(paired_pointSphereId, false);
                return;
            }
            UpdateCapsule(baseIdx, handTip.position, handBase.position, radius, playerUp);
            SetCapsuleActive(baseIdx, true);
            BasisGizmoManager.SetGizmoActive(paired_pointSphereId, false);
        }

        private static void UpdateCapsule(int baseIdx, Vector3 a, Vector3 b, float radius, Vector3 playerUp)
        {
            Vector3 axis = b - a;
            float h = axis.magnitude;
            Vector3 dir = h > 1e-6f ? axis / h : Vector3.up;

            Vector3 ortho = Vector3.Cross(dir, playerUp);
            if (ortho.sqrMagnitude < 1e-6f)
            {
                ortho = Vector3.Cross(dir, Vector3.right);
            }
            ortho.Normalize();
            Vector3 ortho2 = Vector3.Cross(dir, ortho).normalized;

            BasisGizmoManager.UpdateLineGizmo(_lineIds[baseIdx + 0], a + ortho * radius, b + ortho * radius);
            BasisGizmoManager.UpdateLineGizmo(_lineIds[baseIdx + 1], a - ortho * radius, b - ortho * radius);
            BasisGizmoManager.UpdateLineGizmo(_lineIds[baseIdx + 2], a + ortho2 * radius, b + ortho2 * radius);
            BasisGizmoManager.UpdateLineGizmo(_lineIds[baseIdx + 3], a - ortho2 * radius, b - ortho2 * radius);

            float step = Mathf.PI * 2f / CapSegments;
            int capStart = baseIdx + 4;
            for (int s = 0; s < CapSegments; s++)
            {
                float t0 = step * s;
                float t1 = step * (s + 1);
                Vector3 d0 = ortho * Mathf.Cos(t0) + ortho2 * Mathf.Sin(t0);
                Vector3 d1 = ortho * Mathf.Cos(t1) + ortho2 * Mathf.Sin(t1);
                BasisGizmoManager.UpdateLineGizmo(_lineIds[capStart + s], a + d0 * radius, a + d1 * radius);
                BasisGizmoManager.UpdateLineGizmo(_lineIds[capStart + CapSegments + s], b + d0 * radius, b + d1 * radius);
            }
        }

        private static void EnsureCreated()
        {
            if (_created)
            {
                return;
            }
            CreateCapsuleLines(ChestBase, "Chest", ChestColor);
            CreateCapsuleLines(LeftHandBase, "LeftHand", HandColor);
            CreateCapsuleLines(RightHandBase, "RightHand", HandColor);

            BasisGizmoManager.CreateSphereGizmo("IKCollider_LeftHandPoint", out _pointSphereIds[0],
                Vector3.zero, 0.05f, HandColor);
            BasisGizmoManager.CreateSphereGizmo("IKCollider_RightHandPoint", out _pointSphereIds[1],
                Vector3.zero, 0.05f, HandColor);
            BasisGizmoManager.SetGizmoActive(_pointSphereIds[0], false);
            BasisGizmoManager.SetGizmoActive(_pointSphereIds[1], false);

            _created = true;
            _visible = true;
        }

        private static void CreateCapsuleLines(int baseIdx, string label, Color color)
        {
            for (int i = 0; i < LinesPerCapsule; i++)
            {
                BasisGizmoManager.CreateLineGizmo($"IKCollider_{label}_{i}", out _lineIds[baseIdx + i],
                    Vector3.zero, Vector3.zero, LineWidth, color);
            }
        }

        private static void SetCapsuleActive(int baseIdx, bool active)
        {
            for (int i = 0; i < LinesPerCapsule; i++)
            {
                BasisGizmoManager.SetGizmoActive(_lineIds[baseIdx + i], active);
            }
        }

        private static void SetVisible(bool visible)
        {
            if (!_created || _visible == visible)
            {
                return;
            }
            for (int i = 0; i < _lineIds.Length; i++)
            {
                BasisGizmoManager.SetGizmoActive(_lineIds[i], visible);
            }
            BasisGizmoManager.SetGizmoActive(_pointSphereIds[0], visible);
            BasisGizmoManager.SetGizmoActive(_pointSphereIds[1], visible);
            _visible = visible;
        }

        private static void EnsureMasterToggleHook()
        {
            if (_registered)
            {
                return;
            }
            BasisGizmoManager.OnUseGizmosChanged += OnMasterToggleChanged;
            _registered = true;
        }

        // Master toggle going off blows away the gizmo dictionaries in
        // BasisGizmoManager, so our cached IDs become stale. Drop them; the next
        // Tick will EnsureCreated.
        private static void OnMasterToggleChanged(bool state)
        {
            if (!state)
            {
                ResetState();
            }
        }

        private static void ResetState()
        {
            for (int i = 0; i < _lineIds.Length; i++)
            {
                _lineIds[i] = -1;
            }
            _pointSphereIds[0] = -1;
            _pointSphereIds[1] = -1;
            _created = false;
            _visible = false;
        }
    }
}
