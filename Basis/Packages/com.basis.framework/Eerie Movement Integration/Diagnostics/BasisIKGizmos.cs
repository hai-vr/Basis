using Basis.IK;
using Basis.Scripts.Common;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;
using static Basis.Scripts.Avatar.BasisAvatarIKStageCalibration;

namespace Basis.Scripts.Debugging
{
    public static class BasisIKColliderGizmo
    {
        private const int CapSegments = 16;

        private const int LinesPerCapsule = 6;
        private const int CapA_Offset = 4;
        private const int CapB_Offset = 5;
        private const int CapsuleCount = 7;
        private const int HipsBase = 0;
        private const int SpineBase = LinesPerCapsule;
        private const int ChestBase = LinesPerCapsule * 2;
        private const int LeftHandBase = LinesPerCapsule * 3;
        private const int RightHandBase = LinesPerCapsule * 4;
        private const int LeftUpperArmBase = LinesPerCapsule * 5;
        private const int RightUpperArmBase = LinesPerCapsule * 6;
        private const float LineWidth = 0.003f;

        private const float SpineRadiusMultiplier = 0.8f;
        private const float HipsRadiusMultiplier = 1.4f;
        private const float UpperArmRadiusMultiplier = 1.2f;

        private const float ChestDepthRatio = 0.68f;

        private static readonly Color TorsoColor = new Color(0f, 1f, 0f, 0.9f);
        private static readonly Color HandColor = new Color(0f, 1f, 1f, 0.9f);
        private static readonly Color UpperArmColor = new Color(1f, 0f, 1f, 0.9f);

        private static readonly int[] _lineIds = new int[LinesPerCapsule * CapsuleCount];
        private static readonly int[] _pointSphereIds = new int[2] { -1, -1 };

        private static readonly int[] _labelIds = new int[CapsuleCount];
        private static readonly string[] CapsuleNames =
            { "Hips", "Spine", "Chest", "LeftHand", "RightHand", "LeftUpperArm", "RightUpperArm" };
        private const float LabelScale = 0.02f;

        private static readonly Vector3[] _capBuffer = new Vector3[CapSegments];

        private static bool _created;
        private static bool _visible;
        private static bool _registered;

        public static void Tick(bool shouldShow, BasisTransformMapping bones, in BasisEerieMovement job, bool showLabels, Vector3 cameraPos)
        {
            EnsureMasterToggleHook();

            if (!shouldShow)
            {
                SetVisible(false);
                DestroyLabels();
                return;
            }

            if (bones.chest == null || bones.neck == null)
            {
                SetVisible(false);
                DestroyLabels();
                return;
            }

            EnsureCreated();

            Vector3 playerUp = job.playerUp.sqrMagnitude > 1e-6f ? job.playerUp.normalized : Vector3.up;

            float chestRBase = job.chestRadius;
            float skin = job.collisionSkin;
            float chestR = Mathf.Max(0f, chestRBase + skin);
            float spineR = Mathf.Max(0f, chestRBase * SpineRadiusMultiplier + skin);
            float hipsR = Mathf.Max(0f, chestRBase * HipsRadiusMultiplier + skin);

            Vector3 bodyRight = (bones.leftUpperArm != null && bones.RightUpperArm != null)
                ? bones.RightUpperArm.position - bones.leftUpperArm.position : Vector3.zero;
            Vector3 bodyLat = bodyRight - playerUp * Vector3.Dot(bodyRight, playerUp);
            Vector3 bodyFwd = Vector3.zero;
            bool elliptical = bodyLat.sqrMagnitude > 1e-6f;
            if (elliptical)
            {
                bodyLat.Normalize();
                bodyFwd = Vector3.Cross(bodyLat, playerUp);
                elliptical = bodyFwd.sqrMagnitude > 1e-6f;
                if (elliptical) bodyFwd.Normalize();
            }

            if (elliptical)
            {
                UpdateEllipticalBoneCapsule(HipsBase, bones.Hips, bones.spine, hipsR, spineR, bodyLat, bodyFwd);
                UpdateEllipticalBoneCapsule(SpineBase, bones.spine, bones.chest, spineR, chestR, bodyLat, bodyFwd);
                UpdateEllipticalCapsule(ChestBase, bones.chest.position, bones.neck.position, chestR, chestR, bodyLat, bodyFwd);
                SetCapsuleActive(ChestBase, true);
            }
            else
            {
                UpdateBoneCapsule(HipsBase, bones.Hips, bones.spine, hipsR, playerUp);
                UpdateBoneCapsule(SpineBase, bones.spine, bones.chest, spineR, playerUp);
                UpdateCapsule(ChestBase, bones.chest.position, bones.neck.position, chestR, playerUp);
                SetCapsuleActive(ChestBase, true);
            }

            float handR = Mathf.Max(0f, job.handRadius + job.handSkin);

            UpdateHandCapsule(LeftHandBase, bones.leftHand, bones.leftLowerArm, handR, playerUp, _pointSphereIds[0]);
            UpdateHandCapsule(RightHandBase, bones.rightHand, bones.RightLowerArm, handR, playerUp, _pointSphereIds[1]);

            float upperArmR = handR * UpperArmRadiusMultiplier;
            UpdateBoneCapsule(LeftUpperArmBase, bones.leftUpperArm, bones.leftLowerArm, upperArmR, playerUp);
            UpdateBoneCapsule(RightUpperArmBase, bones.RightUpperArm, bones.RightLowerArm, upperArmR, playerUp);

            UpdateLabels(showLabels, cameraPos, bones, job);

            _visible = true;
        }

