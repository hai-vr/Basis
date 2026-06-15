using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Basis.Scripts.Avatar;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Offline sweep of the tracker-placement DISCOVERY pass (BasisConstellationClassifier — the
    // same code the live FullBodyCalibration runs). Generates synthetic tracker constellations for
    // many body archetypes x tracker-set presets x EYE HEIGHTS x placement noise, runs the real
    // classifier, and checks each tracker landed on the role it was placed for. One CSV row per
    // (scenario, tracker). No avatar, no play mode.
    //
    // Heights are modelled correctly: bodies are built in METRES at each eye height, noise is
    // specified in CENTIMETRES (absolute), and the classifier normalizes by a (possibly mis-
    // measured) eye height — so a fixed-cm error becomes a proportionally larger ratio error for a
    // short user (a child), exactly as in reality. The per-eye-height accuracy table surfaces any
    // height bias.

    // A body's proportions in eye-height ratios. Multiplied by an eye height to get metres.
    public struct BasisBodyArchetype
    {
        public string Name;
        public bool Core;      // must classify cleanly; the strict part of the gate. Extreme bodies are reported, not gated.
        public float HipsH, ChestH;
        public float ShoulderH, ElbowH, HandH;
        public float ShoulderLat, ElbowLat, HandLat;   // lateral magnitude (right > 0); left side negates
        public float KneeH, FootMountH, ToeH;
        public float StanceLat;
        public float ToeForwardRatio;                  // toe sits this far forward of the foot (ratio of eye height)
    }

    public struct BasisTrackerSetPreset
    {
        public string Name;
        public BasisBoneTrackedRole[] Roles;           // the FREE FB trackers present (hands are pinned, fed separately)
    }

    public struct BasisTrackerPlacementSweepConfig
    {
        public float[] EyeHeights;            // eye-height sweep axis in metres — the "different heights" axis
        public float[] JitterCm;              // placement noise in centimetres (absolute); converted to ratio per eye height
        public float EyeHeightMeasureErrorCm; // systematic floor / eye-height mis-measure (cm): classifier normalizes by trueEye + this
        public int SeedsPerJitter;            // repeats per (archetype, set, height, jitter>0) for statistics
        public float Tolerance;               // calibration tolerance (sigma multiplier), 1 = stock
        public bool HandsPresent;             // feed hand-controller poses (drives the elbow-prior re-center)
        public bool DumpFullScoreTable;       // also write a per-(scenario,tracker,role) score table

        public static BasisTrackerPlacementSweepConfig Default()
        {
            return new BasisTrackerPlacementSweepConfig
            {
                EyeHeights = new[] { 0.9f, 1.2f, 1.5f, 1.8f, 2.1f }, // child → tall adult, ascending
                JitterCm = new[] { 0f, 2f, 4f },                    // perfect, typical strap/tracking slop, sloppy
                EyeHeightMeasureErrorCm = 0f,
                SeedsPerJitter = 6,
                Tolerance = 1f,
                HandsPresent = true,
                DumpFullScoreTable = false,
            };
        }
    }

    public struct BasisTrackerPlacementSummary
    {
        public bool Ok;
        public string Path;
        public string FullTablePath;
        public string Error;

        public int Scenarios;
        public int Trackers;
        public int Correct;
        public int Misassigned;
        public int Unassigned;

        public int CleanTrackers;       // jitter == 0 (at every eye height)
        public int CleanCorrect;
        public int CoreCleanTrackers;   // jitter == 0 AND archetype.Core
        public int CoreCleanCorrect;

        public float MinCorrectMargin;  // smallest winning margin over the runner-up role among correct binds
        public int CrossSideLeaks;      // tracker placed on one side bound to the opposite side's role
        public int CleanCrossSideLeaks; // cross-side leaks in zero-jitter scenarios (the gate-relevant subset)
        public int NearOriginLeaks;     // stale (origin) tracker that wrongly bound to a role

        // Height bias: accuracy on NOISY rows at the shortest vs tallest eye height. A large gap
        // means short users (kids) degrade more under the same physical (cm) sloppiness.
        public float ShortAccuracy;
        public float TallAccuracy;
        public string PerHeightAccuracy; // "0.90m:96.1% 1.20m:98.0% ..." (noisy rows)
        public float MeasureErrorCm;

        public string TopConfusions;    // "Hips->Chest x3; LeftLowerArm->LeftShoulder x2"
        public int FullTableRows;

        public float CleanCorrectFraction => CleanTrackers > 0 ? (float)CleanCorrect / CleanTrackers : 0f;
        public float CoreCleanFraction => CoreCleanTrackers > 0 ? (float)CoreCleanCorrect / CoreCleanTrackers : 0f;
        public float HeightBiasPts => TallAccuracy - ShortAccuracy;
    }

    public static class BasisTrackerPlacementSweep
    {
        public const string DefaultFileName = "BasisTrackerPlacementSweep.csv";

        public static string DefaultPath()
        {
            return Path.Combine(Application.persistentDataPath, DefaultFileName);
        }

        // Realistic anthropometry in eye-height ratios. "average" is centred on the classifier's own
        // priors (so it must classify perfectly); the rest deviate the way real bodies / mounts do.
        public static readonly BasisBodyArchetype[] Archetypes =
        {
            new BasisBodyArchetype { Name = "average",        Core = true,  HipsH = 0.55f, ChestH = 0.78f, ShoulderH = 0.88f, ElbowH = 0.88f, HandH = 0.88f, ShoulderLat = 0.14f, ElbowLat = 0.30f, HandLat = 0.52f, KneeH = 0.27f, FootMountH = 0.05f, ToeH = 0.02f, StanceLat = 0.10f, ToeForwardRatio = 0.06f },
            new BasisBodyArchetype { Name = "tall-slim",      Core = true,  HipsH = 0.56f, ChestH = 0.80f, ShoulderH = 0.90f, ElbowH = 0.90f, HandH = 0.90f, ShoulderLat = 0.12f, ElbowLat = 0.28f, HandLat = 0.50f, KneeH = 0.28f, FootMountH = 0.05f, ToeH = 0.02f, StanceLat = 0.08f, ToeForwardRatio = 0.06f },
            new BasisBodyArchetype { Name = "short-stocky",   Core = true,  HipsH = 0.53f, ChestH = 0.76f, ShoulderH = 0.86f, ElbowH = 0.86f, HandH = 0.86f, ShoulderLat = 0.16f, ElbowLat = 0.33f, HandLat = 0.55f, KneeH = 0.26f, FootMountH = 0.05f, ToeH = 0.02f, StanceLat = 0.13f, ToeForwardRatio = 0.06f },
            new BasisBodyArchetype { Name = "long-arms",      Core = true,  HipsH = 0.55f, ChestH = 0.78f, ShoulderH = 0.88f, ElbowH = 0.87f, HandH = 0.86f, ShoulderLat = 0.15f, ElbowLat = 0.36f, HandLat = 0.62f, KneeH = 0.27f, FootMountH = 0.05f, ToeH = 0.02f, StanceLat = 0.10f, ToeForwardRatio = 0.06f },
            new BasisBodyArchetype { Name = "short-arms",     Core = true,  HipsH = 0.55f, ChestH = 0.78f, ShoulderH = 0.88f, ElbowH = 0.88f, HandH = 0.88f, ShoulderLat = 0.13f, ElbowLat = 0.26f, HandLat = 0.44f, KneeH = 0.27f, FootMountH = 0.05f, ToeH = 0.02f, StanceLat = 0.10f, ToeForwardRatio = 0.06f },
            new BasisBodyArchetype { Name = "wide-stance",    Core = true,  HipsH = 0.55f, ChestH = 0.78f, ShoulderH = 0.88f, ElbowH = 0.88f, HandH = 0.88f, ShoulderLat = 0.14f, ElbowLat = 0.30f, HandLat = 0.52f, KneeH = 0.27f, FootMountH = 0.05f, ToeH = 0.02f, StanceLat = 0.18f, ToeForwardRatio = 0.06f },
            new BasisBodyArchetype { Name = "narrow-stance",  Core = true,  HipsH = 0.55f, ChestH = 0.78f, ShoulderH = 0.88f, ElbowH = 0.88f, HandH = 0.88f, ShoulderLat = 0.14f, ElbowLat = 0.30f, HandLat = 0.52f, KneeH = 0.27f, FootMountH = 0.05f, ToeH = 0.02f, StanceLat = 0.05f, ToeForwardRatio = 0.06f },
            new BasisBodyArchetype { Name = "high-ankle-mnt", Core = true,  HipsH = 0.55f, ChestH = 0.78f, ShoulderH = 0.88f, ElbowH = 0.88f, HandH = 0.88f, ShoulderLat = 0.14f, ElbowLat = 0.30f, HandLat = 0.52f, KneeH = 0.27f, FootMountH = 0.12f, ToeH = 0.04f, StanceLat = 0.10f, ToeForwardRatio = 0.06f },
            new BasisBodyArchetype { Name = "low-chest",      Core = true,  HipsH = 0.55f, ChestH = 0.72f, ShoulderH = 0.85f, ElbowH = 0.85f, HandH = 0.85f, ShoulderLat = 0.14f, ElbowLat = 0.30f, HandLat = 0.52f, KneeH = 0.27f, FootMountH = 0.05f, ToeH = 0.02f, StanceLat = 0.10f, ToeForwardRatio = 0.06f },
            new BasisBodyArchetype { Name = "child-prop",     Core = false, HipsH = 0.58f, ChestH = 0.80f, ShoulderH = 0.89f, ElbowH = 0.88f, HandH = 0.87f, ShoulderLat = 0.16f, ElbowLat = 0.30f, HandLat = 0.48f, KneeH = 0.30f, FootMountH = 0.05f, ToeH = 0.02f, StanceLat = 0.12f, ToeForwardRatio = 0.07f },
            new BasisBodyArchetype { Name = "deep-squat-cal", Core = false, HipsH = 0.50f, ChestH = 0.74f, ShoulderH = 0.84f, ElbowH = 0.83f, HandH = 0.82f, ShoulderLat = 0.15f, ElbowLat = 0.31f, HandLat = 0.53f, KneeH = 0.24f, FootMountH = 0.05f, ToeH = 0.02f, StanceLat = 0.14f, ToeForwardRatio = 0.06f },
            new BasisBodyArchetype { Name = "broad-shoulder", Core = false, HipsH = 0.55f, ChestH = 0.78f, ShoulderH = 0.88f, ElbowH = 0.87f, HandH = 0.86f, ShoulderLat = 0.22f, ElbowLat = 0.38f, HandLat = 0.58f, KneeH = 0.27f, FootMountH = 0.05f, ToeH = 0.02f, StanceLat = 0.10f, ToeForwardRatio = 0.06f },
        };

        public static readonly BasisTrackerSetPreset[] Presets =
        {
            new BasisTrackerSetPreset { Name = "waist+feet", Roles = new[]
            {
                BasisBoneTrackedRole.Hips, BasisBoneTrackedRole.LeftFoot, BasisBoneTrackedRole.RightFoot,
            }},
            new BasisTrackerSetPreset { Name = "waist+feet+knees", Roles = new[]
            {
                BasisBoneTrackedRole.Hips, BasisBoneTrackedRole.LeftFoot, BasisBoneTrackedRole.RightFoot,
                BasisBoneTrackedRole.LeftLowerLeg, BasisBoneTrackedRole.RightLowerLeg,
            }},
            new BasisTrackerSetPreset { Name = "chest+waist+feet+knees", Roles = new[]
            {
                BasisBoneTrackedRole.Chest, BasisBoneTrackedRole.Hips,
                BasisBoneTrackedRole.LeftFoot, BasisBoneTrackedRole.RightFoot,
                BasisBoneTrackedRole.LeftLowerLeg, BasisBoneTrackedRole.RightLowerLeg,
            }},
            new BasisTrackerSetPreset { Name = "ten-point", Roles = new[]
            {
                BasisBoneTrackedRole.Chest, BasisBoneTrackedRole.Hips,
                BasisBoneTrackedRole.LeftFoot, BasisBoneTrackedRole.RightFoot,
                BasisBoneTrackedRole.LeftLowerLeg, BasisBoneTrackedRole.RightLowerLeg,
                BasisBoneTrackedRole.LeftLowerArm, BasisBoneTrackedRole.RightLowerArm,
                BasisBoneTrackedRole.LeftShoulder, BasisBoneTrackedRole.RightShoulder,
            }},
            new BasisTrackerSetPreset { Name = "full+toes", Roles = new[]
            {
                BasisBoneTrackedRole.Chest, BasisBoneTrackedRole.Hips,
                BasisBoneTrackedRole.LeftFoot, BasisBoneTrackedRole.RightFoot,
                BasisBoneTrackedRole.LeftLowerLeg, BasisBoneTrackedRole.RightLowerLeg,
                BasisBoneTrackedRole.LeftLowerArm, BasisBoneTrackedRole.RightLowerArm,
                BasisBoneTrackedRole.LeftShoulder, BasisBoneTrackedRole.RightShoulder,
                BasisBoneTrackedRole.LeftToes, BasisBoneTrackedRole.RightToes,
            }},
        };

        // The sweep tests the classifier itself, so every FB role is a candidate regardless of the
        // user's live toggles — the matcher must place the present trackers among all possible roles.
        private static readonly System.Func<BasisBoneTrackedRole, bool> AllRolesEnabled = _ => true;

        public static BasisTrackerPlacementSummary Run(BasisTrackerPlacementSweepConfig cfg, string path)
        {
            var summary = new BasisTrackerPlacementSummary { Ok = false, Path = path, MeasureErrorCm = cfg.EyeHeightMeasureErrorCm };
            var confusion = new Dictionary<long, int>();
            float minCorrectMargin = float.PositiveInfinity;

            float[] eyeHeights = (cfg.EyeHeights != null && cfg.EyeHeights.Length > 0) ? cfg.EyeHeights : new[] { 1.5f };
            float[] jitterCm = (cfg.JitterCm != null && cfg.JitterCm.Length > 0) ? cfg.JitterCm : new[] { 0f };
            int[] hNoisyTot = new int[eyeHeights.Length];
            int[] hNoisyCorr = new int[eyeHeights.Length];

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                StreamWriter ft = null;
                if (cfg.DumpFullScoreTable)
                {
                    summary.FullTablePath = Path.Combine(dir ?? "", Path.GetFileNameWithoutExtension(path) + "_scores.csv");
                    ft = new StreamWriter(summary.FullTablePath, false, Encoding.UTF8);
                    ft.WriteLine("scenario,archetype,set,eye_height,jitter_cm,seed,true_role,role,score");
                }

                using (var w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.WriteLine("# BasisTrackerPlacementSweep " + System.DateTime.UtcNow.ToString("o"));
                    w.WriteLine("# tolerance=" + F(cfg.Tolerance) + " handsPresent=" + cfg.HandsPresent +
                                " eyeHeightMeasureErrorCm=" + F(cfg.EyeHeightMeasureErrorCm) +
                                " acceptThreshold=" + F(BasisConstellationClassifier.AcceptThreshold));
                    w.WriteLine("scenario,archetype,core,set,eye_height,jitter_cm,seed,true_role,placed_h,placed_lat,placed_depth," +
                                "assigned_role,assigned_kind,assigned_score,runnerup_role,runnerup_score,margin,correct,unassigned,cross_side");

                    int scenarioId = 0;
                    var sb = new StringBuilder(256);

                    for (int a = 0; a < Archetypes.Length; a++)
                    {
                        BasisBodyArchetype arc = Archetypes[a];
                        for (int p = 0; p < Presets.Length; p++)
                        {
                            BasisTrackerSetPreset preset = Presets[p];
                            for (int hi = 0; hi < eyeHeights.Length; hi++)
                            {
                                float eyeHeight = eyeHeights[hi];
                                for (int jl = 0; jl < jitterCm.Length; jl++)
                                {
                                    float jcm = jitterCm[jl];
                                    bool clean = jcm <= 0f;
                                    int reps = clean ? 1 : Mathf.Max(1, cfg.SeedsPerJitter);
                                    for (int rep = 0; rep < reps; rep++)
                                    {
                                        int seed = unchecked(((((a * 397 + p) * 397 + hi) * 397 + jl) * 397 + rep) * 397 + 17);
                                        var rng = new System.Random(seed);

                                        BasisConstellationSample[] samples = BuildSamples(arc, preset.Roles, eyeHeight, jcm, cfg.EyeHeightMeasureErrorCm, rng, out BasisBoneTrackedRole[] trueRoles);
                                        BasisConstellationHand lh = cfg.HandsPresent ? Hand(arc, -1, eyeHeight, cfg.EyeHeightMeasureErrorCm) : BasisConstellationHand.None;
                                        BasisConstellationHand rh = cfg.HandsPresent ? Hand(arc, +1, eyeHeight, cfg.EyeHeightMeasureErrorCm) : BasisConstellationHand.None;

                                        BasisConstellationResult result = BasisConstellationClassifier.Classify(
                                            samples, samples.Length, lh, rh, cfg.Tolerance, AllRolesEnabled);

                                        for (int i = 0; i < samples.Length; i++)
                                        {
                                            BasisBoneTrackedRole trueRole = trueRoles[i];
                                            bool assigned = result.TryGetRole(i, out BasisBoneTrackedRole gotRole);
                                            bool correct = assigned && gotRole == trueRole;
                                            bool unassigned = !assigned;

                                            FullScores(samples[i], result.Priors, gotRole, assigned,
                                                out BasisBoneTrackedRole runnerRole, out float runnerScore, out float assignedScore);
                                            float margin = assigned ? assignedScore - runnerScore : float.NaN;

                                            int trueSide = trueRole.SideSign();
                                            int gotSide = assigned ? gotRole.SideSign() : 0;
                                            bool crossSide = assigned && trueSide != 0 && gotSide != 0 && trueSide != gotSide;

                                            summary.Trackers++;
                                            if (correct) summary.Correct++;
                                            else if (unassigned) summary.Unassigned++;
                                            else summary.Misassigned++;
                                            if (crossSide) { summary.CrossSideLeaks++; if (clean) summary.CleanCrossSideLeaks++; }

                                            if (clean)
                                            {
                                                summary.CleanTrackers++;
                                                if (correct) summary.CleanCorrect++;
                                                if (arc.Core) { summary.CoreCleanTrackers++; if (correct) summary.CoreCleanCorrect++; }
                                            }
                                            else
                                            {
                                                hNoisyTot[hi]++;
                                                if (correct) hNoisyCorr[hi]++;
                                            }
                                            if (correct && !float.IsNaN(margin) && margin < minCorrectMargin) minCorrectMargin = margin;
                                            if (!correct) AddConfusion(confusion, trueRole, assigned ? (int)gotRole : -1);

                                            sb.Clear();
                                            sb.Append(scenarioId).Append(',').Append(arc.Name).Append(',').Append(arc.Core ? 1 : 0).Append(',')
                                              .Append(preset.Name).Append(',').Append(F(eyeHeight)).Append(',').Append(F(jcm)).Append(',').Append(seed).Append(',')
                                              .Append(trueRole).Append(',').Append(F(samples[i].HeightRatio)).Append(',').Append(F(samples[i].LateralRatio)).Append(',').Append(F(samples[i].DepthLocal)).Append(',')
                                              .Append(assigned ? gotRole.ToString() : "(none)").Append(',').Append(result.AssignedKind[i]).Append(',').Append(F(assignedScore)).Append(',')
                                              .Append(runnerRole).Append(',').Append(F(runnerScore)).Append(',').Append(F(margin)).Append(',')
                                              .Append(correct ? 1 : 0).Append(',').Append(unassigned ? 1 : 0).Append(',').Append(crossSide ? 1 : 0);
                                            w.WriteLine(sb.ToString());

                                            if (ft != null)
                                            {
                                                for (int pr = 0; pr < result.Priors.Length; pr++)
                                                {
                                                    ft.Write(scenarioId); ft.Write(','); ft.Write(arc.Name); ft.Write(','); ft.Write(preset.Name); ft.Write(',');
                                                    ft.Write(F(eyeHeight)); ft.Write(','); ft.Write(F(jcm)); ft.Write(','); ft.Write(seed); ft.Write(','); ft.Write(trueRole.ToString()); ft.Write(',');
                                                    ft.Write(result.Priors[pr].Role.ToString()); ft.Write(',');
                                                    ft.WriteLine(F(BasisConstellationClassifier.Score(samples[i], result.Priors[pr])));
                                                    summary.FullTableRows++;
                                                }
                                            }
                                        }
                                        summary.Scenarios++;
                                        scenarioId++;
                                    }
                                }
                            }
                        }
                    }

                    float repEye = eyeHeights[eyeHeights.Length / 2];
                    summary.NearOriginLeaks = RunStressScenarios(w, sb, cfg, repEye, ref scenarioId);
                }

                ft?.Dispose();

                summary.MinCorrectMargin = float.IsPositiveInfinity(minCorrectMargin) ? 0f : minCorrectMargin;
                summary.TopConfusions = FormatConfusions(confusion);
                summary.PerHeightAccuracy = FormatPerHeight(eyeHeights, hNoisyTot, hNoisyCorr);
                summary.ShortAccuracy = hNoisyTot[0] > 0 ? 100f * hNoisyCorr[0] / hNoisyTot[0] : 100f;
                int last = eyeHeights.Length - 1;
                summary.TallAccuracy = hNoisyTot[last] > 0 ? 100f * hNoisyCorr[last] / hNoisyTot[last] : 100f;
                summary.Ok = true;
            }
            catch (System.Exception e)
            {
                summary.Ok = false;
                summary.Error = e.Message;
            }
            return summary;
        }

        // Stress cases that exercise the classifier's safety rails rather than ordinary placement:
        // a stale (origin) tracker must never bind, and an extra tracker crowding a taken role must
        // not displace it. Returns the near-origin leak count (assignments that should never happen).
        private static int RunStressScenarios(StreamWriter w, StringBuilder sb, BasisTrackerPlacementSweepConfig cfg, float eyeHeight, ref int scenarioId)
        {
            int nearOriginLeaks = 0;
            BasisBodyArchetype arc = Archetypes[0];

            // 1) A full set plus one stale device polled at the origin. The stale one must stay unassigned.
            {
                var rng = new System.Random(1234567);
                BasisConstellationSample[] samples = BuildSamples(arc, Presets[2].Roles, eyeHeight, 0f, 0f, rng, out _);
                int n = samples.Length;
                System.Array.Resize(ref samples, n + 1);
                samples[n] = new BasisConstellationSample { HeightRatio = 0f, LateralRatio = 0f, DepthLocal = 0f, NearOrigin = true, ForcedRole = -1 };

                BasisConstellationResult r = BasisConstellationClassifier.Classify(samples, samples.Length,
                    cfg.HandsPresent ? Hand(arc, -1, eyeHeight, 0f) : BasisConstellationHand.None,
                    cfg.HandsPresent ? Hand(arc, +1, eyeHeight, 0f) : BasisConstellationHand.None, cfg.Tolerance, AllRolesEnabled);

                if (r.TryGetRole(n, out BasisBoneTrackedRole leaked))
                {
                    nearOriginLeaks++;
                    WriteStressRow(w, sb, scenarioId, "stress-nearorigin", eyeHeight, "stale-origin", leaked.ToString(), false);
                }
                else
                {
                    WriteStressRow(w, sb, scenarioId, "stress-nearorigin", eyeHeight, "(none)", "(none)", true);
                }
                scenarioId++;
            }

            // 2) Two trackers crowding the chest band — only one may win Chest; the other should take
            //    its next-best open role or stay unassigned, never duplicate Chest.
            {
                var samples = new[]
                {
                    new BasisConstellationSample { HeightRatio = arc.ChestH, LateralRatio = 0f, DepthLocal = 0f, NearOrigin = false, ForcedRole = -1 },
                    new BasisConstellationSample { HeightRatio = arc.ChestH + 0.01f, LateralRatio = 0.02f, DepthLocal = 0f, NearOrigin = false, ForcedRole = -1 },
                    new BasisConstellationSample { HeightRatio = arc.HipsH, LateralRatio = 0f, DepthLocal = 0f, NearOrigin = false, ForcedRole = -1 },
                };
                BasisConstellationResult r = BasisConstellationClassifier.Classify(samples, samples.Length,
                    BasisConstellationHand.None, BasisConstellationHand.None, cfg.Tolerance, AllRolesEnabled);
                int chestCount = 0;
                for (int i = 0; i < samples.Length; i++)
                {
                    if (r.TryGetRole(i, out BasisBoneTrackedRole gr) && gr == BasisBoneTrackedRole.Chest) chestCount++;
                }
                WriteStressRow(w, sb, scenarioId, "stress-dupchest", eyeHeight, "chestBinds=" + chestCount, chestCount <= 1 ? "ok" : "DUPLICATE", chestCount <= 1);
                scenarioId++;
            }

            return nearOriginLeaks;
        }

        // Builds the constellation in METRES at the body's true eye height, adds absolute (cm) noise,
        // then normalizes by the (possibly mis-measured) eye height the classifier would use. This is
        // what makes height handling correct: a fixed-cm error is a larger RATIO error for a short
        // body, and an eye-height mis-measure distorts a short body's ratios more than a tall one's.
        private static BasisConstellationSample[] BuildSamples(BasisBodyArchetype arc, BasisBoneTrackedRole[] roles, float eyeHeight, float jitterCm, float measErrCm, System.Random rng, out BasisBoneTrackedRole[] trueRoles)
        {
            float measuredEye = Mathf.Max(eyeHeight + measErrCm * 0.01f, 0.3f);
            float sigmaM = jitterCm * 0.01f;
            var samples = new BasisConstellationSample[roles.Length];
            trueRoles = new BasisBoneTrackedRole[roles.Length];
            for (int i = 0; i < roles.Length; i++)
            {
                Placement(arc, roles[i], out float hRatio, out float latRatio, out float depthRatio);
                float yM = hRatio * eyeHeight;
                float xM = latRatio * eyeHeight;
                float zM = depthRatio * eyeHeight;
                if (sigmaM > 0f)
                {
                    yM += (float)NextGaussian(rng) * sigmaM;
                    xM += (float)NextGaussian(rng) * sigmaM;
                    zM += (float)NextGaussian(rng) * sigmaM;
                }
                samples[i] = new BasisConstellationSample
                {
                    HeightRatio = yM / measuredEye,
                    LateralRatio = xM / measuredEye,
                    DepthLocal = zM, // metres — the toe-forward epsilon is absolute
                    NearOrigin = false,
                    ForcedRole = -1,
                };
                trueRoles[i] = roles[i];
            }
            return samples;
        }

        private static void Placement(BasisBodyArchetype arc, BasisBoneTrackedRole role, out float h, out float lat, out float depthRatio)
        {
            depthRatio = 0f;
            switch (role)
            {
                case BasisBoneTrackedRole.Hips: h = arc.HipsH; lat = 0f; break;
                case BasisBoneTrackedRole.Chest: h = arc.ChestH; lat = 0f; break;
                case BasisBoneTrackedRole.LeftFoot: h = arc.FootMountH; lat = -arc.StanceLat; break;
                case BasisBoneTrackedRole.RightFoot: h = arc.FootMountH; lat = +arc.StanceLat; break;
                case BasisBoneTrackedRole.LeftToes: h = arc.ToeH; lat = -arc.StanceLat; depthRatio = arc.ToeForwardRatio; break;
                case BasisBoneTrackedRole.RightToes: h = arc.ToeH; lat = +arc.StanceLat; depthRatio = arc.ToeForwardRatio; break;
                case BasisBoneTrackedRole.LeftLowerLeg: h = arc.KneeH; lat = -arc.StanceLat; break;
                case BasisBoneTrackedRole.RightLowerLeg: h = arc.KneeH; lat = +arc.StanceLat; break;
                case BasisBoneTrackedRole.LeftShoulder: h = arc.ShoulderH; lat = -arc.ShoulderLat; break;
                case BasisBoneTrackedRole.RightShoulder: h = arc.ShoulderH; lat = +arc.ShoulderLat; break;
                case BasisBoneTrackedRole.LeftLowerArm: h = arc.ElbowH; lat = -arc.ElbowLat; break;
                case BasisBoneTrackedRole.RightLowerArm: h = arc.ElbowH; lat = +arc.ElbowLat; break;
                default: h = 0.5f; lat = 0f; break;
            }
        }

        private static BasisConstellationHand Hand(BasisBodyArchetype arc, int sideSign, float eyeHeight, float measErrCm)
        {
            // A tracked controller (low noise); only the eye-height normalization applies.
            float measuredEye = Mathf.Max(eyeHeight + measErrCm * 0.01f, 0.3f);
            float yM = arc.HandH * eyeHeight;
            float xM = sideSign * arc.HandLat * eyeHeight;
            return new BasisConstellationHand { Present = true, HeightRatio = yM / measuredEye, LateralRatio = xM / measuredEye };
        }

        private static void FullScores(BasisConstellationSample sample, BasisConstellationPrior[] priors, BasisBoneTrackedRole assignedRole, bool assigned,
            out BasisBoneTrackedRole runnerRole, out float runnerScore, out float assignedScore)
        {
            assignedScore = float.NaN;
            runnerRole = BasisBoneTrackedRole.CenterEye;
            runnerScore = float.NegativeInfinity;
            for (int i = 0; i < priors.Length; i++)
            {
                float s = BasisConstellationClassifier.Score(sample, priors[i]);
                if (assigned && priors[i].Role == assignedRole) { assignedScore = s; continue; }
                if (s > runnerScore) { runnerScore = s; runnerRole = priors[i].Role; }
            }
            // When unassigned, "runner" is just the best-fitting role overall (the would-be pick).
            if (float.IsNegativeInfinity(runnerScore)) runnerScore = float.NaN;
        }

        private static void AddConfusion(Dictionary<long, int> confusion, BasisBoneTrackedRole trueRole, int assignedRoleOrNeg)
        {
            long key = ((long)(int)trueRole << 32) | (uint)assignedRoleOrNeg;
            confusion.TryGetValue(key, out int c);
            confusion[key] = c + 1;
        }

        private static string FormatConfusions(Dictionary<long, int> confusion)
        {
            if (confusion.Count == 0) return "none";
            var list = new List<KeyValuePair<long, int>>(confusion);
            list.Sort((x, y) => y.Value.CompareTo(x.Value));
            var sb = new StringBuilder();
            int shown = 0;
            for (int i = 0; i < list.Count && shown < 6; i++, shown++)
            {
                long key = list[i].Key;
                var trueRole = (BasisBoneTrackedRole)(int)(key >> 32);
                int assignedInt = (int)(key & 0xffffffff);
                string assigned = assignedInt < 0 ? "(unassigned)" : ((BasisBoneTrackedRole)assignedInt).ToString();
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(trueRole).Append("->").Append(assigned).Append(" x").Append(list[i].Value);
            }
            if (list.Count > shown) sb.Append("; +").Append(list.Count - shown).Append(" more");
            return sb.ToString();
        }

        private static string FormatPerHeight(float[] eyeHeights, int[] tot, int[] corr)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < eyeHeights.Length; i++)
            {
                float acc = tot[i] > 0 ? 100f * corr[i] / tot[i] : 100f;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(eyeHeights[i].ToString("0.00", CultureInfo.InvariantCulture)).Append("m:").Append(acc.ToString("0.0", CultureInfo.InvariantCulture)).Append('%');
            }
            return sb.ToString();
        }

        private static void WriteStressRow(StreamWriter w, StringBuilder sb, int scenarioId, string set, float eyeHeight, string trueOrNote, string assigned, bool ok)
        {
            sb.Clear();
            sb.Append(scenarioId).Append(",stress,0,").Append(set).Append(',').Append(F(eyeHeight)).Append(",0,0,").Append(trueOrNote)
              .Append(",0,0,0,").Append(assigned).Append(",Stress,0,(none),0,0,").Append(ok ? 1 : 0).Append(",0,0");
            w.WriteLine(sb.ToString());
        }

        // Box-Muller; deterministic per seeded rng so a flagged scenario reproduces exactly.
        private static double NextGaussian(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();
            return System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2);
        }

        private static string F(float v)
        {
            if (float.IsNaN(v)) return "nan";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
