using System.Collections.Generic;
using Basis.IK;
using Basis.Scripts.Drivers;
using Unity.Collections;
using UnityEngine;

namespace Basis.Scripts.Debugging
{
    /// <summary>
    /// Main-thread half of the IK solve gizmos. <see cref="Prepare"/> arms the recorder on the job
    /// just before it is scheduled; <see cref="Drain"/> replays whatever the solve queued into
    /// pooled BasisGizmoManager slots once the solve has been joined
    /// (BasisLocalRigDriver.CompleteIKSolve, alongside the leg diagnostics read).
    /// <para>
    /// Nothing here knows what any individual draw means: the queue carries lines, spheres and
    /// labels only, so a new visualization added inside the Burst solve needs no counterpart on
    /// this side. Slots are pooled by index and reused frame to frame; the tail left over from a
    /// busier frame is hidden rather than destroyed.
    /// </para>
    /// </summary>
    public static class BasisIKSolveGizmos
    {
        const float LineWidthBase = 0.003f;
        const float PointSizeBase = 0.014f;
        const float AxisLengthBase = 0.06f;
        const float LabelScaleBase = 0.016f;

        const int IdleCapacity = 1;

        static readonly List<int> _lineIds = new List<int>();
        static readonly List<int> _sphereIds = new List<int>();
        static readonly List<int> _labelIds = new List<int>();
        static readonly List<FixedString64Bytes> _labelText = new List<FixedString64Bytes>();
        static readonly List<string> _labelStrings = new List<string>();

        static int _linesShown;
        static int _spheresShown;
        static int _labelsShown;

        static bool _registered;
        static bool _overflowWarned;

        public static int LineCount => _linesShown;
        public static int SphereCount => _spheresShown;
        public static int LabelCount => _labelsShown;

        /// <summary>
        /// Pushes this frame's stage mask, colours and sizes onto the job and clears last frame's
        /// queue. Must run immediately before the solve is scheduled.
        /// <para>
        /// The queue is sized to the mask: full capacity while any stage is on, and a one-element
        /// stub once they are all off. It is never left unallocated — the job safety system rejects
        /// a scheduled job holding an unconstructed container, whatever the job does with it — so
        /// "off" costs a stub allocation rather than nothing at all.
        /// </para>
        /// </summary>
        public static void Prepare(ref BasisEerieMovement job)
        {
            EnsureMasterToggleHook();

            int mask = BasisIKSolveGizmoStages.Mask();
            bool wantFull = mask != 0;
            bool isFull = job.gizmos.IsCreated
                && job.gizmos.Draws.Capacity >= BasisIKSolveGizmoStages.DrawCapacity;

            if (!job.gizmos.IsCreated || isFull != wantFull)
            {
                job.gizmos.Create(
                    wantFull ? BasisIKSolveGizmoStages.DrawCapacity : IdleCapacity,
                    wantFull ? BasisIKSolveGizmoStages.LabelCapacity : IdleCapacity);
                job.gizmos.StageColors.Length = BasisIKGizmoRecorder.StageCount;
            }

            job.gizmos.StageMask = mask;
            job.gizmos.Clear();

            if (!wantFull)
            {
                job.gizmos.WantLabels = false;
                return;
            }

            for (int i = 0; i < BasisIKSolveGizmoStages.All.Length; i++)
            {
                int index = BasisIKGizmoRecorder.StageIndex(BasisIKSolveGizmoStages.All[i].Stage);
                if ((uint)index < (uint)job.gizmos.StageColors.Length)
                {
                    job.gizmos.StageColors[index] = BasisIKSolveGizmoStages.PackedColor(i);
                }
            }

            float scale = WorldScale();
            job.gizmos.LineWidth = LineWidthBase * scale;
            job.gizmos.PointSize = PointSizeBase * scale;
            job.gizmos.AxisLength = AxisLengthBase * scale;
            job.gizmos.WantLabels = BasisIKSolveGizmoStages.Labels.RawValue;
        }