        private static void UpdateLabels(bool showLabels, Vector3 cameraPos, BasisTransformMapping bones, in BasisEerieMovement job)
        {
            float labelScale = LabelScale * Mathf.Max(0.01f, BasisHeightDriver.ScaledToMatchValue);
            for (int i = 0; i < CapsuleCount; i++)
            {
                if (showLabels && TryCapsuleMidpoint(bones, job, i, out Vector3 mid))
                {
                    Color color = LabelColor(i);
                    if (_labelIds[i] <= 0)
                    {
                        BasisGizmoManager.CreateTextGizmo($"IKLabel_{CapsuleNames[i]}", out _labelIds[i], mid, CapsuleNames[i], color);
                    }
                    Quaternion rot = BasisGizmoManager.BillboardRotation(mid, cameraPos);
                    BasisGizmoManager.UpdateTextGizmo(_labelIds[i], mid, rot, labelScale, CapsuleNames[i], color);
                    BasisGizmoManager.SetGizmoActive(_labelIds[i], true);
                }
                else if (_labelIds[i] > 0)
                {
                    BasisGizmoManager.DestroyGizmo(_labelIds[i]);
                    _labelIds[i] = -1;
                }
            }
        }

        private static Color LabelColor(int idx)
        {
            if (idx < 3) return TorsoColor;
            return idx < 5 ? HandColor : UpperArmColor;
        }

        private static bool TryCapsuleMidpoint(BasisTransformMapping bones, in BasisEerieMovement job, int idx, out Vector3 mid)
        {
            Transform a = null, b = null;
            switch (idx)
            {
                case 0: a = bones.Hips; b = bones.spine; break;
                case 1: a = bones.spine; b = bones.chest; break;
                case 2: a = bones.chest; b = bones.neck; break;
                case 3: a = bones.leftHand; b = bones.leftLowerArm; break;
                case 4: a = bones.rightHand; b = bones.RightLowerArm; break;
                case 5: a = bones.leftUpperArm; b = bones.leftLowerArm; break;
                case 6: a = bones.RightUpperArm; b = bones.RightLowerArm; break;
            }
            if (a == null || b == null)
            {
                mid = default;
                return false;
            }
            mid = (a.position + b.position) * 0.5f;
            return true;
        }

