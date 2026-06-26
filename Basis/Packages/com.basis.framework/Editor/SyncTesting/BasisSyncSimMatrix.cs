using System;
using System.Collections.Generic;
using System.Text;

namespace Basis.Scripts.Networking.Sync.Testing
{
    /// <summary>
    /// Builds and runs the full combinatorial test matrix over the value-sync data path:
    /// field configs (every type x quantize x interpolate, plus composites and transform-style layouts)
    /// x motion patterns x network impairment profiles x optional feature flags (extrapolation,
    /// teleport threshold) x seeds. Feasibility-pruned (e.g. teleport-threshold only on position-first
    /// layouts) so every row is a meaningful combination.
    ///
    /// Driven by the Basis > Networking > Sync Test Matrix editor window.
    /// </summary>
    public static class BasisSyncSimMatrix
    {
        public sealed class FieldConfig
        {
            public string Name;
            public List<SimFieldSpec> Fields;
            public bool PositionFirst;
        }

        public sealed class MatrixOptions
        {
            public bool QuantizeVariants = true;
            public bool InterpolateOffVariants = true;
            public bool ExtrapolateVariant = true;
            public bool TeleportThresholdVariant = true;
            public bool CompositeConfigs = true;

            public int Seeds = 1;
            public int BaseSeed = 0x5A1D;

            public List<SyncMotion> Motions;     // null => all
            public HashSet<string> ProfileNames; // null => all

            public float Dt = 1f / 72f;
            public float DurationSeconds = 6f;
            public float SettleSeconds = 1.8f;

            public static MatrixOptions Quick()
            {
                return new MatrixOptions
                {
                    QuantizeVariants = true,
                    InterpolateOffVariants = false,
                    ExtrapolateVariant = false,
                    TeleportThresholdVariant = true,
                    CompositeConfigs = true,
                    Seeds = 1,
                    DurationSeconds = 4f,
                    SettleSeconds = 1.5f,
                    Motions = new List<SyncMotion> { SyncMotion.Sine, SyncMotion.Step, SyncMotion.Teleport, SyncMotion.RandomWalk },
                    ProfileNames = new HashSet<string> { "perfect", "loss-heavy", "reorder", "bad-wifi", "corrupt", "chaos" },
                };
            }

            public static MatrixOptions Full() => new MatrixOptions();
        }

        // ── Field configs ──
        public static List<FieldConfig> BuildFieldConfigs(MatrixOptions o)
        {
            var list = new List<FieldConfig>();

            AddType(list, o, BasisSyncFieldType.Position, quantizable: true, interpToggle: false);
            AddType(list, o, BasisSyncFieldType.Rotation, quantizable: false, interpToggle: false);
            AddType(list, o, BasisSyncFieldType.Scale, quantizable: true, interpToggle: false);
            AddType(list, o, BasisSyncFieldType.Float, quantizable: true, interpToggle: true);
            AddType(list, o, BasisSyncFieldType.Angle, quantizable: true, interpToggle: true);
            AddType(list, o, BasisSyncFieldType.Vector2, quantizable: true, interpToggle: true);
            AddType(list, o, BasisSyncFieldType.Vector4, quantizable: true, interpToggle: true);
            AddType(list, o, BasisSyncFieldType.Color, quantizable: true, interpToggle: true);
            AddType(list, o, BasisSyncFieldType.Int, quantizable: false, interpToggle: false);
            AddType(list, o, BasisSyncFieldType.UInt, quantizable: false, interpToggle: false);
            AddType(list, o, BasisSyncFieldType.UShort, quantizable: false, interpToggle: false);
            AddType(list, o, BasisSyncFieldType.Bool, quantizable: false, interpToggle: false);
            AddType(list, o, BasisSyncFieldType.Byte, quantizable: false, interpToggle: false);

            if (o.CompositeConfigs)
            {
                list.Add(KitchenSink());
                list.Add(TransformFull(half: false));
                list.Add(TransformFull(half: true));
                list.Add(TransformFullScale());
                list.Add(DoorRotY());
                list.Add(PosOnly());
            }

            return list;
        }

        static void AddType(List<FieldConfig> list, MatrixOptions o, BasisSyncFieldType t, bool quantizable, bool interpToggle)
        {
            bool posFirst = t == BasisSyncFieldType.Position;
            list.Add(One(t.ToString(), t, interpolate: true, quantize: false, posFirst));
            if (quantizable && o.QuantizeVariants)
                list.Add(One(t + "-quant", t, interpolate: true, quantize: true, posFirst));
            if (interpToggle && o.InterpolateOffVariants)
                list.Add(One(t + "-noInterp", t, interpolate: false, quantize: false, posFirst));
        }

