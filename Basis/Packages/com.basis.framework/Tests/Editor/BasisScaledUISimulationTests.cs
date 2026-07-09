using Basis.BasisUI;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.IK
{
    /// <summary>
    /// Simulation of the scaled-down pointer/UI setup from the field report: avatar at 0.01 scale,
    /// camera + raycast + UI all scaled down — "the raycast hits the right targets, but on the UI
    /// the pointer moves left and right scaled by something".
    ///
    /// The "something" was <see cref="BasisMenuMover.MIN_TMP_RENDER_SCALE"/>: the menu root scale is
    /// floored for TMP render safety, and the OLD floor (0.055) made the menu 5.5x oversized AND 5.5x
    /// too far at 0.01 avatar scale (the play-space-stable anchor distance scales by the floored root
    /// too), while the hand/camera/ray stayed true-scale. Everything was world-consistent — the ray
    /// really did hit the panel — but a hand sweep crossed the oversized panel at a 5.5x mismatched
    /// rate and the pointer line stretched to a too-distant panel ("flying away").
    ///
    /// These tests pin the fix (floor is a degenerate-value guard only, far below playable scales)
    /// and empirically verify the LineRenderer semantics the investigation had to assume: a
    /// world-space line renders at its written positions regardless of scaled ancestors — the line
    /// renderer itself was never the displaced element.
    /// </summary>
    public class BasisScaledUISimulationTests
    {
        const float AvatarScale = 0.01f; // the field-report scale

        // ----------------------------------------------------------------- menu scale floor

        [Test]
        public void MenuScale_AtScale001_IsProportional_NotFloored()
        {
            // The core regression: at 0.01 avatar scale the menu root must BE 0.01 — any floor above
            // it desynchronizes the panel from the pointer geometry. Under the old 0.055 floor this
            // returned 0.055 (the 5.5x mismatch the user measured by hand).
            Assert.That(BasisMenuMover.GetRenderSafeMenuScale(AvatarScale), Is.EqualTo(AvatarScale).Within(1e-6f),
                "the menu must stay proportional to the avatar at tiny scales — a render-safety floor above the play scale breaks pointer/UI proportionality.");
        }

        [Test]
        public void MenuScale_PointerSweepRatio_IsOneToOne()
        {
            // The user-visible symptom, as arithmetic: the pointer's travel across the panel relative
            // to the panel's own size. With the menu at GetRenderSafeMenuScale(s) and the hand at s,
            // the ratio must be exactly 1 — the old floor made it 0.055/0.01 = 5.5.
            float menuScale = BasisMenuMover.GetRenderSafeMenuScale(AvatarScale);
            float sweepRatio = menuScale / AvatarScale;
            Assert.That(sweepRatio, Is.EqualTo(1f).Within(1e-4f),
                $"hand motion maps onto the panel {sweepRatio:0.0}x off — this IS the 'moving left and right but scaled by something'.");
        }

        [Test]
        public void MenuScale_DegenerateInputs_StillGuarded()
        {
            Assert.That(BasisMenuMover.GetRenderSafeMenuScale(0f), Is.EqualTo(BasisMenuMover.MIN_TMP_RENDER_SCALE));
            Assert.That(BasisMenuMover.GetRenderSafeMenuScale(-1f), Is.EqualTo(BasisMenuMover.MIN_TMP_RENDER_SCALE));
            Assert.That(BasisMenuMover.GetRenderSafeMenuScale(float.NaN), Is.EqualTo(BasisMenuMover.MIN_TMP_RENDER_SCALE));
            Assert.That(BasisMenuMover.GetRenderSafeMenuScale(float.PositiveInfinity), Is.EqualTo(BasisMenuMover.MIN_TMP_RENDER_SCALE));
        }

        [Test]
        public void MenuScale_FloorIsBelowPlayableScales()
        {
            // 0.01 was a real play scale in the field; keep margin under it so the guard can never
            // reintroduce the mismatch for any plausible tiny avatar.
            Assert.That(BasisMenuMover.MIN_TMP_RENDER_SCALE, Is.LessThanOrEqualTo(AvatarScale * 0.5f),
                "the degenerate guard must sit well below playable avatar scales.");
        }

        // ----------------------------------------------------------------- line renderer semantics

        [Test]
        public void WorldSpaceLine_UnderScaledAncestor_RendersAtWrittenPositions()
        {
            // Empirical check of the assumption the whole investigation rested on: a useWorldSpace
            // LineRenderer ignores every ancestor transform for its positions. Built exactly like the
            // production pointer line (child of a player-root-like object), ancestor scaled to 0.01.
            GameObject parent = new GameObject("ScaledPlayerRoot");
            GameObject lineGO = new GameObject("PointerLine");
            try
            {
                parent.transform.position = new Vector3(3f, 1f, -2f);
                parent.transform.rotation = Quaternion.Euler(0f, 47f, 0f);
                parent.transform.localScale = Vector3.one * AvatarScale;
                lineGO.transform.SetParent(parent.transform);
                lineGO.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

                LineRenderer line = lineGO.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                Vector3 start = new Vector3(10f, 2f, 5f);
                Vector3 end = new Vector3(10.5f, 2.1f, 5.4f);
                line.SetPosition(0, start);
                line.SetPosition(1, end);
                line.startWidth = 0.001f;
                line.endWidth = 0.001f;

                Vector3 expectedCenter = (start + end) * 0.5f;
                Assert.That((line.bounds.center - expectedCenter).magnitude, Is.LessThan(0.01f),
                    $"world-space line rendered at {line.bounds.center}, written midpoint {expectedCenter} — a scaled ancestor displaced it.");
            }
            finally
            {
                Object.DestroyImmediate(lineGO);
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void LocalSpaceLine_UnderScaledAncestor_ShowsTheDisplacedSignature()
        {
            // The failure shape a flipped useWorldSpace WOULD produce, kept as a signature reference:
            // positions get crushed by the 0.01 ancestor and dragged to the parent's pose. If a future
            // report matches this signature, the flag regressed somewhere.
            GameObject parent = new GameObject("ScaledPlayerRoot");
            GameObject lineGO = new GameObject("PointerLine");
            try
            {
                parent.transform.position = new Vector3(3f, 1f, -2f);
                parent.transform.localScale = Vector3.one * AvatarScale;
                lineGO.transform.SetParent(parent.transform);
                lineGO.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

                LineRenderer line = lineGO.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.positionCount = 2;
                Vector3 start = new Vector3(10f, 2f, 5f);
                Vector3 end = new Vector3(10.5f, 2.1f, 5.4f);
                line.SetPosition(0, start);
                line.SetPosition(1, end);

                Vector3 writtenMid = (start + end) * 0.5f;
                Assert.That((line.bounds.center - writtenMid).magnitude, Is.GreaterThan(1f),
                    "local-space line under a scaled ancestor should be visibly displaced from the written positions — if this stops failing-to-match, the signature test needs rework.");
            }
            finally
            {
                Object.DestroyImmediate(lineGO);
                Object.DestroyImmediate(parent);
            }
        }

        // ----------------------------------------------------------------- pointer sweep end-to-end

        [Test]
        public void PointerSweep_AtScale001_HitTracksHandOneToOne_OnProportionalPanel()
        {
            // Full geometric simulation at 0.01: hand at body height sweeps laterally in real metres;
            // device coords scale by DeviceScale = 0.01; the ray (forward) hits a panel placed the way
            // the menu mover places it — at GroupOffset distance x the menu root scale. With the
            // PROPORTIONAL root scale the hit sweeps exactly as far as the scaled hand does (1:1 in
            // body-relative terms). Substituting the old 0.055 floor changes the panel distance and
            // (crucially) its SIZE, giving the 5.5x relative sweep mismatch — asserted at the end.
            float deviceScale = AvatarScale;
            float menuScale = BasisMenuMover.GetRenderSafeMenuScale(AvatarScale);
            const float groupOffsetZ = 0.5f;    // default GroupOffset local Z from the mover
            const float handSweepMetres = 0.30f; // real-world lateral hand travel

            // Panel plane placed at the anchored distance for this menu scale.
            float panelDistance = groupOffsetZ * menuScale;

            // Ray origins for the sweep endpoints (device space x DeviceScale), pointing forward.
            Vector3 originA = new Vector3(-0.5f * handSweepMetres * deviceScale, 0f, 0f);
            Vector3 originB = new Vector3(0.5f * handSweepMetres * deviceScale, 0f, 0f);

            // Forward rays onto the z = panelDistance plane.
            Vector3 hitA = new Vector3(originA.x, originA.y, panelDistance);
            Vector3 hitB = new Vector3(originB.x, originB.y, panelDistance);

            // Sweep across the panel, measured in PANEL-RELATIVE units (panel content spans
            // proportional to its root scale).
            float panelRelativeSweep = (hitB.x - hitA.x) / menuScale;
            float bodyRelativeSweep = handSweepMetres * deviceScale / AvatarScale; // = handSweepMetres

            Assert.That(panelRelativeSweep / bodyRelativeSweep, Is.EqualTo(1f).Within(1e-4f),
                "the pointer must cross the panel at the same rate the hand crosses the body's space.");

            // Document the old failure numerically: with the 0.055 floor the same geometry yields a
            // 5.5x mismatch — the exact 'scaled by something' the user reported.
            const float oldFloor = 0.055f;
            float oldMismatch = (handSweepMetres * deviceScale / oldFloor) / bodyRelativeSweep;
            Assert.That(oldMismatch, Is.EqualTo(AvatarScale / oldFloor).Within(1e-4f));
            Assert.That(Mathf.Abs(1f - oldMismatch), Is.GreaterThan(0.5f),
                "the old floor produced a large sweep mismatch at 0.01 — that is the bug this suite locks out.");
        }
    }
}
