using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// Guards the FULL-CIRCLE swivel search in <see cref="BasisElbowProtectCore"/>.
    ///
    /// ================================================================================================
    /// WHAT THIS IS FOR. The protect used to sweep only the arc from the natural pole to outDir. That is
    /// a DOMAIN limit, not a resolution limit: measured against the live arm solve over 11 759 reachable
    /// hand targets, 39.1% of the poses it gave up on had a clearing elbow sitting on the same circle,
    /// just outside the sampled arc -- 68.4% past outDir and 31.6% on the inward side, which was never
    /// looked at at all. Widening the domain took the cleared fraction from 0.6708 to 0.7995 at the SAME
    /// sample count, because the feasible arcs are wide (median widest arc 67 deg, only 0.6% narrower
    /// than the old 7.5 deg spacing).
    ///
    /// THE DOMAIN AND THE SELECTION RULE ONLY WORK AS A PAIR. Full circle with the old "smallest
    /// swing wins" rule is a REGRESSION -- measured over 30 hand slides it popped 145 deg in a single
    /// frame, 25 times, against 54 deg / 4 times for the shipped one-sided sweep -- because a wider
    /// domain makes the feasible set genuinely disconnected and a stateless argmax hops between
    /// components as they open and close. Anchoring on last frame's answer removes that.
    ///
    /// BUT THE DOMAIN AND THE ANCHOR ARE INDEPENDENT KNOBS, AND CONFLATING THEM MEASURED BACKWARDS.
    /// They were originally one flag, and because an absent anchor defaults to ZERO -- a pull toward the
    /// natural pole, not a neutral start -- the 354k-point sweep read the cleared fraction going the WRONG
    /// WAY, 0.3606 -> 0.3552, with mean swing FALLING 12.68 -> 11.70 deg. FullCircle widens the search;
    /// HasPrevSwivel says an established previous choice exists. Keep them separate.
    ///
    /// So there are two things worth gating, and they pull in opposite directions: the search has to
    /// find MORE (Clears_StrictlyMorePoses_ThanTheOneSidedSweep) and it has to move LESS
    /// (TheChosenSwivel_DoesNotHop_AsTheHandSlides). A fix that only does one of them is the bug.
    /// ================================================================================================
    /// </summary>
    public class BasisElbowFullCircleTests
    {
        static readonly Vector3 k_Hips = new Vector3(0f, 0.95f, 0f);
        static readonly Vector3 k_Spine = new Vector3(0f, 1.05f, 0f);
        static readonly Vector3 k_Chest = new Vector3(0f, 1.20f, 0f);
        static readonly Vector3 k_Neck = new Vector3(0f, 1.42f, 0f);
        static readonly Vector3 k_Shoulder = new Vector3(0.17f, 1.38f, 0f);

        const float k_Upper = 0.28f;
        const float k_Lower = 0.26f;

        /// <summary>Two-bone elbow placement toward a natural down-and-out pole. Deliberately simple and
        /// local: this fixture is testing the SEARCH, not the arm solver, and a rig the test owns cannot
        /// drift underneath it.</summary>
        static bool SolvedElbow(Vector3 hand, out Vector3 elbow)
        {
            elbow = Vector3.zero;
            Vector3 sh = hand - k_Shoulder;
            float d = sh.magnitude;
            if (d <= Mathf.Abs(k_Upper - k_Lower) + 1e-4f || d >= k_Upper + k_Lower - 1e-4f) return false;

            Vector3 axis = sh / d;
            float cosA = (k_Upper * k_Upper + d * d - k_Lower * k_Lower) / (2f * k_Upper * d);
            float a = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f));

            Vector3 pole = new Vector3(0.35f, -1f, 0f).normalized;
            Vector3 perp = pole - axis * Vector3.Dot(pole, axis);
            if (perp.sqrMagnitude <= 1e-8f) perp = Vector3.Cross(axis, Vector3.forward);
            if (perp.sqrMagnitude <= 1e-8f) perp = Vector3.Cross(axis, Vector3.right);
            perp = perp.normalized;

            elbow = k_Shoulder + (axis * Mathf.Cos(a) + perp * Mathf.Sin(a)) * k_Upper;
            return true;
        }

        static bool Input(Vector3 hand, out BasisElbowProtectInput i)
        {
            i = default;
            if (!SolvedElbow(hand, out Vector3 elbow)) return false;
            i.Shoulder = k_Shoulder;
            i.Elbow = elbow;
            i.Hand = hand;
            i.HasHips = true;
            i.HasSpine = true;
            i.HipsPos = k_Hips;
            i.SpinePos = k_Spine;
            i.ChestPos = k_Chest;
            i.NeckPos = k_Neck;
            i.ChestRadiusBase = 0.055f;
            i.CollisionSkin = 0.025f;
            i.HandRadius = 0.040f;
            i.HandSkin = 0f;
            i.PlayerUp = Vector3.up;
            i.BodyRight = new Vector3(0.34f, 0f, 0f);
            return true;
        }

        /// <summary>A grid of hand targets across and in front of the torso -- where the protect engages.</summary>
        static System.Collections.Generic.List<Vector3> Corpus()
        {
            var hands = new System.Collections.Generic.List<Vector3>();
            for (float x = -0.28f; x <= 0.34f; x += 0.03f)
                for (float y = 0.98f; y <= 1.52f; y += 0.03f)
                    for (float z = -0.04f; z <= 0.28f; z += 0.04f)
                        hands.Add(new Vector3(x, y, z));
            return hands;
        }

        /// <summary>The default struct -- FullCircle false -- must keep the original one-sided sweep.
        /// BlendUsed is the swivel as a fraction of thetaOut, so the legacy domain is exactly [0, 1];
        /// anything outside it means the full-circle branch ran when it was not asked to.</summary>
        [Test]
        public void WithoutAnAnchor_TheSweepStaysOneSided()
        {
            foreach (Vector3 h in Corpus())
            {
                if (!Input(h, out BasisElbowProtectInput i)) continue;
                BasisElbowProtectCore.Solve(i, out BasisElbowProtectResult r);
                if (!r.Engaged) continue;

                Assert.That(r.BlendUsed, Is.InRange(-1e-4f, 1.0001f),
                    $"legacy path left its arc at hand {h}: BlendUsed {r.BlendUsed}");
            }
        }

        /// <summary>THE HEADLINE. The whole point of the wider domain is that it clears poses the
        /// one-sided sweep gives up on (CollisionState 2). Asserted as a strict inequality over the
        /// corpus rather than on a hardcoded pose, so it cannot rot into a tautology if the rig or the
        /// collider dimensions move.</summary>
        [Test]
        public void Clears_StrictlyMorePoses_ThanTheOneSidedSweep()
        {
            int engaged = 0, legacyCleared = 0, fullCleared = 0, rescued = 0;

            foreach (Vector3 h in Corpus())
            {
                if (!Input(h, out BasisElbowProtectInput i)) continue;

                BasisElbowProtectCore.Solve(i, out BasisElbowProtectResult legacy);
                if (legacy.CollisionState == 0) continue;   // no real penetration: nothing to clear
                engaged++;
                if (legacy.CollisionState == 1) legacyCleared++;

                BasisElbowProtectInput f = i;
                f.FullCircle = true;   // domain only; no history on a single-pose comparison
                BasisElbowProtectCore.Solve(f, out BasisElbowProtectResult full);
                if (full.CollisionState == 1) fullCleared++;
                if (legacy.CollisionState == 2 && full.CollisionState == 1) rescued++;
            }

            Assert.That(engaged, Is.GreaterThan(50), "corpus does not engage the protect often enough to be a test");
            Assert.That(rescued, Is.GreaterThan(0),
                "ANTI-TAUTOLOGY: not one pose the one-sided sweep failed was rescued by the full circle");
            Assert.That(fullCleared, Is.GreaterThan(legacyCleared),
                $"full circle cleared {fullCleared} of {engaged}, one-sided cleared {legacyCleared}");
        }

        /// <summary>THE COUNTERWEIGHT. A wider domain with a stateless rule hops between disconnected
        /// feasible arcs -- measured at 145 deg in one frame. With the anchor fed forward the chosen
        /// swivel has to move smoothly as the hand slides. The bound is generous next to that 145 deg
        /// but far tighter than a component hop, and it is above the 7.5 deg grid quantisation.</summary>
        [Test]
        public void TheChosenSwivel_DoesNotHop_AsTheHandSlides()
        {
            const float k_MaxStepDeg = 25f;

            foreach (float x in new[] { 0.00f, 0.04f, 0.08f })
            {
                foreach (float z in new[] { 0.10f, 0.13f })
                {
                    float prev = 0f;
                    bool have = false;

                    for (int s = 0; s < 500; s++)
                    {
                        Vector3 h = new Vector3(x, 1.05f + s * 0.0005f, z);
                        if (!Input(h, out BasisElbowProtectInput i)) { have = false; continue; }

                        i.FullCircle = true;
                        i.HasPrevSwivel = have;
                        i.PrevSwivelDeg = prev;
                        BasisElbowProtectCore.Solve(i, out BasisElbowProtectResult r);

                        float now = r.Engaged ? r.ChosenSwivelDeg : 0f;
                        if (have)
                        {
                            float d = Mathf.Abs(now - prev);
                            if (d > 180f) d = 360f - d;
                            Assert.That(d, Is.LessThan(k_MaxStepDeg),
                                $"swivel hopped {d:F1} deg on a 0.5 mm hand step at {h}");
                        }
                        prev = now;
                        have = true;
                    }
                }
            }
        }

        /// <summary>A disengaged frame must report zero, not a stale angle. The caller feeds this straight
        /// back in as the next frame's anchor, so a stale value would re-anchor the search on an arc the
        /// elbow is no longer anywhere near.</summary>
        [Test]
        public void NotEngaged_ReportsZeroSwivel()
        {
            Vector3 farOut = new Vector3(0.50f, 1.38f, 0f);   // arm hanging clear of the torso
            Assert.That(Input(farOut, out BasisElbowProtectInput i), Is.True);
            i.FullCircle = true;
            i.HasPrevSwivel = true;
            i.PrevSwivelDeg = 90f;                            // a large stale anchor

            BasisElbowProtectCore.Solve(i, out BasisElbowProtectResult r);
            Assume.That(r.Engaged, Is.False, "pose was meant to be clear of the torso");
            Assert.That(r.ChosenSwivelDeg, Is.EqualTo(0f));
        }
    }
}