        /// <summary>
        /// Replays the joined solve's queue. Safe to call when the solve never ran or recorded
        /// nothing — it simply hides whatever the last active frame left on screen.
        /// </summary>
        public static void Drain(ref BasisEerieMovement job, Vector3 cameraPosition)
        {
            EnsureMasterToggleHook();

            if (!job.gizmos.IsCreated || job.gizmos.StageMask == 0)
            {
                Hide();
                return;
            }

            NativeList<BasisIKGizmoDraw> draws = job.gizmos.Draws;
            int lines = 0;
            int spheres = 0;

            for (int i = 0; i < draws.Length; i++)
            {
                BasisIKGizmoDraw draw = draws[i];
                Color32 color = Unpack(draw.Color);
                if (draw.Kind == BasisIKGizmoKind.Line)
                {
                    BasisGizmoManager.UpdateLineGizmo(RentLine(lines++), draw.A, draw.B, draw.Size, color);
                }
                else
                {
                    BasisGizmoManager.UpdateSphereGizmo(RentSphere(spheres++), draw.A, draw.Size, color);
                }
            }

            HideTail(_lineIds, lines, ref _linesShown);
            HideTail(_sphereIds, spheres, ref _spheresShown);

            DrainLabels(ref job, cameraPosition);
            ReportOverflow(ref job);
        }

        static void DrainLabels(ref BasisEerieMovement job, Vector3 cameraPosition)
        {
            if (!job.gizmos.Labels.IsCreated || !BasisIKSolveGizmoStages.Labels.RawValue)
            {
                HideTail(_labelIds, 0, ref _labelsShown);
                return;
            }

            float labelScale = LabelScaleBase * WorldScale();
            NativeList<BasisIKGizmoLabel> labels = job.gizmos.Labels;
            for (int i = 0; i < labels.Length; i++)
            {
                BasisIKGizmoLabel label = labels[i];
                Color32 color = Unpack(label.Color);
                int id = RentLabel(i, label.Position, color);
                if (id <= 0)
                {
                    continue;
                }

                // FixedString.ToString allocates, so the managed string is cached per slot and only
                // re-materialized when the recorded text actually changed. Slots cycle through
                // different labels as the solve's draw order shifts, hence the per-slot compare.
                if (_labelStrings[i] == null || !_labelText[i].Equals(label.Text))
                {
                    _labelText[i] = label.Text;
                    _labelStrings[i] = label.Text.ToString();
                }

                Quaternion rotation = BasisGizmoManager.BillboardRotation(label.Position, cameraPosition);
                BasisGizmoManager.UpdateTextGizmo(id, label.Position, rotation, labelScale, _labelStrings[i], color);
            }

            HideTail(_labelIds, labels.Length, ref _labelsShown);
        }

        static void ReportOverflow(ref BasisEerieMovement job)
        {
            if (_overflowWarned || !job.gizmos.Overflow.IsCreated)
            {
                return;
            }
            int droppedDraws = job.gizmos.Overflow[BasisIKGizmoRecorder.OverflowDraws];
            int droppedLabels = job.gizmos.Overflow[BasisIKGizmoRecorder.OverflowLabels];
            if (droppedDraws == 0 && droppedLabels == 0)
            {
                return;
            }
            _overflowWarned = true;
            BasisDebug.LogWarning(
                $"IK solve gizmos dropped {droppedDraws} draws and {droppedLabels} labels this frame " +
                $"(caps {BasisIKSolveGizmoStages.DrawCapacity}/{BasisIKSolveGizmoStages.LabelCapacity}). " +
                "The picture is incomplete; disable a stage or raise the caps.", BasisDebug.LogTag.Gizmo);
        }

