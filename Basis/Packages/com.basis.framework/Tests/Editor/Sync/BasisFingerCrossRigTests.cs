using Basis.Scripts.Drivers;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// The claim v47 rests on, tested against real humanoid rigs: nothing about finger geometry
    /// crosses the wire, so a hand pose authored by one player has to come out PROPORTIONALLY right
    /// on whatever avatar the viewer happens to be drawing.
    ///
    /// The mechanism is that curl/splay are muscle-space quantities and each receiver expands them
    /// through a grid baked from its own rig. If that holds, two rigs whose fingers differ in length
    /// by 60% still produce the same joint angles and the same extension fraction for the same wire
    /// values — and, crucially, an avatar scaled 2x produces bit-identical grid cells, because
    /// rotations do not scale.
    ///
    /// These use procedurally built humanoids (BasisHumanoidRigFixture) rather than a project asset
    /// so the finger proportions can be dialled deliberately instead of being whatever some test
    /// avatar happened to ship with.
    /// </summary>
    public class BasisFingerCrossRigTests
    {
        const int Fingers = BasisHandPoseGrid.FingerCount;
        const int Joints = BasisHandPoseGrid.JointsPerFinger;

        /// <summary>Finger 0 is the left thumb, finger 5 the right; both index into a hand's slot 0.</summary>
        static bool IsThumb(int finger) => finger % 5 == 0;

        static float AngleDeg(quaternion q)
        {
            float4 v = math.normalize(q).value;
            return math.degrees(2f * math.atan2(math.length(v.xyz), math.abs(v.w)));
        }

        static float AngleBetween(quaternion a, quaternion b)
            => AngleDeg(math.mul(math.normalize(a), math.conjugate(math.normalize(b))));

        static BasisHandPoseGrid Bake(BasisHumanoidRigFixture rig)
        {
            var grid = new BasisHandPoseGrid();
            Assert.IsTrue(rig.Animator.isHuman, $"{rig.Root.name} did not build as a humanoid");
            Assert.IsTrue(grid.TryBake(rig.Animator, BasisHandPoseGrid.DefaultIncrement, out _),
                $"grid bake failed for {rig.Root.name}");
            Assert.IsTrue(grid.IsCreated);
            return grid;
        }

        /// <summary>Writes a sampled pose onto the rig so world-space measurements can be taken.</summary>
        static void Apply(BasisHumanoidRigFixture rig, BasisHandPoseGrid grid, Vector2[] percentages)
        {
            for (int finger = 0; finger < Fingers; finger++)
            {
                var pct = new float2(percentages[finger].x, percentages[finger].y);
                for (int joint = 0; joint < Joints; joint++)
                {
                    Transform t = rig.FingerJoints[finger * Joints + joint];
                    if (t != null) t.localRotation = grid.SampleJoint(finger, joint, pct);
                }
            }
        }

        /// <summary>
        /// Straight-line span from proximal to distal as a fraction of the two segments it covers.
        /// 1.0 is a straight finger, less is curled. Dimensionless, so it is directly comparable
        /// between rigs of different size — which is the whole point.
        /// </summary>
        static float ExtensionFraction(BasisHumanoidRigFixture rig, int finger)
        {
            Transform proximal = rig.FingerJoints[finger * Joints];
            Transform distal = rig.FingerJoints[finger * Joints + 2];
            float span = Vector3.Distance(proximal.position, distal.position);
            float scale = rig.Root.transform.localScale.x;
            return span / (2f * rig.FingerSegmentLength[finger] * scale);
        }

        // ────────────────────────────────────────────────────────────

        [Test]
        public void Fixture_BuildsAValidHumanoidWithEveryFingerJoint()
        {
            using var rig = BasisHumanoidRigFixture.Build("plain");
            Assert.IsTrue(rig.Avatar.isValid, "procedural avatar is not valid");
            Assert.IsTrue(rig.Avatar.isHuman, "procedural avatar is not humanoid");
            for (int i = 0; i < 30; i++)
            {
                Assert.IsNotNull(rig.FingerJoints[i], $"finger joint {i} missing");
            }
        }

        /// <summary>
        /// Dimensions are asserted against literals, not against each other. The obvious
        /// <c>Cells.Length == FingerCount * FingerStride</c> is satisfied by 0 == 10 * 0, and a bake
        /// that zeroed its own stride before allocating produced exactly that: a grid reporting
        /// IsCreated with no cells, which Burst turns into an abort rather than an exception the
        /// first time the finger job samples it.
        /// </summary>
        [Test]
        public void GridBakes_OnAProcedurallyBuiltRig()
        {
            using var rig = BasisHumanoidRigFixture.Build("bakeable");
            using var grid = Bake(rig);

            Assert.AreEqual(21, grid.GridWidth);
            Assert.AreEqual(21, grid.GridHeight);
            Assert.AreEqual(21 * 21 * Joints, grid.FingerStride);
            Assert.AreEqual(Fingers * 21 * 21 * Joints, grid.Cells.Length);
            Assert.IsTrue(grid.IsCreated);

            for (int i = 0; i < grid.Cells.Length; i++)
            {
                float length = math.length(grid.Cells[i].value);
                Assert.AreEqual(1f, length, 1e-3f, $"cell {i} is not a unit quaternion");
            }
        }

        /// <summary>
        /// Every corpus pose must sample in bounds on a freshly baked grid. This is the shape of the
        /// crash the zeroed-stride bake produced, reproduced without needing Burst to abort the
        /// editor to notice it.
        /// </summary>
        [Test]
        public void EveryCorpusPose_SamplesInBoundsAfterBaking()
        {
            using var rig = BasisHumanoidRigFixture.Build("inbounds");
            using var grid = Bake(rig);

            int cells = grid.Cells.Length;
            foreach (var pose in BasisFingerCorpus.All())
            {
                for (int finger = 0; finger < Fingers; finger++)
                {
                    var pct = new float2(pose.Fingers[finger].x, pose.Fingers[finger].y);
                    for (int joint = 0; joint < Joints; joint++)
                    {
                        // Mirrors the sampler's own indexing so an out-of-range read is a failed
                        // assertion here instead of a Burst abort in the running app.
                        float fx = (pct.x + 1f) / grid.Increment;
                        float fy = (pct.y + 1f) / grid.Increment;
                        int x0 = math.clamp((int)math.floor(fx), 0, grid.GridWidth - 2);
                        int y0 = math.clamp((int)math.floor(fy), 0, grid.GridHeight - 2);
                        int worst = finger * grid.FingerStride
                            + ((x0 + 1) * grid.GridHeight + y0 + 1) * Joints + joint;

                        Assert.Less(worst, cells,
                            $"{pose.Name} finger {finger} joint {joint} would read cell {worst} of {cells}");
                        Assert.DoesNotThrow(() => grid.SampleJoint(finger, joint, pct));
                    }
                }
            }
        }

        /// <summary>
        /// A grid that never baked, or whose bake failed, must not advertise itself as usable — the
        /// drivers gate their Burst dispatch on exactly this property.
        /// </summary>
        [Test]
        public void UnbakedOrDegenerateGrid_IsNotCreated()
        {
            var empty = new BasisHandPoseGrid();
            Assert.IsFalse(empty.IsCreated, "a grid that never baked claims to be usable");

            using var rig = BasisHumanoidRigFixture.Build("disposable");
            var grid = new BasisHandPoseGrid();
            Assert.IsTrue(grid.TryBake(rig.Animator, BasisHandPoseGrid.DefaultIncrement, out _));
            Assert.IsTrue(grid.IsCreated);

            grid.Dispose();
            Assert.IsFalse(grid.IsCreated, "a disposed grid still claims to be usable");
        }

        /// <summary>
        /// Curl is a muscle-space quantity, so the SAME wire value has to bend a stubby finger and a
        /// long finger by the same angle. If this drifts, hands look subtly wrong on every avatar
        /// except the sender's — the exact failure the grid-per-receiver design exists to prevent.
        /// </summary>
        [Test]
        public void SameWireValues_ProduceSameJointAnglesOnDifferentFingerLengths()
        {
            using var shortRig = BasisHumanoidRigFixture.Build("short", fingerScale: 0.65f);
            using var longRig = BasisHumanoidRigFixture.Build("long", fingerScale: 1.6f);
            using var shortGrid = Bake(shortRig);
            using var longGrid = Bake(longRig);

            float worst = 0f;
            string where = "";
            foreach (var pose in BasisFingerCorpus.Expressive())
            {
                for (int finger = 0; finger < Fingers; finger++)
                {
                    var pct = new float2(pose.Fingers[finger].x, pose.Fingers[finger].y);
                    for (int joint = 0; joint < Joints; joint++)
                    {
                        float delta = AngleBetween(
                            shortGrid.SampleJoint(finger, joint, pct),
                            longGrid.SampleJoint(finger, joint, pct));
                        if (delta > worst)
                        {
                            worst = delta;
                            where = $"{pose.Name} finger {finger} joint {joint}";
                        }
                    }
                }
            }

            Assert.Less(worst, 2.0f,
                $"joint angle differed by {worst:F3}° between rigs at {where} — curl is not rig-neutral");
        }

        /// <summary>
        /// The proportional half of the same claim, measured in world space rather than in rotations:
        /// a fist has to close by the same FRACTION of finger length on both rigs, or the long-fingered
        /// avatar's hand ends up visibly more (or less) closed than the sender's.
        /// </summary>
        [Test]
        public void ExtensionFraction_MatchesAcrossFingerLengths()
        {
            using var shortRig = BasisHumanoidRigFixture.Build("short", fingerScale: 0.65f);
            using var longRig = BasisHumanoidRigFixture.Build("long", fingerScale: 1.6f);
            using var shortGrid = Bake(shortRig);
            using var longGrid = Bake(longRig);

            foreach (var pose in BasisFingerCorpus.Expressive())
            {
                Apply(shortRig, shortGrid, pose.Fingers);
                Apply(longRig, longGrid, pose.Fingers);

                for (int finger = 0; finger < Fingers; finger++)
                {
                    float a = ExtensionFraction(shortRig, finger);
                    float b = ExtensionFraction(longRig, finger);
                    Assert.AreEqual(a, b, 0.06f,
                        $"{pose.Name} finger {finger}: short rig extended {a:F3}, long rig {b:F3}");
                }
            }
        }

        /// <summary>
        /// Uniform avatar scale must fall out entirely. Rotations do not scale, so the grids should
        /// be bit-identical — asserted exactly rather than within a tolerance, because a tolerance
        /// would hide exactly how much absolute length had leaked in.
        /// </summary>
        [Test]
        public void UniformAvatarScale_ProducesBitIdenticalGrids()
        {
            using var normal = BasisHumanoidRigFixture.Build("normal", uniformScale: 1f);
            using var giant = BasisHumanoidRigFixture.Build("giant", uniformScale: 2.5f);
            using var normalGrid = Bake(normal);
            using var giantGrid = Bake(giant);

            Assert.AreEqual(normalGrid.Cells.Length, giantGrid.Cells.Length);
            for (int i = 0; i < normalGrid.Cells.Length; i++)
            {
                float4 a = normalGrid.Cells[i].value;
                float4 b = giantGrid.Cells[i].value;
                Assert.AreEqual(a.x, b.x, $"cell {i}.x");
                Assert.AreEqual(a.y, b.y, $"cell {i}.y");
                Assert.AreEqual(a.z, b.z, $"cell {i}.z");
                Assert.AreEqual(a.w, b.w, $"cell {i}.w");
            }
        }

        /// <summary>
        /// A pose is only "correct" if it reads correctly. Flat-open must actually be straight and a
        /// fist must actually be closed — per-joint angle error can pass while the hand shape does
        /// not, which is the failure mode a rotation-only assertion cannot see.
        /// </summary>
        [Test]
        public void FlatOpenIsStraight_AndFistIsClosed()
        {
            using var rig = BasisHumanoidRigFixture.Build("shapes");
            using var grid = Bake(rig);

            Vector2[] flat = null, fist = null;
            foreach (var pose in BasisFingerCorpus.Expressive())
            {
                if (pose.Name == "flat-open") flat = pose.Fingers;
                if (pose.Name == "fist") fist = pose.Fingers;
            }
            Assert.IsNotNull(flat);
            Assert.IsNotNull(fist);

            Apply(rig, grid, flat);
            var flatExtension = new float[Fingers];
            for (int finger = 0; finger < Fingers; finger++) flatExtension[finger] = ExtensionFraction(rig, finger);

            Apply(rig, grid, fist);
            var fistExtension = new float[Fingers];
            for (int finger = 0; finger < Fingers; finger++) fistExtension[finger] = ExtensionFraction(rig, finger);

            var report = new System.Text.StringBuilder();
            for (int finger = 0; finger < Fingers; finger++)
            {
                report.AppendLine($"  finger {finger}{(IsThumb(finger) ? " (thumb)" : "")}: " +
                    $"flat {flatExtension[finger]:F3}, fist {fistExtension[finger]:F3}");
            }

            for (int finger = 0; finger < Fingers; finger++)
            {
                Assert.Greater(flatExtension[finger], 0.90f,
                    $"finger {finger} is not straight when flat-open\n{report}");

                // The thumb travels less than the fingers do: Unity's thumb muscles spend part of
                // their range on opposition rather than flexion, so its span between open and closed
                // is genuinely smaller. The assertion is still that closing visibly closes it; only
                // the amount expected differs.
                float minimumTravel = IsThumb(finger) ? 0.10f : 0.15f;
                Assert.Less(fistExtension[finger], flatExtension[finger] - minimumTravel,
                    $"finger {finger} barely curled for a fist\n{report}");
            }
        }

        /// <summary>
        /// Curl has to move the hand in one direction across its whole range. A grid that folded back
        /// on itself would read as a finger that opens, closes slightly, then opens again: an
        /// artefact only visible while someone slowly releases a trigger.
        ///
        /// Curl runs CURLED -> OPEN, so extension rises. An earlier version asserted the opposite
        /// direction with a 0.02 tolerance, which exceeded the real per-step change (~0.006 over 40
        /// steps) and so could never fail - it passed while claiming the reverse of what it measured.
        /// The travel assertion at the end is what stops that from recurring silently.
        /// </summary>
        [Test]
        public void SweepingCurl_OpensMonotonically()
        {
            using var rig = BasisHumanoidRigFixture.Build("sweep");
            using var grid = Bake(rig);

            var pose = new Vector2[Fingers];
            const int steps = 40;

            for (int finger = 0; finger < Fingers; finger++)
            {
                float previous = float.MinValue;
                float first = 0f, last = 0f;

                for (int i = 0; i <= steps; i++)
                {
                    float curl = -1f + 2f * i / steps;
                    for (int f = 0; f < Fingers; f++) pose[f] = new Vector2(curl, 0f);
                    Apply(rig, grid, pose);

                    float extension = ExtensionFraction(rig, finger);
                    // 0.005 is half a percent of finger length, about 0.1 mm on a real hand: far
                    // below anything visible, and above the ~0.001 wobble the thumb shows near full
                    // extension where its muscle mapping saturates and the bilinear cells flatten.
                    // The travel assertion below is what keeps this from degenerating into a test
                    // that tolerates everything.
                    Assert.GreaterOrEqual(extension, previous - 0.005f,
                        $"finger {finger} reversed at curl {curl:F3} ({previous:F3} -> {extension:F3})");
                    previous = extension;
                    if (i == 0) first = extension;
                    last = extension;
                }

                Assert.Greater(last - first, 0.08f,
                    $"finger {finger} barely moved across the whole curl range ({first:F3} -> {last:F3})");
            }
        }
    }
}