        private static void DestroyLabels()
        {
            for (int i = 0; i < _labelIds.Length; i++)
            {
                if (_labelIds[i] > 0)
                {
                    BasisGizmoManager.DestroyGizmo(_labelIds[i]);
                    _labelIds[i] = -1;
                }
            }
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
            DestroyLabels();
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

        private static void UpdateBoneCapsule(int baseIdx, Transform a, Transform b, float radius, Vector3 playerUp)
        {
            if (a == null || b == null)
            {
                SetCapsuleActive(baseIdx, false);
                return;
            }
            UpdateCapsule(baseIdx, a.position, b.position, radius, playerUp);
            SetCapsuleActive(baseIdx, true);
        }

        private static void UpdateEllipticalBoneCapsule(int baseIdx, Transform a, Transform b, float latR0, float latR1, Vector3 bodyLat, Vector3 bodyFwd)
        {
            if (a == null || b == null)
            {
                SetCapsuleActive(baseIdx, false);
                return;
            }
            UpdateEllipticalCapsule(baseIdx, a.position, b.position, latR0, latR1, bodyLat, bodyFwd);
            SetCapsuleActive(baseIdx, true);
        }

        private static void UpdateEllipticalCapsule(int baseIdx, Vector3 a, Vector3 b, float latR0, float latR1, Vector3 bodyLat, Vector3 bodyFwd)
        {
            Vector3 axis = b - a;
            float h = axis.magnitude;
            Vector3 dir = h > 1e-6f ? axis / h : Vector3.up;

            Vector3 u = bodyLat - dir * Vector3.Dot(bodyLat, dir);
            u = u.sqrMagnitude > 1e-8f ? u.normalized : Vector3.Cross(dir, Vector3.up).normalized;
            Vector3 w = bodyFwd - dir * Vector3.Dot(bodyFwd, dir);
            w = w.sqrMagnitude > 1e-8f ? w.normalized : Vector3.Cross(dir, u).normalized;

            float apR0 = latR0 * ChestDepthRatio;
            float apR1 = latR1 * ChestDepthRatio;

            BasisGizmoManager.UpdateLineGizmo(_lineIds[baseIdx + 0], a + u * latR0, b + u * latR1);
            BasisGizmoManager.UpdateLineGizmo(_lineIds[baseIdx + 1], a - u * latR0, b - u * latR1);
            BasisGizmoManager.UpdateLineGizmo(_lineIds[baseIdx + 2], a + w * apR0, b + w * apR1);
            BasisGizmoManager.UpdateLineGizmo(_lineIds[baseIdx + 3], a - w * apR0, b - w * apR1);

            float step = Mathf.PI * 2f / CapSegments;
            for (int s = 0; s < CapSegments; s++)
            {
                float t = step * s;
                _capBuffer[s] = a + u * (Mathf.Cos(t) * latR0) + w * (Mathf.Sin(t) * apR0);
            }
            BasisGizmoManager.UpdateLineGizmo(_lineIds[baseIdx + CapA_Offset], _capBuffer);
            for (int s = 0; s < CapSegments; s++)
            {
                float t = step * s;
                _capBuffer[s] = b + u * (Mathf.Cos(t) * latR1) + w * (Mathf.Sin(t) * apR1);
            }
            BasisGizmoManager.UpdateLineGizmo(_lineIds[baseIdx + CapB_Offset], _capBuffer);
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

            for (int s = 0; s < CapSegments; s++)
            {
                float t = step * s;
                _capBuffer[s] = a + (ortho * Mathf.Cos(t) + ortho2 * Mathf.Sin(t)) * radius;
            }
            BasisGizmoManager.UpdateLineGizmo(_lineIds[baseIdx + CapA_Offset], _capBuffer);

            for (int s = 0; s < CapSegments; s++)
            {
                float t = step * s;
                _capBuffer[s] = b + (ortho * Mathf.Cos(t) + ortho2 * Mathf.Sin(t)) * radius;
            }
            BasisGizmoManager.UpdateLineGizmo(_lineIds[baseIdx + CapB_Offset], _capBuffer);
        }

        private static void EnsureCreated()
        {
            if (_created)
            {
                return;
            }
            CreateCapsuleLines(HipsBase, "Hips", TorsoColor);
            CreateCapsuleLines(SpineBase, "Spine", TorsoColor);
            CreateCapsuleLines(ChestBase, "Chest", TorsoColor);
            CreateCapsuleLines(LeftHandBase, "LeftHand", HandColor);
            CreateCapsuleLines(RightHandBase, "RightHand", HandColor);
            CreateCapsuleLines(LeftUpperArmBase, "LeftUpperArm", UpperArmColor);
            CreateCapsuleLines(RightUpperArmBase, "RightUpperArm", UpperArmColor);

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
            for (int i = 0; i < 4; i++)
            {
                BasisGizmoManager.CreateLineGizmo($"IKCollider_{label}_axial{i}", out _lineIds[baseIdx + i],
                    Vector3.zero, Vector3.zero, LineWidth, color);
            }

            for (int i = 0; i < CapSegments; i++)
            {
                _capBuffer[i] = Vector3.zero;
            }
            BasisGizmoManager.CreateLineGizmo($"IKCollider_{label}_capA", out _lineIds[baseIdx + CapA_Offset],
                _capBuffer, LineWidth, color, loop: true);
            BasisGizmoManager.CreateLineGizmo($"IKCollider_{label}_capB", out _lineIds[baseIdx + CapB_Offset],
                _capBuffer, LineWidth, color, loop: true);
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
            for (int i = 0; i < _labelIds.Length; i++)
            {
                _labelIds[i] = -1;
            }
            _created = false;
            _visible = false;
        }
    }

    public static class BasisHintOffsetGizmos
    {
        public static bool Show;

