using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// Bind-frame regression for the crouch body-offset (<see cref="BasisCrouchOffsetCore"/>). As the head
    /// drops the hips slide BACKWARD along the body's forward so a squat reads as sitting back. "Backward"
    /// was `HipsRot * Vector3.forward` -- the hips BONE's local +Z, which is a rig convention. On a Blender-
    /// exported rig the hips bind is rolled −90 about X, so that +Z is world-UP: the slide went vertical and,
    /// once the vertical component was stripped, collapsed to zero and the crouch silently never fired.
    ///
    /// The fix cancels the hips bind (offsetRotationHips) to get the anatomical forward. These guard that the
    /// slide is the same anatomical backward move for every bind convention, and that a degenerate/absent bind
    /// falls back to the old HipsRot-forward behaviour bit for bit (so the sweep and equivariance test, which
    /// pass identity / default, are unchanged).
    /// </summary>
    public class BasisCrouchBindFrameTests
    {
        // Same spread of hips BIND conventions as the spine bind-frame suite.
        static readonly Quaternion[] Binds =
        {
            Quaternion.identity,
            Quaternion.AngleAxis(90f, Vector3.forward),
            Quaternion.AngleAxis(-90f, Vector3.right),   // Blender export -- collapsed the slide entirely pre-fix
            Quaternion.Euler(20f, -35f, 110f),
        };

        // Anatomical scene: hips face forward (hipsAnat = identity, so HipsRot = the bind itself), head dropped
        // 0.25 m below standing while facing forward. Anatomical forward is +Z, so the hips must slide toward −Z.
        static BasisCrouchOffsetResult SolveCrouch(Quaternion bind)
        {
            BasisCrouchOffsetInput i;
            i.HeadTargetPos = new Vector3(0f, 0.65f, 0f);
            i.HipsPos = new Vector3(0f, 0.90f, 0f);
            i.HipsRot = Quaternion.identity * bind;
            i.Bind = bind;
            i.PlayerUp = Vector3.up;
            i.Factor = 1f;
            i.RestDist = 0.6f;
            BasisCrouchOffsetCore.Solve(i, out BasisCrouchOffsetResult r);
            return r;
        }

        [Test]
        public void CrouchSlide_IsInvariant_ToTheHipsBindConvention([ValueSource(nameof(Binds))] Quaternion bind)
        {
            var reference = SolveCrouch(Quaternion.identity);
            Assert.That(reference.Applied, Is.True, "reference crouch did not fire -- test would be vacuous.");

            var r = SolveCrouch(bind);
            Assert.That(r.Applied, Is.True, $"crouch did not fire on bind {bind} (the Blender-rig collapse).");
            Vector3 refOff = reference.HipsPos - new Vector3(0f, 0.90f, 0f);
            Vector3 off = r.HipsPos - new Vector3(0f, 0.90f, 0f);
            Assert.That((off - refOff).magnitude, Is.LessThan(1e-4f),
                $"crouch slid a different way on bind {bind}: {off} vs {refOff}.");
            // and it is a purely-backward (−Z), purely-horizontal slide.
            Assert.That(off.z, Is.LessThan(-1e-3f), $"crouch did not slide backward on bind {bind} ({off}).");
            Assert.That(Mathf.Abs(off.y), Is.LessThan(1e-4f), $"crouch slid vertically on bind {bind} ({off}).");
        }

        [Test]
        public void DegenerateBind_FallsBackToHipsRotForward_Unchanged()
        {
            // The sweep and the equivariance test leave Bind at its default (zero quaternion). That must mean
            // "HipsRot is already anatomical" -- the exact pre-fix behaviour -- not a divide-by-a-zero-quat.
            Quaternion hipsRot = Quaternion.Euler(0f, 25f, 0f);
            BasisCrouchOffsetInput i;
            i.HeadTargetPos = new Vector3(0f, 0.65f, 0f);
            i.HipsPos = new Vector3(0f, 0.90f, 0f);
            i.HipsRot = hipsRot;
            i.Bind = default;                    // zero quaternion
            i.PlayerUp = Vector3.up;
            i.Factor = 1f;
            i.RestDist = 0.6f;
            BasisCrouchOffsetCore.Solve(i, out BasisCrouchOffsetResult r);

            Vector3 fwd = hipsRot * Vector3.forward;
            fwd -= Vector3.up * Vector3.Dot(fwd, Vector3.up);
            Vector3 expected = i.HipsPos - fwd.normalized * (r.Crouch * i.Factor);
            Assert.That(r.Applied, Is.True);
            Assert.That((r.HipsPos - expected).magnitude, Is.LessThan(1e-4f),
                "degenerate bind did not reproduce the raw HipsRot-forward slide.");
        }
    }
}
