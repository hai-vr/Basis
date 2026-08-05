using Basis.Scripts.Drivers;
using NUnit.Framework;
using System.Diagnostics;
using Unity.Mathematics;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// What the grid costs to build and to sample.
    ///
    /// This matters because v47 moved the bake onto the REMOTE avatar load path. It was previously
    /// paid once, for the local player, during calibration; now every distinct avatar in the
    /// instance pays it. A bake instantiates a hidden duplicate of the rig and runs 441
    /// SetHumanPose calls, so "how bad is that" stopped being rhetorical.
    ///
    /// The ceilings are deliberately loose. A wall-clock assertion tight enough to be interesting
    /// would flake on a loaded CI machine, so these only catch a catastrophic regression — an
    /// accidental per-cell allocation, a bake that stopped hitting the cache — while the logged
    /// figures carry the actual signal for anyone reading the run.
    /// </summary>
    public class BasisHandPoseGridCostTests
    {
        const int Fingers = BasisHandPoseGrid.FingerCount;
        const int Joints = BasisHandPoseGrid.JointsPerFinger;

        [Test]
        public void BakeCost_IsBoundedAndReported()
        {
            using var rig = BasisHumanoidRigFixture.Build("cost");

            // One warm bake first: the very first HumanPoseHandler in a domain pays one-time setup
            // that has nothing to do with the grid.
            using (var warm = new BasisHandPoseGrid())
            {
                Assert.IsTrue(warm.TryBake(rig.Animator, BasisHandPoseGrid.DefaultIncrement, out _));
            }

            var sw = Stopwatch.StartNew();
            using var grid = new BasisHandPoseGrid();
            Assert.IsTrue(grid.TryBake(rig.Animator, BasisHandPoseGrid.DefaultIncrement, out _));
            sw.Stop();

            int cells = grid.Cells.Length;
            UnityEngine.Debug.Log(
                $"[finger] grid bake: {sw.Elapsed.TotalMilliseconds:F1} ms for " +
                $"{grid.GridWidth}x{grid.GridHeight} x {Fingers} fingers x {Joints} joints " +
                $"({cells} cells, {cells * 16 / 1024} KB)");

            Assert.Less(sw.Elapsed.TotalMilliseconds, 2000.0,
                "grid bake got catastrophically slower; it runs on every distinct remote avatar load");
        }

        [Test]
        public void CacheRestore_IsFarCheaperThanBaking()
        {
            using var rig = BasisHumanoidRigFixture.Build("cached");
            using var source = new BasisHandPoseGrid();
            Assert.IsTrue(source.TryBake(rig.Animator, BasisHandPoseGrid.DefaultIncrement, out var bake));

            var snapshot = new BasisAvatarModelCache.HandPoseGridData
            {
                NativeGridSnapshot = source.ToSnapshot(),
                GridWidth = source.GridWidth,
                GridHeight = source.GridHeight,
                FingerStride = source.FingerStride,
                TotalElements = source.Cells.Length,
                Increment = source.Increment,
                InitialPose = bake.RestPose,
            };

            var sw = Stopwatch.StartNew();
            using var restored = new BasisHandPoseGrid();
            restored.RestoreFrom(snapshot);
            sw.Stop();

            UnityEngine.Debug.Log($"[finger] grid cache restore: {sw.Elapsed.TotalMilliseconds:F2} ms");

            Assert.IsTrue(restored.IsCreated);
            Assert.AreEqual(source.Cells.Length, restored.Cells.Length);
            Assert.Less(sw.Elapsed.TotalMilliseconds, 200.0,
                "cache restore should be a memcpy-shaped cost; a crowd in matching avatars depends on it");

            // A restore that quietly produced different cells would make one player's hands disagree
            // with another's while both looked individually plausible.
            for (int i = 0; i < source.Cells.Length; i++)
            {
                float4 a = source.Cells[i].value;
                float4 b = restored.Cells[i].value;
                Assert.AreEqual(a.x, b.x, $"cell {i}.x");
                Assert.AreEqual(a.y, b.y, $"cell {i}.y");
                Assert.AreEqual(a.z, b.z, $"cell {i}.z");
                Assert.AreEqual(a.w, b.w, $"cell {i}.w");
            }
        }

        /// <summary>
        /// Per-frame cost of the expansion every remote now runs: thirty samples per player.
        /// </summary>
        [Test]
        public void ExpansionCost_ScalesToACrowd()
        {
            using var rig = BasisHumanoidRigFixture.Build("crowd");
            using var grid = new BasisHandPoseGrid();
            Assert.IsTrue(grid.TryBake(rig.Animator, BasisHandPoseGrid.DefaultIncrement, out _));

            const int players = 40;
            const int frames = 60;
            var percentages = new float2[Fingers];
            for (int f = 0; f < Fingers; f++) percentages[f] = new float2(0.31f * f - 1f, 0.13f * f - 0.5f);

            // Untimed pass so JIT is not attributed to the measurement.
            for (int f = 0; f < Fingers; f++)
                for (int j = 0; j < Joints; j++) grid.SampleJoint(f, j, percentages[f]);

            var sw = Stopwatch.StartNew();
            for (int frame = 0; frame < frames; frame++)
            {
                for (int player = 0; player < players; player++)
                {
                    for (int f = 0; f < Fingers; f++)
                        for (int j = 0; j < Joints; j++) grid.SampleJoint(f, j, percentages[f]);
                }
            }
            sw.Stop();

            double perFrame = sw.Elapsed.TotalMilliseconds / frames;
            UnityEngine.Debug.Log(
                $"[finger] expansion: {perFrame:F3} ms/frame for {players} players " +
                $"({players * Fingers * Joints} samples), managed/non-Burst");

            Assert.Less(perFrame, 8.0,
                $"finger expansion cost {perFrame:F3} ms/frame for {players} players");
        }
    }
}