        static float WorldScale()
        {
            float avatar = BasisHeightDriver.ScaledToMatchValue;
            if (!(avatar > 0f))
            {
                avatar = 1f;
            }
            float user = Mathf.Clamp(BasisIKSolveGizmoStages.Scale.RawValue,
                BasisIKSolveGizmoStages.ScaleMin, BasisIKSolveGizmoStages.ScaleMax);
            return avatar * user;
        }

        static Color32 Unpack(uint packed)
        {
            return new Color32(
                BasisIKGizmoPalette.R(packed),
                BasisIKGizmoPalette.G(packed),
                BasisIKGizmoPalette.B(packed),
                BasisIKGizmoPalette.A(packed));
        }

        static int RentLine(int index)
        {
            while (_lineIds.Count <= index)
            {
                BasisGizmoManager.CreateLineGizmo($"IKSolve_Line{_lineIds.Count}", out int created,
                    Vector3.zero, Vector3.zero, LineWidthBase, Color.white);
                _lineIds.Add(created);
            }
            if (index >= _linesShown)
            {
                BasisGizmoManager.SetGizmoActive(_lineIds[index], true);
                _linesShown = index + 1;
            }
            return _lineIds[index];
        }

        static int RentSphere(int index)
        {
            while (_sphereIds.Count <= index)
            {
                BasisGizmoManager.CreateSphereGizmo($"IKSolve_Point{_sphereIds.Count}", out int created,
                    Vector3.zero, PointSizeBase, Color.white);
                _sphereIds.Add(created);
            }
            if (index >= _spheresShown)
            {
                BasisGizmoManager.SetGizmoActive(_sphereIds[index], true);
                _spheresShown = index + 1;
            }
            return _sphereIds[index];
        }

        static int RentLabel(int index, Vector3 position, Color color)
        {
            while (_labelIds.Count <= index)
            {
                if (!BasisGizmoManager.CreateTextGizmo($"IKSolve_Label{_labelIds.Count}", out int created,
                        position, string.Empty, color))
                {
                    return -1;
                }
                _labelIds.Add(created);
                _labelText.Add(default);
                _labelStrings.Add(null);
            }
            if (index >= _labelsShown)
            {
                BasisGizmoManager.SetGizmoActive(_labelIds[index], true);
                _labelsShown = index + 1;
            }
            return _labelIds[index];
        }

        static void HideTail(List<int> ids, int used, ref int shown)
        {
            for (int i = used; i < shown && i < ids.Count; i++)
            {
                BasisGizmoManager.SetGizmoActive(ids[i], false);
            }
            shown = Mathf.Min(used, ids.Count);
        }

        /// <summary>Clears whatever the last active frame drew, without tearing the pool down.</summary>
        public static void Hide()
        {
            HideTail(_lineIds, 0, ref _linesShown);
            HideTail(_sphereIds, 0, ref _spheresShown);
            HideTail(_labelIds, 0, ref _labelsShown);
        }

        public static void Shutdown()
        {
            for (int i = 0; i < _lineIds.Count; i++)
            {
                BasisGizmoManager.DestroyGizmo(_lineIds[i]);
            }
            for (int i = 0; i < _sphereIds.Count; i++)
            {
                BasisGizmoManager.DestroyGizmo(_sphereIds[i]);
            }
            for (int i = 0; i < _labelIds.Count; i++)
            {
                BasisGizmoManager.DestroyGizmo(_labelIds[i]);
            }
            ResetState();
        }

        static void EnsureMasterToggleHook()
        {
            if (_registered)
            {
                return;
            }
            BasisGizmoManager.OnUseGizmosChanged += OnMasterToggleChanged;
            _registered = true;
        }

        static void OnMasterToggleChanged(bool state)
        {
            if (!state)
            {
                ResetState();
            }
        }

        static void ResetState()
        {
            _lineIds.Clear();
            _sphereIds.Clear();
            _labelIds.Clear();
            _labelText.Clear();
            _labelStrings.Clear();
            _linesShown = 0;
            _spheresShown = 0;
            _labelsShown = 0;
            _overflowWarned = false;
        }
    }
}
