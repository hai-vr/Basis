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
    // many body archetypes (heights, limb proportions, stance, mount variants) x tracker-set
    // presets x placement jitter, runs the real classifier, and checks each tracker landed on the
    // role it was placed for. One CSV row per (scenario, tracker). No avatar, no play mode.

    // A body in eye-height-ratio space: where each tracker sits on a given person. The classifier
    // is height-invariant (it works in ratios), so "different heights" = different proportions here,
    // plus an explicit invariance pass that varies absolute eye height.
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
        public float[] JitterLevels;        // gaussian sigma added to each tracker's height/lateral ratio
        public int SeedsPerJitter;          // repeats per (archetype, set, jitter>0) for statistics
        public float EyeHeight;             // metres; only affects the toe-forward depth (the one absolute-scale path)
        public float Tolerance;             // calibration tolerance (sigma multiplier), 1 = stock
        public bool HandsPresent;           // feed hand-controller poses (drives the elbow-prior re-center)
        public bool DumpFullScoreTable;     // also write a per-(scenario,tracker,role) score table
        public bool IncludeInvariancePass;  // re-run clean scenarios across several eye heights, flag any assignment change
        public float[] InvarianceEyeHeights;

        public static BasisTrackerPlacementSweepConfig Default()
        {
            return new BasisTrackerPlacementSweepConfig
            {
                JitterLevels = new[] { 0f, 0.02f, 0.04f },
                SeedsPerJitter = 8,
                EyeHeight = 1.7f,
                Tolerance = 1f,
                HandsPresent = true,
                DumpFullScoreTable = false,
                IncludeInvariancePass = true,
                InvarianceEyeHeights = new[] { 0.95f, 1.3f, 1.7f, 2.0f },
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

        public int CleanTrackers;       // jitter == 0
        public int CleanCorrect;
        public int CoreCleanTrackers;   // jitter == 0 AND archetype.Core
        public int CoreCleanCorrect;

        public float MinCorrectMargin;  // smallest winning margin over the runner-up role among correct binds
        public int CrossSideLeaks;      // tracker placed on one side bound to the opposite side's role
        public int NearOriginLeaks;     // stale (origin) tracker that wrongly bound to a role

        public int InvarianceChecks;
        public int InvarianceDiffs;

        public string TopConfusions;    // "Hips->Chest x3; LeftLowerArm->LeftShoulder x2"
        public int FullTableRows;

        public float CleanCorrectFraction => CleanTrackers > 0 ? (float)CleanCorrect / CleanTrackers : 0f;
        public float CoreCleanFraction => CoreCleanTrackers > 0 ? (float)CoreCleanCorrect / CoreCleanTrackers : 0f;
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
            var summary = new BasisTrackerPlacementSummary { Ok = false, Path = path };
            var confusion = new Dictionary<long, int>();
            float minCorrectMargin = float.PositiveInfinity;

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                StreamWriter ft = null;
                if (cfg.DumpFullScoreTable)
                {
                    summary.FullTablePath = Path.Combine(dir ?? "", Path.GetFileNameWithoutExtension(path) + "_scores.csv");
                    ft = new StreamWriter(summary.FullTablePath, false, Encoding.UTF8);
                    ft.WriteLine("scenario,archetype,set,jitter,seed,true_role,role,score");
                }

                using (var w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.WriteLine("# BasisTrackerPlacementSweep " + System.DateTime.UtcNow.ToString("o"));
                    w.WriteLine("# eyeHeight=" + F(cfg.EyeHeight) + " tolerance=" + F(cfg.Tolerance) +
                                " handsPresent=" + cfg.HandsPresent + " acceptThreshold=" + F(BasisConstellationClassifier.AcceptThreshold));
                    w.WriteLine("scenario,archetype,core,set,jitter,seed,true_role,placed_h,placed_lat,placed_depth," +
                                "assigned_role,assigned_kind,assigned_score,runnerup_role,runnerup_score,margin,correct,unassigned,cross_side");

                    int scenarioId = 0;
                    var sb = new StringBuilder(256);

                    for (int a = 0; a < Archetypes.Length; a++)
                    {
                        BasisBodyArchetype arc = Archetypes[a];
                        for (int p = 0; p < Presets.Length; p++)
                        {
                            BasisTrackerSetPreset preset = Presets[p];
                            for (int jl = 0; jl < cfg.JitterLevels.Length; jl++)
                            {
                                float jitter = cfg.JitterLevels[jl];
                                int reps = jitter <= 0f ? 1 : Mathf.Max(1, cfg.SeedsPerJitter);
                                for (int rep = 0; rep < reps; rep++)
                                {
                                    int seed = unchecked((a * 73856093) ^ (p * 19349663) ^ (jl * 83492791) ^ (rep * 2654435761u).GetHashCode());
                                    var rng = new System.Random(seed);

                                    BasisConstellationSample[] samples = BuildSamples(arc, preset.Roles, jitter, cfg.EyeHeight, rng, out BasisBoneTrackedRole[] trueRoles);
                                    BasisConstellationHand lh = cfg.HandsPresent ? Hand(arc, -1) : BasisConstellationHand.None;
                                    BasisConstellationHand rh = cfg.HandsPresent ? Hand(arc, +1) : BasisConstellationHand.None;

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
                                        if (crossSide) summary.CrossSideLeaks++;

                                        if (jitter <= 0f)
                                        {
                                            summary.CleanTrackers++;
                                            if (correct) summary.CleanCorrect++;
                                            if (arc.Core) { summary.CoreCleanTrackers++; if (correct) summary.CoreCleanCorrect++; }
                                        }
                                        if (correct && !float.IsNaN(margin) && margin < minCorrectMargin) minCorrectMargin = margin;
                                        if (!correct) AddConfusion(confusion, trueRole, assigned ? (int)gotRole : -1);

                                        sb.Clear();
                                        sb.Append(scenarioId).Append(',').Append(arc.Name).Append(',').Append(arc.Core ? 1 : 0).Append(',')
                                          .Append(preset.Name).Append(',').Append(F(jitter)).Append(',').Append(seed).Append(',')
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
                                                ft.Write(F(jitter)); ft.Write(','); ft.Write(seed); ft.Write(','); ft.Write(trueRole.ToString()); ft.Write(',');
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

                    summary.NearOriginLeaks = RunStressScenarios(w, sb, cfg, ref scenarioId);

                    if (cfg.IncludeInvariancePass)
                    {
                        RunInvariancePass(cfg, ref summary);
                    }
                }

                ft?.Dispose();

                summary.MinCorrectMargin = float.IsPositiveInfinity(minCorrectMargin) ? 0f : minCorrectMargin;
                summary.TopConfusions = FormatConfusions(confusion);
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
        private static int RunStressScenarios(StreamWriter w, StringBuilder sb, BasisTrackerPlacementSweepConfig cfg, ref int scenarioId)
        {
            int nearOriginLeaks = 0;
            BasisBodyArchetype arc = Archetypes[0];

            // 1) A full set plus one stale device polled at the origin. The stale one must stay unassigned.
            {
                var rng = new System.Random(1234567);
                BasisConstellationSample[] samples = BuildSamples(arc, Presets[2].Roles, 0f, cfg.EyeHeight, rng, out BasisBoneTrackedRole[] trueRoles);
                int n = samples.Length;
                System.Array.Resize(ref samples, n + 1);
                System.Array.Resize(ref trueRoles, n + 1);
                samples[n] = new BasisConstellationSample { HeightRatio = 0f, LateralRatio = 0f, DepthLocal = 0f, NearOrigin = true, ForcedRole = -1 };
                trueRoles[n] = BasisBoneTrackedRole.CenterEye; // sentinel "should not bind"

                BasisConstellationResult r = BasisConstellationClassifier.Classify(samples, samples.Length,
                    cfg.HandsPresent ? Hand(arc, -1) : BasisConstellationHand.None,
                    cfg.HandsPresent ? Hand(arc, +1) : BasisConstellationHand.None, cfg.Tolerance, AllRolesEnabled);

                if (r.TryGetRole(n, out BasisBoneTrackedRole leaked))
                {
                    nearOriginLeaks++;
                    WriteStressRow(w, sb, scenarioId, "stress-nearorigin", "stale-origin", leaked.ToString(), false);
                }
                else
                {
                    WriteStressRow(w, sb, scenarioId, "stress-nearorigin", "(none)", "(none)", true);
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
                WriteStressRow(w, sb, scenarioId, "stress-dupchest", "chestBinds=" + chestCount, chestCount <= 1 ? "ok" : "DUPLICATE", chestCount <= 1);
                scenarioId++;
            }

            return nearOriginLeaks;
        }

        // Re-runs every clean (jitter 0) archetype x set across the configured eye heights and counts
        // any change in the assignment vector. The classifier is ratio-based, so the only height-
        // sensitive path is the absolute toe-forward epsilon; a non-zero count localizes exactly that.
        private static void RunInvariancePass(BasisTrackerPlacementSweepConfig cfg, ref BasisTrackerPlacementSummary summary)
        {
            float[] eyes = cfg.InvarianceEyeHeights;
            if (eyes == null || eyes.Length < 2) return;

            for (int a = 0; a < Archetypes.Length; a++)
            {
                for (int p = 0; p < Presets.Length; p++)
                {
                    int[] baseline = null;
                    for (int e = 0; e < eyes.Length; e++)
                    {
                        var rng = new System.Random(424242 + a * 131 + p);
                        BasisConstellationSample[] samples = BuildSamples(Archetypes[a], Presets[p].Roles, 0f, eyes[e], rng, out _);
                        BasisConstellationResult r = BasisConstellationClassifier.Classify(samples, samples.Length,
                            cfg.HandsPresent ? Hand(Archetypes[a], -1) : BasisConstellationHand.None,
                            cfg.HandsPresent ? Hand(Archetypes[a], +1) : BasisConstellationHand.None, cfg.Tolerance, AllRolesEnabled);

                        if (baseline == null)
                        {
                            baseline = (int[])r.AssignedRole.Clone();
                        }
                        else
                        {
                            summary.InvarianceChecks++;
                            for (int i = 0; i < baseline.Length && i < r.AssignedRole.Length; i++)
                            {
                                if (baseline[i] != r.AssignedRole[i]) { summary.InvarianceDiffs++; break; }
                            }
                        }
                    }
                }
            }
        }

        private static BasisConstellationSample[] BuildSamples(BasisBodyArchetype arc, BasisBoneTrackedRole[] roles, float jitter, float eyeHeight, System.Random rng, out BasisBoneTrackedRole[] trueRoles)
        {
            var samples = new BasisConstellationSample[roles.Length];
            trueRoles = new BasisBoneTrackedRole[roles.Length];
            for (int i = 0; i < roles.Length; i++)
            {
                Placement(arc, roles[i], out float h, out float lat, out float depthRatio);
                if (jitter > 0f)
                {
                    h += (float)NextGaussian(rng) * jitter;
                    lat += (float)NextGaussian(rng) * jitter;
                    depthRatio += (float)NextGaussian(rng) * jitter;
                }
                samples[i] = new BasisConstellationSample
                {
                    HeightRatio = h,
                    LateralRatio = lat,
                    DepthLocal = depthRatio * eyeHeight,
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

        private static BasisConstellationHand Hand(BasisBodyArchetype arc, int sideSign)
        {
            return new BasisConstellationHand { Present = true, HeightRatio = arc.HandH, LateralRatio = sideSign * arc.HandLat };
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

        private static void WriteStressRow(StreamWriter w, StringBuilder sb, int scenarioId, string set, string trueOrNote, string assigned, bool ok)
        {
            sb.Clear();
            sb.Append(scenarioId).Append(",stress,0,").Append(set).Append(",0,0,").Append(trueOrNote)
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