        private const int RoleCount = 5;
        private const float RawSphereSize = 0.03f;
        private const float BiasedSphereSize = 0.03f;
        private const float AxisLength = 0.06f;
        private const float LineWidth = 0.003f;
        private const float LabelScale = 0.02f;

        private static readonly Color RawColor = new Color(1f, 1f, 0f, 0.9f);
        private static readonly Color BiasedColor = new Color(0f, 1f, 1f, 0.9f);
        private static readonly Color OffsetLineColor = new Color(1f, 0.25f, 0.25f, 0.95f);
        private static readonly Color XAxisColor = new Color(1f, 0.2f, 0.2f, 0.9f);
        private static readonly Color YAxisColor = new Color(0.2f, 1f, 0.2f, 0.9f);
        private static readonly Color ZAxisColor = new Color(0.2f, 0.4f, 1f, 0.9f);

        private static readonly BasisBoneTrackedRole[] Roles =
        {
            BasisBoneTrackedRole.Chest,
            BasisBoneTrackedRole.LeftLowerArm,
            BasisBoneTrackedRole.RightLowerArm,
            BasisBoneTrackedRole.LeftLowerLeg,
            BasisBoneTrackedRole.RightLowerLeg,
        };

        private static readonly int[] _rawSphere = NewIds();
        private static readonly int[] _biasedSphere = NewIds();
        private static readonly int[] _offsetLine = NewIds();
        private static readonly int[] _axisX = NewIds();
        private static readonly int[] _axisY = NewIds();
        private static readonly int[] _axisZ = NewIds();
        private static readonly int[] _label = NewIds();
        private static readonly bool[] _slotVisible = new bool[RoleCount];

        private static bool _created;
        private static bool _registered;

        public static void Tick(bool shouldShow, bool showLabels, Vector3 cameraPos)
        {
            EnsureMasterToggleHook();

            if (!shouldShow)
            {
                HideAll();
                return;
            }
            EnsureCreated();

            float labelScale = LabelScale * Mathf.Max(0.01f, BasisHeightDriver.ScaledToMatchValue);

            for (int i = 0; i < RoleCount; i++)
            {
                BasisLocalBoneControl control = ControlForSlot(i);
                if (control == null || !BasisHintBiasStore.TryGet(Roles[i], out Vector3 localOffset))
                {
                    SetSlotVisible(i, false);
                    DestroyLabel(i);
                    continue;
                }

                Vector3 rawPos = control.OutgoingWorldData.position;
                Quaternion rawRot = control.OutgoingWorldData.rotation;
                Vector3 biasedPos = rawPos + rawRot * localOffset;

                BasisGizmoManager.UpdateSphereGizmo(_rawSphere[i], rawPos, Vector3.one * RawSphereSize);
                BasisGizmoManager.UpdateSphereGizmo(_biasedSphere[i], biasedPos, Vector3.one * BiasedSphereSize);
                BasisGizmoManager.UpdateLineGizmo(_offsetLine[i], rawPos, biasedPos);
                BasisGizmoManager.UpdateLineGizmo(_axisX[i], rawPos, rawPos + rawRot * Vector3.right * AxisLength);
                BasisGizmoManager.UpdateLineGizmo(_axisY[i], rawPos, rawPos + rawRot * Vector3.up * AxisLength);
                BasisGizmoManager.UpdateLineGizmo(_axisZ[i], rawPos, rawPos + rawRot * Vector3.forward * AxisLength);
                SetSlotVisible(i, true);

                if (showLabels)
                {
                    string text = $"{Roles[i]}\n|offset|={localOffset.magnitude:F3}m";
                    Vector3 labelPos = rawPos + Vector3.up * (RawSphereSize * 2.5f);
                    if (_label[i] <= 0)
                    {
                        BasisGizmoManager.CreateTextGizmo($"HintLabel_{Roles[i]}", out _label[i], labelPos, text, Color.white);
                    }
                    Quaternion rot = BasisGizmoManager.BillboardRotation(labelPos, cameraPos);
                    BasisGizmoManager.UpdateTextGizmo(_label[i], labelPos, rot, labelScale, text, Color.white);
                    BasisGizmoManager.SetGizmoActive(_label[i], true);
                }
                else
                {
                    DestroyLabel(i);
                }
            }
        }

