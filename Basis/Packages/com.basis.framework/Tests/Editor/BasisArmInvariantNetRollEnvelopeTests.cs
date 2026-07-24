using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// THE AXIAL ROLL CHAIN: CLAVICLE -> HUMERUS -> FOREARM -> HAND. EVERY LINK, BOUNDED, SEPARATELY.
    ///
    /// ================================================================================================
    /// THE CLASS THIS CATCHES: AN UNBOUNDED DEGREE OF FREEDOM. Humeral axial rotation was bounded by
    /// NOTHING -- measured in Unity, rolling an elbow tracker rolled the humerus 1:1 through a full 360
    /// inside a clavicle that never moved, because BasisShoulderSolveCore is direction-driven and a pure
    /// roll changes no direction, so the girdle contributed exactly 0.0 deg. Every neighbouring DOF was
    /// bounded -- elbow height, elbow flexion, forearm roll, wrist relief, clavicle-vs-chest -- and nobody
    /// noticed that one was not, because NOTHING ASSERTED THE TOTAL.
    ///
    /// The lesson generalises past the humerus: the way an unbounded DOF hides is that each neighbouring
    /// bound is asserted on its own, so the chain is never added up. This file adds it up. It measures each
    /// link independently, in the rig's OWN bind relationship (so it is a property of the skeleton, not of
    /// whatever animation clip is playing), and it holds the sum to a human total.
    ///
    /// THE HUMAN NUMBERS, and where each comes from:
    ///   humerus vs clavicle .... 150 deg, this repo's own HumeralTwistHardDeg -- deliberately looser than a
    ///                            true glenohumeral arc (clinically ~48/71/40 at 45/90/135 of abduction)
    ///                            because this rig's girdle does not move, so the humerus is absorbing the
    ///                            scapula's share as well.
    ///   forearm vs humerus ..... ~85 deg each way. Pronation/supination is a FOREARM motion (the radius
    ///                            crossing the ulna); this repo calls the comfort band 80.
    ///   hand vs forearm ........ ~0. THE WRIST HAS NO AXIAL DEGREE OF FREEDOM. Radiocarpal motion is
    ///                            flexion/extension and radial/ulnar deviation; what feels like "twisting
    ///                            your wrist" is the forearm. A generous 15 deg is allowed here for soft
    ///                            tissue and rig slop.
    /// ================================================================================================
    /// </summary>
    public class BasisArmInvariantNetRollEnvelopeTests
    {
        const float HumerusLimitDeg = BasisArmSolveCore.HumeralTwistHardDeg;   // 150
        const float ForearmLimitDeg = 100f;                                    // 80 comfort band + slop
        const float WristLimitDeg = 15f;                                       // the wrist has no axial DOF
        /// <summary>What the NO-TRACKER forearm stage's seam cap analytically leaves the wrist:
        /// residual = demand - want, which peaks at 50 deg where the cap starts binding (|demand| = 130)
        /// and returns to zero at the seam. Plus slop.</summary>
        const float NoTrackerWristCeilingDeg = 60f;
        /// <summary>The tracker path saturates its forearm roll against the ANIMATED pose at 90/120, so
        /// its absolute pronation against the bind can run further than the comfort band. Reported
        /// rather than derived, and generous.</summary>
        const float TrackerForearmCeilingDeg = 180f;
        const float ChainLimitDeg = HumerusLimitDeg + ForearmLimitDeg + WristLimitDeg;

        static Vector3 Dir(float azDeg, float elDeg)
        {
            float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az)).normalized;
        }

        // ------------------------------------------------------------------ the three links

        /// <summary>Humeral axial rotation relative to the CLAVICLE, read off the SOLVED arm. Mirrors the
        /// core's own reference construction -- it is the definition of the bounded quantity -- and works on
        /// an unguarded control too, which is the point: with the bind fields declined the core reports
        /// nothing at all, and the control is where the runaway shows.</summary>
        static float HumerusRollDeg(in BasisArmSolveInput i, Vector3 elbow, Quaternion rootRot,
                                    Quaternion bindHumRot, Vector3 bindHumDir, Vector3 refAxis,
                                    Quaternion clavLive, Quaternion clavBind)
        {
            Vector3 liveDir = elbow - i.Shoulder;
            if (liveDir.sqrMagnitude < 1e-10f) return 0f;
            liveDir = liveDir.normalized;

            Quaternion carry = clavLive * Quaternion.Inverse(clavBind);
            Quaternion restHum = carry * bindHumRot;
            Vector3 restDir = (carry * bindHumDir).normalized;

            Quaternion swingMin = BasisQuaternionExt.FromToRotation(restDir, liveDir);
            Vector3 refPerp = (swingMin * restHum) * refAxis;
            Vector3 livePerp = rootRot * refAxis;
            refPerp -= liveDir * Vector3.Dot(refPerp, liveDir);
            livePerp -= liveDir * Vector3.Dot(livePerp, liveDir);
            if (refPerp.sqrMagnitude < 1e-10f || livePerp.sqrMagnitude < 1e-10f) return 0f;
            return Vector3.SignedAngle(refPerp, livePerp, liveDir);
        }

        /// <summary>Forearm pronation relative to the humerus, defined by the RIG'S OWN BIND relationship --
        /// "the forearm sits relative to the humerus exactly as it does in the rig's bind pose", carried onto
        /// the solved humerus. A property of the skeleton, so it cannot rot the way a fitted model can, and
        /// it is the same neutral the no-tracker forearm roll is written against.</summary>
        static float ForearmRollDeg(Quaternion rootRot, Quaternion midRot, Vector3 foreAxis,
                                    Quaternion bindHumRot, Quaternion bindLowerRot)
        {
            Quaternion neutral = rootRot * (Quaternion.Inverse(bindHumRot) * bindLowerRot);
            return BasisArmNet.TwistDeg(midRot * Quaternion.Inverse(neutral), foreAxis);
        }

        /// <summary>Hand axial roll relative to the forearm. The rig's bind hand IS its bind forearm here
        /// (zero relative roll at bind), so this is simply the hand's twist about the forearm's long axis.</summary>
        static float WristRollDeg(Quaternion midRot, Quaternion tipRot, Vector3 foreAxis)
            => BasisArmNet.TwistDeg(tipRot * Quaternion.Inverse(midRot), foreAxis);

        // ------------------------------------------------------------------ the sweep

        struct Row
        {
            public float Hum, Fore, Wrist, Chain;
            /// <summary>The core's own PRE-guard twist. The guard's envelope is a function of it -- see the
            /// seam note -- so the bound cannot be checked without it.</summary>
            public float RawTwist;
            /// <summary>What the core SAYS it applied, so a disagreement with the measurement is visible.</summary>
            public float ReportedRoll, Relief, Demand, Want;
            public float HandRoll, TrackerRoll, HintAz, Reach, El;
            public bool Tracker;
        }

        static List<Row> Sweep(bool feedTwistBind, bool feedLowerBind)
        {
            var rows = new List<Row>();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            foreach (float el in new[] { -60f, -25f, 15f })
            foreach (float reach in new[] { 0.75f, 0.90f, 0.98f })
            foreach (bool tracker in new[] { false, true })
            for (int hr = 0; hr <= 360; hr += 5)
            for (int aux = 0; aux <= 300; aux += 60)
            {
                BasisArmNet.Spec s = BasisArmNet.Default(rig);
                s.TargetDir = Dir(30f, el);
                s.Reach = reach;
                s.HandRollDeg = hr;
                s.HintMode = tracker ? BasisArmNet.HintTracker : BasisArmNet.HintModel;
                s.HintAzimuthDeg = aux;
                s.HintRhoMin = 0.015f;
                s.TrackerRollDeg = tracker ? aux : float.NaN;
                s.RefPerp = Vector3.up;
                s.FeedTwistBind = feedTwistBind;
                s.FeedLowerBind = feedLowerBind;
                s.WristBound = feedLowerBind;
                s.FeedTip = true;
                // The ANIMATED forearm's own roll is swept too. With the bind fed the solve must not
                // inherit it at all; with the bind DECLINED it inherits it 1:1, which is what makes the
                // control here reach a full principal turn instead of sitting at one arbitrary value.
                s.AnimForeRollDeg = 35f + aux;
                s.AnimHandRollDeg = -20f;

                BasisArmSolveInput i = BasisArmNet.Build(s);
                BasisArmNet.Solve(i, out BasisArmSolveResult r);
                BasisArmNet.StreamCompose(i, r, out Vector3 elbow, out Vector3 hand, out Quaternion root, out Quaternion mid);

                Vector3 foreAxis = hand - elbow;
                if (foreAxis.sqrMagnitude < 1e-10f) continue;
                foreAxis = foreAxis.normalized;

                Row row = default;
                row.Hum = HumerusRollDeg(i, elbow, root, rig.BindHumerusRot, rig.BindHumerusDir,
                                         rig.BindHumerusRefAxis, BasisArmNet.ClavBind, BasisArmNet.ClavBind);
                row.Fore = ForearmRollDeg(root, mid, foreAxis, rig.BindHumerusRot, rig.BindLowerArmRot);
                row.Wrist = WristRollDeg(mid, r.TipRotation, foreAxis);
                row.Chain = Mathf.Abs(row.Hum) + Mathf.Abs(row.Fore) + Mathf.Abs(row.Wrist);
                row.RawTwist = r.HumeralTwistDeg;
                row.ReportedRoll = r.ForearmRollDeg;
                row.Relief = r.WristReliefDeg;
                // What the no-tracker stage says it is aiming at, recomputed here: the hand's roll demand
                // against the SAME bind neutral, clamped to the comfort band. If the measured pronation is
                // not this, the stage is not landing on its own target.
                Quaternion neutralFore = root * (Quaternion.Inverse(rig.BindHumerusRot) * rig.BindLowerArmRot);
                // ⚠️ The DEMAND is the RAW CONTROLLER, not r.TipRotation. Since the wrist axial bound landed,
                // TipRotation is the BOUNDED hand -- so measuring demand from it compares the forearm against a
                // yardstick that already moved, reporting an apparent 38.5 deg forearm excess where the true
                // figure against the raw controller is 0.0. The yardstick moved, not the forearm.
                row.Demand = BasisArmNet.TwistDeg((i.TargetRotation * i.TargetOffset) * Quaternion.Inverse(neutralFore), foreAxis);
                row.Want = Mathf.Clamp(row.Demand, -BasisArmSolveCore.WristRollComfortDeg, BasisArmSolveCore.WristRollComfortDeg);
                row.HandRoll = hr; row.TrackerRoll = tracker ? aux : float.NaN; row.HintAz = aux;
                row.Reach = reach; row.El = el; row.Tracker = tracker;
                rows.Add(row);
            }
            return rows;
        }

        static void Worst(List<Row> rows, bool tracker, out float hum, out float fore, out float wrist, out float chain)
        {
            hum = fore = wrist = chain = 0f;
            foreach (Row r in rows)
            {
                if (r.Tracker != tracker) continue;
                hum = Mathf.Max(hum, Mathf.Abs(r.Hum));
                fore = Mathf.Max(fore, Mathf.Abs(r.Fore));
                wrist = Mathf.Max(wrist, Mathf.Abs(r.Wrist));
                chain = Mathf.Max(chain, r.Chain);
            }
        }

        // ============================================================================================
        // 1. THE HUMERUS AND THE FOREARM
        // ============================================================================================

        /// <summary>
        /// The two links that ARE bounded, held to their bounds -- with the unguarded control on the same
        /// sweep, so a pass is the guard working and not the sweep having gone quiet.
        ///
        /// ⚠️ THE HUMERAL BOUND RELAXES IN THE LAST ~15 DEG BEFORE THE SEAM AND THAT IS TOPOLOGY, NOT
        /// SLACK. The twist is a principal angle, so any guard maps a CIRCLE onto an ARC; a map that is the
        /// identity inside the arc must either be discontinuous (measured: an 80.009 deg pose jump for a
        /// 0.05 deg input step) or drive the humerus to ZERO twist at 180, which is 180 deg from the pose
        /// being guarded. The guard takes the third option -- cap the correction by the distance to the
        /// seam -- and the price is that the hard bound is not enforced in the last 15 deg. Stated here
        /// rather than hidden in a tolerance.
        /// </summary>
        [Test]
        public void HumerusAndForearm_AxialRoll_StayInsideTheHumanEnvelope()
        {
            var findings = new List<string>();
            var log = new StringBuilder();

            List<Row> guarded = Sweep(feedTwistBind: true, feedLowerBind: true);
            List<Row> control = Sweep(feedTwistBind: false, feedLowerBind: false);
            Assert.That(guarded.Count, Is.GreaterThan(1000), "the sweep is too small.");

            Worst(guarded, false, out float gHumM, out float gForeM, out float gWristM, out float gChainM);
            Worst(guarded, true, out float gHumT, out float gForeT, out float gWristT, out float gChainT);
            Worst(control, false, out float cHumM, out float cForeM, out float cWristM, out float cChainM);
            Worst(control, true, out float cHumT, out float cForeT, out float cWristT, out float cChainT);

            log.AppendLine($"      path         humerus      forearm        wrist        chain");
            log.AppendLine($"      model  G  {gHumM,9:F1}  {gForeM,11:F1}  {gWristM,11:F1}  {gChainM,11:F1}");
            log.AppendLine($"      model  C  {cHumM,9:F1}  {cForeM,11:F1}  {cWristM,11:F1}  {cChainM,11:F1}   (bind fields declined)");
            log.AppendLine($"      track  G  {gHumT,9:F1}  {gForeT,11:F1}  {gWristT,11:F1}  {gChainT,11:F1}");
            log.AppendLine($"      track  C  {cHumT,9:F1}  {cForeT,11:F1}  {cWristT,11:F1}  {cChainT,11:F1}   (bind fields declined)");
            log.AppendLine($"      limits    {HumerusLimitDeg,9:F0}  {ForearmLimitDeg,11:F0}  {WristLimitDeg,11:F0}  {ChainLimitDeg,11:F0}");

            // ── HUMERUS. The envelope is a function of the PRE-guard twist: max(hard, 2*|raw| - 180). Below
            //    |raw| ~ 158 that is just the hard bound; above it the seam cap takes over and the bound
            //    relaxes, which is the documented and unavoidable price of continuity at +/-180.
            float humWorst = Mathf.Max(gHumM, gHumT);
            float humControl = Mathf.Max(cHumM, cHumT);
            float humWorstBelowSeam = 0f;
            float humWorstBelowSeamRaw = 0f;
            int belowSeam = 0;
            foreach (Row r in guarded)
            {
                float post = Mathf.Abs(r.Hum);
                float raw = Mathf.Abs(r.RawTwist);
                float envelope = Mathf.Max(HumerusLimitDeg, 2f * raw - 180f);
                if (post > envelope + 2f)
                {
                    findings.Add($"humeral axial roll came out at {r.Hum:0.0} deg against a {envelope:0.0} deg " +
                                 $"envelope for a pre-guard twist of {r.RawTwist:0.0} deg " +
                                 $"({(r.Tracker ? "tracker" : "model")}, reach {r.Reach:0.00}, el {r.El:0}, " +
                                 $"controller roll {r.HandRoll:0}, hint az {r.HintAz:0}).");
                    break;
                }
                if (raw <= 165f)
                {
                    belowSeam++;
                    if (post > humWorstBelowSeam) { humWorstBelowSeam = post; humWorstBelowSeamRaw = raw; }
                }
            }
            log.AppendLine($"      away from the +/-180 seam ({belowSeam} rows, |pre-guard twist| <= 165): worst " +
                           $"guarded humeral roll {humWorstBelowSeam:F1} deg (at a raw {humWorstBelowSeamRaw:F1} deg)");

            // ── FOREARM, NO-TRACKER PATH. ⚠️ THE ENVELOPE IS SEAM-AWARE, AND FOR THE SAME REASON THE
            //    HUMERAL ONE IS. The stage caps its clamp by the distance to the +/-180 seam
            //    (pull = min(mag - band, PI - mag)), so `want` is the comfort band for an ordinary demand
            //    and eases out along 2*|demand| - 180 above the crossing at (180+band)/2 = 130 deg,
            //    reaching 180 AT the seam where +180 and -180 are the same rotation. A flat 100 deg bound
            //    would therefore fail on exactly the poses the seam cap exists to make continuous -- which
            //    is a test contradicting a fix, not a defect.
            float worstForeExcess = 0f;
            Row wf = default;
            foreach (Row r in guarded)
            {
                if (r.Tracker) continue;
                float env = Mathf.Max(ForearmLimitDeg, 2f * Mathf.Abs(r.Demand) - 180f);
                float excess = Mathf.Abs(r.Fore) - env;
                if (excess > worstForeExcess) { worstForeExcess = excess; wf = r; }
            }
            if (worstForeExcess > 2f)
            {
                findings.Add($"forearm pronation exceeded its seam-aware envelope by {worstForeExcess:0.0} deg on the " +
                             $"NO-TRACKER path. Worst: reach {wf.Reach:0.00}, el {wf.El:0}, controller roll " +
                             $"{wf.HandRoll:0}, aux {wf.HintAz:0}, pronation {wf.Fore:0.0} deg against an envelope of " +
                             $"{Mathf.Max(ForearmLimitDeg, 2f * Mathf.Abs(wf.Demand) - 180f):0.0} for a demand of " +
                             $"{wf.Demand:0.0}; reported ForearmRollDeg {wf.ReportedRoll:0.0}, relief {wf.Relief:0.0}, " +
                             $"humerus {wf.Hum:0.0}, wrist {wf.Wrist:0.0}.");
            }

            BasisArmNet.Report(findings, log, "humerus / forearm axial roll envelope");

            Assert.That(belowSeam, Is.GreaterThan(500),
                $"only {belowSeam} rows landed away from the seam; the bound below measures almost nothing.");
            BasisArmNet.Gate(
                "humeral axial roll vs the clavicle, away from the +/-180 seam where the guard's envelope is " +
                "the plain hard bound",
                humWorstBelowSeam, HumerusLimitDeg + 1f, humControl, 170f);
            BasisArmNet.Gate("forearm pronation vs the rig's bind neutral, no-tracker path, worst EXCESS over " +
                             "the seam-aware envelope",
                             worstForeExcess, 2f, cForeM, 110f);

            TestContext.WriteLine($"  (worst guarded humeral roll ANYWHERE, seam included, is {humWorst:0.0} deg; " +
                                  $"the unguarded control reaches {humControl:0.0} deg.)");

            TestContext.WriteLine("\n  axial roll chain, worst |deg| per link:\n" + log);
        }

        // ============================================================================================
        // 2. ⭐ THE WRIST -- THE LINK NOTHING BOUNDS
        // ============================================================================================

        /// <summary>
        /// ⭐ THE HAND'S WORLD ROTATION IS WRITTEN STRAIGHT FROM THE CONTROLLER: the solver ends with
        /// `r.TipRotation = TargetRotation * TargetOffset` and the runtime does `tip.SetRotation(TipRotation)`.
        /// So every degree of controller roll that the forearm and the humerus do not absorb lands in the
        /// WRIST JOINT -- a joint that has no axial degree of freedom at all. This is the SAME SHAPE as the
        /// humeral defect that shipped: a DOF driven 1:1 from a tracker with nothing bounding it, sitting
        /// next to several carefully-bounded neighbours.
        ///
        /// The relief and the forearm roll are the absorbers, and they are bounded (70 deg of relief, an
        /// 80 deg comfort band), so what is left over past those is structurally unbounded.
        ///
        /// This test states the ANATOMICAL invariant. If it is red, that is a FINDING and the number in the
        /// message is the finding -- do not relax the constant to make it green.
        /// </summary>
        [Test]
        public void WristAxialRoll_StaysInsideTheHumanEnvelope()
        {
            var log = new StringBuilder();
            List<Row> rows = Sweep(feedTwistBind: true, feedLowerBind: true);

            Row worstModel = default, worstTracker = default;
            foreach (Row r in rows)
            {
                if (r.Tracker) { if (Mathf.Abs(r.Wrist) > Mathf.Abs(worstTracker.Wrist)) worstTracker = r; }
                else           { if (Mathf.Abs(r.Wrist) > Mathf.Abs(worstModel.Wrist)) worstModel = r; }
            }
            float wm = Mathf.Abs(worstModel.Wrist), wt = Mathf.Abs(worstTracker.Wrist);

            log.AppendLine($"      NO-TRACKER path: worst |hand-vs-forearm axial roll| {wm:F1} deg " +
                           $"(reach {worstModel.Reach:F2}, el {worstModel.El:F0}, controller roll {worstModel.HandRoll:F0}, " +
                           $"demand {worstModel.Demand:F1} -> want {worstModel.Want:F1})");
            log.AppendLine($"      TRACKER   path: worst |hand-vs-forearm axial roll| {wt:F1} deg " +
                           $"(reach {worstTracker.Reach:F2}, el {worstTracker.El:F0}, controller roll {worstTracker.HandRoll:F0}, " +
                           $"forearm {worstTracker.Fore:F1}, humerus {worstTracker.Hum:F1})");
            log.AppendLine($"      human envelope {WristLimitDeg:F0} deg; the no-tracker seam cap's own stated ceiling " +
                           "on the residual it leaves the wrist is 50 deg");
            TestContext.WriteLine("\n  wrist axial roll, BY PATH:\n" + log);

            // ⚠️ THE ANATOMICAL BOUND IS THE SAME ON BOTH PATHS, AND IT IS THE ONLY ONE ASSERTED HERE. The
            // no-tracker path is MUCH better than the tracker one and getting better -- the seam cap on
            // `want` hands the wrist a residual of demand - want that peaks at 50 deg by construction, and
            // it was measurably ~180 deg before that cap landed -- but "much better" is not "inside a joint
            // that has no axial degree of freedom", and pinning it at whatever it measures today would turn
            // an anatomical statement into a changelog. So both paths are held to the same 15 deg, both are
            // reported, and the gap between them is the interesting number.
            float worst = Mathf.Max(wm, wt);
            if (worst >= WristLimitDeg)
            {
                BasisArmNet.KnownOpen(
                    $"the hand's axial roll relative to the forearm must stay inside ~{WristLimitDeg:0} deg -- the " +
                    "radiocarpal joint has NO axial degree of freedom, so what feels like twisting your wrist is the " +
                    "forearm's radius crossing its ulna. The hand's world rotation is written straight from the " +
                    "controller (TipRotation = TargetRotation * TargetOffset, applied as tip.SetRotation), so every " +
                    "degree the absorbers above it do not take lands in a joint that cannot take any. Same shape as " +
                    "the humeral defect fixed today: a DOF driven 1:1 from a tracker with nothing bounding it",
                    $"ELBOW-TRACKER path {wt:0.0} deg (reach {worstTracker.Reach:0.00}, elevation {worstTracker.El:0}, " +
                    $"controller roll {worstTracker.HandRoll:0}); NO-TRACKER path {wm:0.0} deg (reach " +
                    $"{worstModel.Reach:0.00}, elevation {worstModel.El:0}, controller roll {worstModel.HandRoll:0}, " +
                    $"demand {worstModel.Demand:0.0} -> want {worstModel.Want:0.0}). THE TWO PATHS DIFFER BY " +
                    $"{wt - wm:0.0} DEGREES and that difference is the seam cap on the no-tracker forearm roll: it " +
                    "hands the wrist a residual of demand - want, bounded by construction, instead of fading the " +
                    "hand's say to ZERO near the seam and putting the whole residual in the wrist -- which is what " +
                    "the tracker path still does, and what the no-tracker block's own comment rejects by name. " +
                    "A parallel agent is working this defect.");
            }
            Assert.That(worst, Is.LessThan(WristLimitDeg),
                $"THE WRIST'S AXIAL ROLL REACHES {worst:0.0} DEGREES relative to the forearm (tracker {wt:0.0}, " +
                $"no-tracker {wm:0.0}), against a human envelope of ~{WristLimitDeg:0}.");
        }

        /// <summary>
        /// AND THE WHOLE CHAIN, ADDED UP. Each link on its own can look defensible while the total does not:
        /// a shoulder, a forearm and a wrist each 'only' near their limit compose into an arm that has
        /// rotated further than a person can. This is the assertion that no per-joint gate can make.
        /// </summary>
        [Test]
        public void TotalAxialRollFromClavicleToHand_StaysInsideTheHumanEnvelope()
        {
            List<Row> rows = Sweep(feedTwistBind: true, feedLowerBind: true);
            List<Row> control = Sweep(feedTwistBind: false, feedLowerBind: false);

            // Per-row, seam-aware, and split by path -- because two of the three links carry a seam cap whose
            // envelope is a function of the row's OWN input, and the third is bounded on one path and not on
            // the other. A single pooled constant would be wrong in every direction at once.
            float worstExcessModel = 0f, worstExcessTracker = 0f;
            Row wm = default, wt = default;
            foreach (Row r in rows)
            {
                float humEnv = Mathf.Max(HumerusLimitDeg, 2f * Mathf.Abs(r.RawTwist) - 180f);
                float foreEnv = r.Tracker ? TrackerForearmCeilingDeg
                                          : Mathf.Max(ForearmLimitDeg, 2f * Mathf.Abs(r.Demand) - 180f);
                float wristEnv = WristLimitDeg;   // the same anatomical bound on both paths
                float excess = r.Chain - (humEnv + foreEnv + wristEnv);
                if (r.Tracker) { if (excess > worstExcessTracker) { worstExcessTracker = excess; wt = r; } }
                else           { if (excess > worstExcessModel) { worstExcessModel = excess; wm = r; } }
            }

            float controlWorst = 0f;
            foreach (Row r in control) controlWorst = Mathf.Max(controlWorst, r.Chain);

            TestContext.WriteLine(
                "  total |clavicle->hand| axial roll, worst EXCESS over the per-row seam-aware envelope:\n" +
                $"    NO-TRACKER {worstExcessModel:0.0} deg (chain {wm.Chain:0.0} = humerus {wm.Hum:0.0} + forearm " +
                $"{wm.Fore:0.0} + wrist {wm.Wrist:0.0}, at reach {wm.Reach:0.00} el {wm.El:0} roll {wm.HandRoll:0})\n" +
                $"    TRACKER    {worstExcessTracker:0.0} deg (chain {wt.Chain:0.0} = humerus {wt.Hum:0.0} + forearm " +
                $"{wt.Fore:0.0} + WRIST {wt.Wrist:0.0}, at reach {wt.Reach:0.00} el {wt.El:0} roll {wt.HandRoll:0})\n" +
                $"    with every bind field declined the chain reaches {controlWorst:0.0} deg.");

            float worstExcess = Mathf.Max(worstExcessModel, worstExcessTracker);
            if (worstExcess > 5f)
            {
                BasisArmNet.KnownOpen(
                    "total axial roll from the clavicle to the hand must stay inside the sum of its three links' " +
                    "own envelopes. No per-joint gate can make this assertion, which is exactly how an unbounded " +
                    "DOF hides: each neighbouring bound looks defensible alone and the chain is never added up",
                    $"NO-TRACKER exceeds by {worstExcessModel:0.0} deg (chain {wm.Chain:0.0} = humerus {wm.Hum:0.0} " +
                    $"+ forearm {wm.Fore:0.0} + wrist {wm.Wrist:0.0}); ELBOW-TRACKER exceeds by " +
                    $"{worstExcessTracker:0.0} deg (chain {wt.Chain:0.0} = humerus {wt.Hum:0.0} + forearm " +
                    $"{wt.Fore:0.0} + WRIST {wt.Wrist:0.0}). BOTH breaches are the WRIST term alone -- the humerus " +
                    "and forearm terms are inside their own seam-aware envelopes on both paths, which is what makes " +
                    "this a pointer at one open defect rather than a diffuse 'everything is loose'.");
            }
            Assert.That(worstExcess, Is.LessThan(5f),
                $"the axial-roll chain exceeds its per-row envelope by {worstExcess:0.0} deg.");
        }
    }
}