        static FieldConfig One(string name, BasisSyncFieldType t, bool interpolate, bool quantize, bool posFirst)
        {
            return new FieldConfig
            {
                Name = name,
                PositionFirst = posFirst,
                Fields = new List<SimFieldSpec> { new SimFieldSpec(t, interpolate, quantize) },
            };
        }

        static FieldConfig KitchenSink()
        {
            return new FieldConfig
            {
                Name = "KitchenSink",
                PositionFirst = true,
                Fields = new List<SimFieldSpec>
                {
                    new SimFieldSpec(BasisSyncFieldType.Position),
                    new SimFieldSpec(BasisSyncFieldType.Rotation),
                    new SimFieldSpec(BasisSyncFieldType.Scale),
                    new SimFieldSpec(BasisSyncFieldType.Float),
                    new SimFieldSpec(BasisSyncFieldType.Angle),
                    new SimFieldSpec(BasisSyncFieldType.Vector2),
                    new SimFieldSpec(BasisSyncFieldType.Vector4, true, true),
                    new SimFieldSpec(BasisSyncFieldType.Color, true, true),
                    new SimFieldSpec(BasisSyncFieldType.Int),
                    new SimFieldSpec(BasisSyncFieldType.UInt),
                    new SimFieldSpec(BasisSyncFieldType.UShort),
                    new SimFieldSpec(BasisSyncFieldType.Bool),
                    new SimFieldSpec(BasisSyncFieldType.Byte),
                },
            };
        }

        static FieldConfig TransformFull(bool half)
        {
            return new FieldConfig
            {
                Name = half ? "Transform-half" : "Transform-full",
                PositionFirst = false,
                Fields = new List<SimFieldSpec>
                {
                    new SimFieldSpec(BasisSyncFieldType.Float, true, half),
                    new SimFieldSpec(BasisSyncFieldType.Float, true, half),
                    new SimFieldSpec(BasisSyncFieldType.Float, true, half),
                    new SimFieldSpec(BasisSyncFieldType.Rotation),
                },
            };
        }

        static FieldConfig TransformFullScale()
        {
            var c = TransformFull(half: false);
            c.Name = "Transform-full+scale";
            c.Fields.Add(new SimFieldSpec(BasisSyncFieldType.Float));
            c.Fields.Add(new SimFieldSpec(BasisSyncFieldType.Float));
            c.Fields.Add(new SimFieldSpec(BasisSyncFieldType.Float));
            return c;
        }

        static FieldConfig DoorRotY()
        {
            return new FieldConfig
            {
                Name = "Door-rotY",
                PositionFirst = false,
                Fields = new List<SimFieldSpec> { new SimFieldSpec(BasisSyncFieldType.Angle) },
            };
        }

        static FieldConfig PosOnly()
        {
            return new FieldConfig
            {
                Name = "Pos-xyz",
                PositionFirst = false,
                Fields = new List<SimFieldSpec>
                {
                    new SimFieldSpec(BasisSyncFieldType.Float),
                    new SimFieldSpec(BasisSyncFieldType.Float),
                    new SimFieldSpec(BasisSyncFieldType.Float),
                },
            };
        }

        // ── Network profiles ──
        public static List<NetworkProfile> BuildProfiles()
        {
            return new List<NetworkProfile>
            {
                new NetworkProfile { Name = "perfect" },
                new NetworkProfile { Name = "loss-light", DeltaLoss = 0.05f },
                new NetworkProfile { Name = "loss-heavy", DeltaLoss = 0.40f },
                new NetworkProfile { Name = "duplicate", DuplicateProb = 0.25f },
                new NetworkProfile { Name = "reorder", BaseLatencySec = 0.02f, JitterSec = 0.08f },
                new NetworkProfile { Name = "jitter-heavy", BaseLatencySec = 0.03f, JitterSec = 0.15f },
                new NetworkProfile { Name = "bad-wifi", DeltaLoss = 0.20f, DuplicateProb = 0.10f, BaseLatencySec = 0.04f, JitterSec = 0.12f },
                new NetworkProfile { Name = "corrupt", DeltaLoss = 0.02f, CorruptProb = 0.08f },
                new NetworkProfile { Name = "chaos", DeltaLoss = 0.35f, DuplicateProb = 0.20f, CorruptProb = 0.10f, BaseLatencySec = 0.03f, JitterSec = 0.20f, ImpairReliable = true, HardConvergence = false },
            };
        }

