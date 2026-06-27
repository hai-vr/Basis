using System.Collections.Generic;
using Basis.Scripts.Networking.Sync;
using Basis.Scripts.Networking.Sync.Testing;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using static Basis.Tests.Sync.BasisSyncTestSupport;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// Coverage for the interactions that only exist once several objects share the wire: the real batch
    /// coalesce/demux through impairment, the real Burst interpolation over a multi-slot driver SoA (vs the
    /// managed mirror, at non-zero bases and with an inactive slot), the late-joiner that recovers via a keyframe,
    /// and the owner cadence (idle keyframe backoff, rotation send threshold) the wire actually carries.
    /// </summary>
    public class BasisSyncMultiObjectTests
    {
        // ── Batched scene: coalesce → wire → demux preserves every object ──

        [Test]
        public void Scene_Batched_PreservesEveryObject_AcrossFaithfulProfiles()
        {
            foreach (string profileName in new[] { "perfect", "loss-light", "loss-heavy", "duplicate", "reorder", "jitter-heavy", "bad-wifi" })
            {
                BasisSyncSceneScenario scene = MixedScene(Profile(profileName), SyncMotion.RandomWalk, SceneTransport.Batched, SceneSample.RealJob, seed: 11);
                List<BasisSyncSimResult> results = BasisSyncSceneSim.RunScene(scene);

                Assert.AreEqual(scene.Objects.Count, results.Count, "one result row per object");
                foreach (BasisSyncSimResult r in results)
                {
                    Assert.IsNull(r.Exception, $"{profileName}/{r.FieldConfigName} threw: {r.Exception}");
                    Assert.IsTrue(r.Pass, $"{profileName}/{r.FieldConfigName} failed to converge: {r.FailReason}");
                    Assert.AreEqual("batched", r.Transport);
                    Assert.Greater(r.DemuxEntries, 0, "every object should have had batch sub-payloads routed to it");
                }
            }
        }

        [Test]
        public void Scene_PerPacket_AllObjectsConverge()
        {
            BasisSyncSceneScenario scene = MixedScene(Profile("loss-heavy"), SyncMotion.RandomWalk, SceneTransport.PerPacket, SceneSample.Managed, seed: 3);
            List<BasisSyncSimResult> results = BasisSyncSceneSim.RunScene(scene);
            foreach (BasisSyncSimResult r in results)
                Assert.IsTrue(r.Pass, $"{r.FieldConfigName}: {r.FailReason}");
        }

        [Test]
        public void Scene_Batched_CorruptionAndChaos_NeverThrow_DemuxStaysSafe()
        {
            int runs = 0, exceptions = 0;
            foreach (string profileName in new[] { "corrupt", "chaos" })
            {
                foreach (SyncMotion motion in new[] { SyncMotion.Sine, SyncMotion.RandomWalk })
                {
                    BasisSyncSceneScenario scene = MixedScene(Profile(profileName), motion, SceneTransport.Batched, SceneSample.RealJob, seed: 5);
                    List<BasisSyncSimResult> results = BasisSyncSceneSim.RunScene(scene);
                    foreach (BasisSyncSimResult r in results)
                    {
                        runs++;
                        if (r.Exception != null) { exceptions++; TestContext.WriteLine($"[scene-decode] {profileName}/{motion}/{r.FieldConfigName}: {r.Exception}"); }
                    }
                }
            }
            TestContext.WriteLine($"scene corruption/chaos: {runs} object-runs, {exceptions} decode exceptions");
            Assert.AreEqual(0, exceptions, "the batch demux + receiver must tolerate a corrupted shared wire without throwing");
        }

        // ── Late join: a receiver attaches mid-stream and only recovers via the forced keyframe ──

        [Test]
        public void Scene_LateJoiner_RecoversViaKeyframe()
        {
            BasisSyncSceneScenario scene = MixedScene(Profile("reorder"), SyncMotion.Sine, SceneTransport.Batched, SceneSample.RealJob, seed: 9);
            scene.LateJoinFrame = 100;
            List<BasisSyncSimResult> results = BasisSyncSceneSim.RunScene(scene);

            foreach (BasisSyncSimResult r in results)
            {
                Assert.IsNull(r.Exception, $"{r.FieldConfigName} threw: {r.Exception}");
                Assert.IsTrue(r.Pass, $"late-join {r.FieldConfigName} failed to converge: {r.FailReason}");
                Assert.Greater(r.Keyframes, 0, "a late joiner only recovers because the owner forced a keyframe on join");
            }
        }

        // ── Real multi-slot Burst job vs managed mirror, at non-zero bases ──

        [Test]
        public void RealJob_MultiSlot_MatchesManagedMirror()
        {
            BasisSyncSchema[] schemas = { S0(), S1(), S2() };
            int n = schemas.Length;
            var cur = new BasisSyncValues[n];
            var next = new BasisSyncValues[n];
            var t = new[] { 0.37f, 0.6f, 0.12f };
            var active = new[] { true, true, true };
            for (int i = 0; i < n; i++) { cur[i] = Values(schemas[i]); next[i] = Values(schemas[i]); FillDistinct(schemas[i], cur[i], next[i], i); }

            var managed = new BasisSyncValues[n];
            for (int i = 0; i < n; i++) { managed[i] = Values(schemas[i]); BasisSyncSim.Interpolate(schemas[i], cur[i], next[i], t[i], managed[i]); }

            BasisSyncRealInterp.Layout layout = BasisSyncRealInterp.BuildLayout(schemas);
            var real = new BasisSyncValues[n];
            for (int i = 0; i < n; i++) real[i] = Values(schemas[i]);
            BasisSyncRealInterp.Run(layout, cur, next, t, active, real);

            for (int i = 0; i < n; i++)
            {
                for (int c = 0; c < schemas[i].ContCount; c++)
                    Assert.AreEqual(managed[i].Cont[c], real[i].Cont[c], 1e-4f, $"object {i} continuous slot {c} diverged from the managed mirror");
                for (int r = 0; r < schemas[i].RotCount; r++)
                    Assert.LessOrEqual(QuatAngle(managed[i].Rot[r], real[i].Rot[r]), 0.01f, $"object {i} rotation slot {r} diverged");
                for (int d = 0; d < schemas[i].DiscCount; d++)
                    Assert.AreEqual(managed[i].Disc[d], real[i].Disc[d], $"object {i} discrete slot {d} diverged");
            }
        }

        [Test]
        public void RealJob_InactiveSlot_LeavesOutputUntouched()
        {
            BasisSyncSchema[] schemas = { S0(), S1(), S2() };
            int n = schemas.Length;
            var cur = new BasisSyncValues[n];
            var next = new BasisSyncValues[n];
            var t = new[] { 0.4f, 0.4f, 0.4f };
            var active = new[] { true, false, true };   // middle object inactive
            for (int i = 0; i < n; i++) { cur[i] = Values(schemas[i]); next[i] = Values(schemas[i]); FillDistinct(schemas[i], cur[i], next[i], i); }

            var real = new BasisSyncValues[n];
            for (int i = 0; i < n; i++) real[i] = Values(schemas[i]);
            // Seed the inactive slot's output with sentinels the job must not overwrite.
            real[1].Cont[0] = 123.5f;
            real[1].Disc[0] = 4242;

            BasisSyncRealInterp.Layout layout = BasisSyncRealInterp.BuildLayout(schemas);
            BasisSyncRealInterp.Run(layout, cur, next, t, active, real);

            Assert.AreEqual(123.5f, real[1].Cont[0], 0f, "an inactive slot's output must be left untouched (Active==0 skip)");
            Assert.AreEqual(4242, real[1].Disc[0], "an inactive slot's discrete output must be left untouched");

            // The active neighbours still interpolate correctly (their windows are isolated from the skipped slot).
            var m0 = Values(schemas[0]); BasisSyncSim.Interpolate(schemas[0], cur[0], next[0], t[0], m0);
            for (int c = 0; c < schemas[0].ContCount; c++)
                Assert.AreEqual(m0.Cont[c], real[0].Cont[c], 1e-4f, $"active neighbour slot {c} corrupted by the inactive slot");
        }

        // ── Owner cadence the wire actually carries ──

        [Test]
        public void IdleKeyframeBackoff_StaticObject_SendsFewerKeyframes_StillConverges()
        {
            BasisSyncSimResult withBackoff = BasisSyncSim.Run(SingleScenario(BasisSyncFieldType.Float, SyncMotion.Static, "perfect", seed: 4, backoff: true));
            BasisSyncSimResult without = BasisSyncSim.Run(SingleScenario(BasisSyncFieldType.Float, SyncMotion.Static, "perfect", seed: 4, backoff: false));

            Assert.IsTrue(withBackoff.Pass, $"backoff still has to converge: {withBackoff.FailReason}");
            Assert.IsTrue(without.Pass, $"fixed cadence converges: {without.FailReason}");
            Assert.AreEqual(withBackoff.Keyframes, withBackoff.PacketsSent, "a static value never emits a delta");
            Assert.Less(withBackoff.Keyframes, without.Keyframes, "idle backoff must re-send fewer keyframes than the fixed cadence");
        }

        [Test]
        public void RotationSendThreshold_GatesDeltasByAngle()
        {
            var schema = new BasisSyncSchema();
            schema.AddRotation(true, 9);
            schema.Lock();

            var sender = new SimSender(schema)
            {
                SendInterval = 0.05f,
                KeyframeInterval = 100f, // keep periodic keyframes out of the way
                RotationSendThresholdDegrees = 5f,
            };

            double t = 0.05;
            sender.Local.Rot[0] = Quat(0, 0, 0);
            SimPacket initial = sender.Tick(t);
            Assert.IsNotNull(initial, "the first tick sends the initial keyframe");
            Assert.IsTrue(initial.Reliable, "first send is a keyframe");

            t += 0.05;
            sender.Local.Rot[0] = Quat(0, 1f, 0); // ~1deg < 5deg threshold
            Assert.IsNull(sender.Tick(t), "a sub-threshold rotation must not emit a delta");

            t += 0.05;
            sender.Local.Rot[0] = Quat(0, 20f, 0); // 20deg > threshold
            SimPacket delta = sender.Tick(t);
            Assert.IsNotNull(delta, "an above-threshold rotation must emit");
            Assert.IsFalse(delta.Reliable, "a rotation change is a delta, not a keyframe");
        }

        // ── Matrix wiring ──

        [Test]
        public void Matrix_EnumeratesScenes_AndEachYieldsRowPerObject()
        {
            BasisSyncSimMatrix.MatrixOptions o = BasisSyncSimMatrix.MatrixOptions.Quick();
            List<BasisSyncSceneScenario> scenes = BasisSyncSimMatrix.EnumerateScenes(o);
            Assert.Greater(scenes.Count, 0, "the quick matrix should enumerate multi-object scenes");

            List<BasisSyncSimResult> rows = BasisSyncSceneSim.RunScene(scenes[0]);
            Assert.AreEqual(scenes[0].Objects.Count, rows.Count, "a scene yields one result row per object");

            o.MultiObjectScenes = false;
            Assert.AreEqual(0, BasisSyncSimMatrix.EnumerateScenes(o).Count, "disabling scenes enumerates none");
        }

        [Test]
        public void Scene_IsDeterministic_SameSeedSameResults()
        {
            List<BasisSyncSimResult> a = BasisSyncSceneSim.RunScene(MixedScene(Profile("bad-wifi"), SyncMotion.RandomWalk, SceneTransport.Batched, SceneSample.RealJob, seed: 4242));
            List<BasisSyncSimResult> b = BasisSyncSceneSim.RunScene(MixedScene(Profile("bad-wifi"), SyncMotion.RandomWalk, SceneTransport.Batched, SceneSample.RealJob, seed: 4242));

            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Pass, b[i].Pass, $"object {i} pass not deterministic");
                Assert.AreEqual(a[i].PacketsSent, b[i].PacketsSent, $"object {i} packets-sent not deterministic");
                Assert.AreEqual(a[i].DemuxEntries, b[i].DemuxEntries, $"object {i} demux not deterministic");
                Assert.AreEqual(a[i].MaxContError, b[i].MaxContError, 1e-6f, $"object {i} error metric not deterministic");
            }
        }

        // ── helpers ──

        static NetworkProfile Profile(string name) => BasisSyncSimMatrix.BuildProfiles().Find(p => p.Name == name);

        static BasisSyncSceneScenario MixedScene(NetworkProfile profile, SyncMotion motion, SceneTransport transport, SceneSample sample, int seed)
        {
            return new BasisSyncSceneScenario
            {
                Name = $"test/{motion}/{profile.Name}",
                Objects = new List<BasisSyncSceneObjectSpec>
                {
                    new BasisSyncSceneObjectSpec("xform", new List<SimFieldSpec>
                    {
                        new SimFieldSpec(BasisSyncFieldType.Position),
                        new SimFieldSpec(BasisSyncFieldType.Rotation),
                        new SimFieldSpec(BasisSyncFieldType.Scale),
                    }),
                    new BasisSyncSceneObjectSpec("params", new List<SimFieldSpec>
                    {
                        new SimFieldSpec(BasisSyncFieldType.Float),
                        new SimFieldSpec(BasisSyncFieldType.Vector2),
                        new SimFieldSpec(BasisSyncFieldType.Color, true, true),
                        new SimFieldSpec(BasisSyncFieldType.Bool),
                    }),
                    new BasisSyncSceneObjectSpec("door", new List<SimFieldSpec>
                    {
                        new SimFieldSpec(BasisSyncFieldType.Angle),
                    }),
                },
                Motion = motion,
                Profile = profile,
                Seed = seed,
                Transport = transport,
                Sample = sample,
                IdleKeyframeBackoff = true,
                RotationSendThresholdDegrees = 0.5f,
                UseChecksum = true,
                Dt = 1f / 72f,
                DurationSeconds = 4f,
                SettleSeconds = 1.6f,
            };
        }

        static BasisSyncSimScenario SingleScenario(BasisSyncFieldType type, SyncMotion motion, string profile, int seed, bool backoff)
        {
            return new BasisSyncSimScenario
            {
                Name = $"{type}/{motion}/{profile}",
                FieldConfigName = type.ToString(),
                Fields = new List<SimFieldSpec> { new SimFieldSpec(type) },
                Motion = motion,
                Profile = Profile(profile),
                Seed = seed,
                IdleKeyframeBackoff = backoff,
                Dt = 1f / 72f,
                DurationSeconds = 6f,
                SettleSeconds = 1.6f,
            };
        }

        static BasisSyncSchema S0()
        {
            var s = new BasisSyncSchema();
            s.AddField(BasisSyncFieldType.Position);
            s.AddField(BasisSyncFieldType.Angle, true, false);   // wrapped-lerp (mode 2)
            s.AddField(BasisSyncFieldType.Float, false, false);  // snap (mode 0)
            s.AddRotation(true, 9);
            s.AddField(BasisSyncFieldType.Int);
            s.Lock();
            return s;
        }

        static BasisSyncSchema S1()
        {
            var s = new BasisSyncSchema();
            s.AddField(BasisSyncFieldType.Vector2);
            s.AddField(BasisSyncFieldType.Color);
            s.AddField(BasisSyncFieldType.Bool);
            s.AddField(BasisSyncFieldType.Byte);
            s.Lock();
            return s;
        }

        static BasisSyncSchema S2()
        {
            var s = new BasisSyncSchema();
            s.AddField(BasisSyncFieldType.Position);
            s.AddRotation(true, 9);
            s.AddRotation(true, 9);   // two rotations -> RotCount > 1, exercises the rot base
            s.AddField(BasisSyncFieldType.Scale);
            s.AddField(BasisSyncFieldType.UShort);
            s.Lock();
            return s;
        }

        static void FillDistinct(BasisSyncSchema s, BasisSyncValues cur, BasisSyncValues next, int obj)
        {
            for (int c = 0; c < s.ContCount; c++) { cur.Cont[c] = 10f + obj * 100f + c; next.Cont[c] = 350f - obj * 40f - c * 3f; }
            for (int r = 0; r < s.RotCount; r++)
            {
                cur.Rot[r] = Quat(10 + obj * 5 + r, 20 - obj * 3, 30 + r);
                next.Rot[r] = Quat(80 - obj * 4, -40 + obj + r, 15 + r * 2);
            }
            for (int d = 0; d < s.DiscCount; d++) { cur.Disc[d] = obj * 7 + d + 1; next.Disc[d] = 900 - obj * 13 - d; }
        }
    }
}
