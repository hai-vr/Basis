using Basis.Scripts.Drivers;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// Guards <see cref="BasisHandPoseSampler"/>, the single copy of the curl/splay → joint rotation
    /// interpolation that the local finger job and (once the finger block lands) every remote
    /// reconstruct through.
    ///
    /// Determinism is the load-bearing property here, and it is not the same claim as accuracy.
    /// The remote apply path skips a localRotation write when the composed rotation is bit-identical
    /// to last frame's, and it names settled fingers as the main beneficiary. A sampler that returns
    /// an almost-equal quaternion for unchanged input would defeat that and dirty every remote's
    /// finger subtree every frame — a frame-time regression with no visual symptom pointing at hands.
    ///
    /// Continuity is the other one: a seam at a grid cell boundary is a visible finger pop, and it is
    /// invisible to any test that only samples at node coordinates.
    /// </summary>
    public class BasisHandPoseSamplerTests
    {
        const float Increment = BasisHandPoseGrid.DefaultIncrement;
        const int Width = 21;   // matches Mathf.RoundToInt(2 / 0.1) + 1
        const int Height = 21;
        const int Fingers = BasisHandPoseGrid.FingerCount;
        const int Joints = BasisHandPoseGrid.JointsPerFinger;
        const int FingerStride = Width * Height * Joints;

        /// <summary>
        /// A grid whose cells vary smoothly and distinctly per finger and joint, so a sampler that
        /// read the wrong finger, joint or axis produces an obviously wrong angle rather than a
        /// plausible one.
        /// </summary>
        static NativeArray<quaternion> BuildGrid(Allocator allocator)
        {
            var cells = new NativeArray<quaternion>(Fingers * FingerStride, allocator);
            for (int finger = 0; finger < Fingers; finger++)
            {
                for (int xi = 0; xi < Width; xi++)
                {
                    for (int yi = 0; yi < Height; yi++)
                    {
                        int gridIdx = xi * Height + yi;
                        for (int joint = 0; joint < Joints; joint++)
                        {
                            float curl = -1f + xi * Increment;
                            float splay = -1f + yi * Increment;
                            float angle = math.radians(40f * curl + 12f * splay + 3f * finger + 7f * joint);
                            cells[finger * FingerStride + gridIdx * Joints + joint] =
                                quaternion.AxisAngle(math.normalize(new float3(1f, 0.3f, -0.2f)), angle);
                        }
                    }
                }
            }
            return cells;
        }

        static quaternion Sample(NativeArray<quaternion> cells, int finger, int joint, float2 pct)
            => BasisHandPoseSampler.SampleJoint(cells, FingerStride, Width, Height, Increment, finger, joint, pct);

        static float AngleDeg(quaternion q)
        {
            float4 v = math.normalize(q).value;
            return math.degrees(2f * math.atan2(math.length(v.xyz), math.abs(v.w)));
        }

        static float AngleBetween(quaternion a, quaternion b)
            => AngleDeg(math.mul(math.normalize(a), math.conjugate(math.normalize(b))));

        [Test]
        public void AtGridNodes_ReturnsTheStoredCell()
        {
            var cells = BuildGrid(Allocator.Temp);
            try
            {
                for (int xi = 0; xi < Width; xi++)
                {
                    for (int yi = 0; yi < Height; yi++)
                    {
                        var pct = new float2(-1f + xi * Increment, -1f + yi * Increment);
                        quaternion expected = cells[3 * FingerStride + (xi * Height + yi) * Joints + 1];
                        quaternion actual = Sample(cells, 3, 1, pct);
                        Assert.Less(AngleBetween(expected, actual), 0.02f,
                            $"node ({xi},{yi}) did not round-trip");
                    }
                }
            }
            finally { cells.Dispose(); }
        }

        [Test]
        public void SameInput_ProducesBitIdenticalOutput()
        {
            var cells = BuildGrid(Allocator.Temp);
            try
            {
                foreach (var pose in BasisFingerCorpus.All())
                {
                    for (int finger = 0; finger < Fingers; finger++)
                    {
                        var pct = new float2(pose.Fingers[finger].x, pose.Fingers[finger].y);
                        for (int joint = 0; joint < Joints; joint++)
                        {
                            float4 first = Sample(cells, finger, joint, pct).value;
                            float4 second = Sample(cells, finger, joint, pct).value;
                            Assert.AreEqual(first.x, second.x, $"{pose.Name} f{finger} j{joint} x");
                            Assert.AreEqual(first.y, second.y, $"{pose.Name} f{finger} j{joint} y");
                            Assert.AreEqual(first.z, second.z, $"{pose.Name} f{finger} j{joint} z");
                            Assert.AreEqual(first.w, second.w, $"{pose.Name} f{finger} j{joint} w");
                        }
                    }
                }
            }
            finally { cells.Dispose(); }
        }

        [Test]
        public void OutOfRangeInput_SaturatesInsteadOfWrapping()
        {
            var cells = BuildGrid(Allocator.Temp);
            try
            {
                for (int finger = 0; finger < Fingers; finger++)
                {
                    quaternion atMin = Sample(cells, finger, 0, new float2(-1f, -1f));
                    quaternion belowMin = Sample(cells, finger, 0, new float2(-4f, -9f));
                    Assert.Less(AngleBetween(atMin, belowMin), 0.02f,
                        $"finger {finger} underflow did not clamp to the low corner");

                    quaternion atMax = Sample(cells, finger, 0, new float2(1f, 1f));
                    quaternion aboveMax = Sample(cells, finger, 0, new float2(6f, 3f));
                    Assert.Less(AngleBetween(atMax, aboveMax), 0.02f,
                        $"finger {finger} overflow did not clamp to the high corner");
                }
            }
            finally { cells.Dispose(); }
        }

        [Test]
        public void EachFingerReadsItsOwnCells()
        {
            var cells = BuildGrid(Allocator.Temp);
            try
            {
                var pct = new float2(0.25f, -0.35f);
                for (int finger = 1; finger < Fingers; finger++)
                {
                    float previous = AngleDeg(Sample(cells, finger - 1, 0, pct));
                    float current = AngleDeg(Sample(cells, finger, 0, pct));
                    Assert.Greater(math.abs(current - previous), 1f,
                        $"finger {finger} sampled the same cells as finger {finger - 1}");
                }
            }
            finally { cells.Dispose(); }
        }

        /// <summary>
        /// Steps four times finer than the tightest wire quantiser the finger block will use, so a
        /// discontinuity at a cell boundary shows up as a step far larger than its neighbours.
        /// </summary>
        [Test]
        public void SweepingCurl_HasNoDiscontinuity()
        {
            var cells = BuildGrid(Allocator.Temp);
            try
            {
                const int steps = 1024;
                for (int finger = 0; finger < Fingers; finger++)
                {
                    for (int joint = 0; joint < Joints; joint++)
                    {
                        quaternion previous = Sample(cells, finger, joint, new float2(-1f, 0.1f));
                        for (int i = 1; i <= steps; i++)
                        {
                            float curl = -1f + 2f * i / steps;
                            quaternion current = Sample(cells, finger, joint, new float2(curl, 0.1f));
                            float step = AngleBetween(previous, current);
                            Assert.Less(step, 1f,
                                $"finger {finger} joint {joint} jumped {step:F3}° at curl {curl:F4}");
                            previous = current;
                        }
                    }
                }
            }
            finally { cells.Dispose(); }
        }

        [Test]
        public void SweepingSplay_HasNoDiscontinuity()
        {
            var cells = BuildGrid(Allocator.Temp);
            try
            {
                const int steps = 1024;
                for (int finger = 0; finger < Fingers; finger++)
                {
                    quaternion previous = Sample(cells, finger, 0, new float2(-0.3f, -1f));
                    for (int i = 1; i <= steps; i++)
                    {
                        float splay = -1f + 2f * i / steps;
                        quaternion current = Sample(cells, finger, 0, new float2(-0.3f, splay));
                        float step = AngleBetween(previous, current);
                        Assert.Less(step, 1f,
                            $"finger {finger} jumped {step:F3}° at splay {splay:F4}");
                        previous = current;
                    }
                }
            }
            finally { cells.Dispose(); }
        }

        [Test]
        public void Output_IsAlwaysUnitLength()
        {
            var cells = BuildGrid(Allocator.Temp);
            try
            {
                foreach (var pose in BasisFingerCorpus.All())
                {
                    for (int finger = 0; finger < Fingers; finger++)
                    {
                        var pct = new float2(pose.Fingers[finger].x, pose.Fingers[finger].y);
                        for (int joint = 0; joint < Joints; joint++)
                        {
                            float length = math.length(Sample(cells, finger, joint, pct).value);
                            Assert.AreEqual(1f, length, 1e-4f, $"{pose.Name} f{finger} j{joint}");
                        }
                    }
                }
            }
            finally { cells.Dispose(); }
        }
    }
}