        static readonly SyncMotion[] AllMotions =
        {
            SyncMotion.Static, SyncMotion.Ramp, SyncMotion.Sine, SyncMotion.Step, SyncMotion.Teleport, SyncMotion.RandomWalk,
        };

        // ── Enumeration ──
        public static List<BasisSyncSimScenario> Enumerate(MatrixOptions o)
        {
            var scenarios = new List<BasisSyncSimScenario>();
            List<FieldConfig> configs = BuildFieldConfigs(o);
            List<NetworkProfile> profiles = BuildProfiles();
            if (o.ProfileNames != null) profiles = profiles.FindAll(p => o.ProfileNames.Contains(p.Name));
            SyncMotion[] motions = o.Motions != null ? o.Motions.ToArray() : AllMotions;

            for (int ci = 0; ci < configs.Count; ci++)
            {
                FieldConfig cfg = configs[ci];
                foreach (SyncMotion motion in motions)
                {
                    foreach (NetworkProfile profile in profiles)
                    {
                        for (int seedIx = 0; seedIx < Math.Max(1, o.Seeds); seedIx++)
                        {
                            int seed = unchecked(o.BaseSeed + seedIx * 100003 + Hash(cfg.Name) * 31 + (int)motion * 7 + Hash(profile.Name));

                            scenarios.Add(MakeScenario(cfg, motion, profile, seed, o, extrapolate: false, teleport: false, suffix: ""));

                            if (o.ExtrapolateVariant && (motion == SyncMotion.Sine || motion == SyncMotion.RandomWalk || motion == SyncMotion.Step))
                                scenarios.Add(MakeScenario(cfg, motion, profile, seed ^ 0x1234, o, extrapolate: true, teleport: false, suffix: "+extrap"));

                            if (o.TeleportThresholdVariant && cfg.PositionFirst && (motion == SyncMotion.Teleport || motion == SyncMotion.Step))
                                scenarios.Add(MakeScenario(cfg, motion, profile, seed ^ 0x55AA, o, extrapolate: false, teleport: true, suffix: "+teleThresh"));
                        }
                    }
                }
            }

            return scenarios;
        }

        static BasisSyncSimScenario MakeScenario(FieldConfig cfg, SyncMotion motion, NetworkProfile profile, int seed, MatrixOptions o, bool extrapolate, bool teleport, string suffix)
        {
            return new BasisSyncSimScenario
            {
                Name = $"{cfg.Name}/{motion}/{profile.Name}{suffix}",
                FieldConfigName = cfg.Name + suffix,
                Fields = cfg.Fields,
                Motion = motion,
                Profile = profile,
                Seed = seed,
                Extrapolate = extrapolate,
                TeleportThreshold = teleport,
                Dt = o.Dt,
                DurationSeconds = o.DurationSeconds,
                SettleSeconds = o.SettleSeconds,
            };
        }

        // ── Runner ──
        public static List<BasisSyncSimResult> RunAll(MatrixOptions o, Action<int, int, BasisSyncSimResult> onResult, Func<bool> cancelled)
        {
            List<BasisSyncSimScenario> scenarios = Enumerate(o);
            var results = new List<BasisSyncSimResult>(scenarios.Count);
            for (int i = 0; i < scenarios.Count; i++)
            {
                if (cancelled != null && cancelled()) break;
                BasisSyncSimResult r = BasisSyncSim.Run(scenarios[i]);
                results.Add(r);
                onResult?.Invoke(i + 1, scenarios.Count, r);
            }
            return results;
        }

        public static int CountScenarios(MatrixOptions o) => Enumerate(o).Count;

        // ── CSV ──
        public static string ToCsv(List<BasisSyncSimResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine(BasisSyncSimResult.CsvHeader);
            for (int i = 0; i < results.Count; i++) sb.AppendLine(results[i].ToCsvRow());
            return sb.ToString();
        }

        static int Hash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int h = 17;
            for (int i = 0; i < s.Length; i++) h = unchecked(h * 31 + s[i]);
            return h & 0x7FFFFFFF;
        }
    }
}