        public static void Shutdown()
        {
            if (!_created)
            {
                return;
            }
            for (int i = 0; i < RoleCount; i++)
            {
                BasisGizmoManager.DestroyGizmo(_rawSphere[i]);
                BasisGizmoManager.DestroyGizmo(_biasedSphere[i]);
                BasisGizmoManager.DestroyGizmo(_offsetLine[i]);
                BasisGizmoManager.DestroyGizmo(_axisX[i]);
                BasisGizmoManager.DestroyGizmo(_axisY[i]);
                BasisGizmoManager.DestroyGizmo(_axisZ[i]);
                DestroyLabel(i);
            }
            ResetState();
        }

        private static BasisLocalBoneControl ControlForSlot(int i)
        {
            switch (i)
            {
                case 0: return BasisLocalBoneDriver.ChestControl;
                case 1: return BasisLocalBoneDriver.LeftLowerArmControl;
                case 2: return BasisLocalBoneDriver.RightLowerArmControl;
                case 3: return BasisLocalBoneDriver.LeftLowerLegControl;
                case 4: return BasisLocalBoneDriver.RightLowerLegControl;
                default: return null;
            }
        }

        private static void EnsureCreated()
        {
            if (_created)
            {
                return;
            }
            for (int i = 0; i < RoleCount; i++)
            {
                string role = Roles[i].ToString();
                BasisGizmoManager.CreateSphereGizmo($"HintRaw_{role}", out _rawSphere[i], Vector3.zero, RawSphereSize, RawColor);
                BasisGizmoManager.CreateSphereGizmo($"HintBiased_{role}", out _biasedSphere[i], Vector3.zero, BiasedSphereSize, BiasedColor);
                BasisGizmoManager.CreateLineGizmo($"HintOffset_{role}", out _offsetLine[i], Vector3.zero, Vector3.zero, LineWidth, OffsetLineColor);
                BasisGizmoManager.CreateLineGizmo($"HintAxisX_{role}", out _axisX[i], Vector3.zero, Vector3.zero, LineWidth, XAxisColor);
                BasisGizmoManager.CreateLineGizmo($"HintAxisY_{role}", out _axisY[i], Vector3.zero, Vector3.zero, LineWidth, YAxisColor);
                BasisGizmoManager.CreateLineGizmo($"HintAxisZ_{role}", out _axisZ[i], Vector3.zero, Vector3.zero, LineWidth, ZAxisColor);
                BasisGizmoManager.SetGizmoActive(_rawSphere[i], false);
                BasisGizmoManager.SetGizmoActive(_biasedSphere[i], false);
                BasisGizmoManager.SetGizmoActive(_offsetLine[i], false);
                BasisGizmoManager.SetGizmoActive(_axisX[i], false);
                BasisGizmoManager.SetGizmoActive(_axisY[i], false);
                BasisGizmoManager.SetGizmoActive(_axisZ[i], false);
                _slotVisible[i] = false;
            }
            _created = true;
        }

        private static void SetSlotVisible(int i, bool active)
        {
            if (!_created || _slotVisible[i] == active)
            {
                return;
            }
            BasisGizmoManager.SetGizmoActive(_rawSphere[i], active);
            BasisGizmoManager.SetGizmoActive(_biasedSphere[i], active);
            BasisGizmoManager.SetGizmoActive(_offsetLine[i], active);
            BasisGizmoManager.SetGizmoActive(_axisX[i], active);
            BasisGizmoManager.SetGizmoActive(_axisY[i], active);
            BasisGizmoManager.SetGizmoActive(_axisZ[i], active);
            _slotVisible[i] = active;
        }

        private static void HideAll()
        {
            if (!_created)
            {
                return;
            }
            for (int i = 0; i < RoleCount; i++)
            {
                SetSlotVisible(i, false);
                DestroyLabel(i);
            }
        }

        private static void DestroyLabel(int i)
        {
            if (_label[i] > 0)
            {
                BasisGizmoManager.DestroyGizmo(_label[i]);
                _label[i] = -1;
            }
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

        private static void OnMasterToggleChanged(bool state)
        {
            if (!state)
            {
                ResetState();
            }
        }

        private static void ResetState()
        {
            for (int i = 0; i < RoleCount; i++)
            {
                _rawSphere[i] = -1;
                _biasedSphere[i] = -1;
                _offsetLine[i] = -1;
                _axisX[i] = -1;
                _axisY[i] = -1;
                _axisZ[i] = -1;
                _label[i] = -1;
                _slotVisible[i] = false;
            }
            _created = false;
        }

        private static int[] NewIds()
        {
            int[] ids = new int[RoleCount];
            for (int i = 0; i < RoleCount; i++)
            {
                ids[i] = -1;
            }
            return ids;
        }
    }
}
