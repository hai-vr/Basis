using NUnit.Framework;
using Unity.Mathematics;

namespace GatorDragonGames.JigglePhysics.Tests {

[TestFixture]
internal unsafe class JiggleJobSimulateCacheTests {
    private JiggleSimHarness harness;

    [TearDown]
    public void TearDown() {
        harness?.Dispose();
        harness = null;
    }

    [Test]
    public void VirtualRoot_ExtrapolatesBackwardsFromFirstTwoRealPoints() {
        var tree = JiggleTestTree.Chain(2, new float3(0f, 1f, 0f), new float3(1f, 0f, 0f), 0.5f);
        harness = new JiggleSimHarness(tree);

        harness.Step();

        var root = harness.Point(0);
        JiggleAssert.AreEqual(new float3(-0.5f, 1f, 0f), root.pose, JiggleTestFactory.Tolerance);
        JiggleAssert.AreEqual(new float3(-1f, 1f, 0f), root.parentPose, JiggleTestFactory.Tolerance);
        Assert.AreEqual(0.5f, root.desiredLengthToParent, JiggleTestFactory.Tolerance);
        Assert.AreEqual(0f, root.worldRadius, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void VirtualRoot_IsolatedBone_UsesQuarterMetreFallback() {
        var tree = JiggleTestTree.SingleBone(new float3(2f, 3f, 4f));
        harness = new JiggleSimHarness(tree);

        harness.Step();

        var root = harness.Point(0);
        JiggleAssert.AreEqual(new float3(2f, 2.75f, 4f), root.pose, JiggleTestFactory.Tolerance);
        JiggleAssert.AreEqual(new float3(2f, 2.5f, 4f), root.parentPose, JiggleTestFactory.Tolerance);
        Assert.AreEqual(0.25f, root.desiredLengthToParent, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void RealPoint_PoseTracksInputPose() {
        var tree = JiggleTestTree.Chain(2, float3.zero, new float3(0f, -1f, 0f), 1f);
        harness = new JiggleSimHarness(tree);
        harness.inputPoses[2] = JiggleTestFactory.Pose(new float3(0.25f, -1.5f, 0f));

        harness.Step();

        JiggleAssert.AreEqual(new float3(0.25f, -1.5f, 0f), harness.Point(2).pose, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void RealPoint_ParentPoseAndDesiredLengthDeriveFromParent() {
        var tree = JiggleTestTree.Chain(2, float3.zero, new float3(0f, -1f, 0f), 1f);
        harness = new JiggleSimHarness(tree);
        harness.inputPoses[2] = JiggleTestFactory.Pose(new float3(0f, -3f, 0f));

        harness.Step();

        var point = harness.Point(2);
        JiggleAssert.AreEqual(harness.Point(1).pose, point.parentPose, JiggleTestFactory.Tolerance);
        Assert.AreEqual(3f, point.desiredLengthToParent, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void WorldRadius_ScalesByAverageOfInputScaleComponents() {
        var tree = JiggleTestTree.Chain(2, float3.zero, new float3(1f, 0f, 0f), 1f,
            JiggleTestFactory.Params(collisionRadius: 0.1f));
        harness = new JiggleSimHarness(tree);
        harness.inputPoses[2] = new JiggleTransform {
            isVirtual = false, position = new float3(1f, 0f, 0f),
            rotation = quaternion.identity, scale = new float3(2f, 4f, 6f),
        };

        harness.Step();

        Assert.AreEqual(0.4f, harness.Point(2).worldRadius, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void WorldRadius_UsesAbsoluteScale_SoMirroredRigsStayPositive() {
        var tree = JiggleTestTree.Chain(2, float3.zero, new float3(1f, 0f, 0f), 1f,
            JiggleTestFactory.Params(collisionRadius: 0.1f));
        harness = new JiggleSimHarness(tree);
        harness.inputPoses[2] = new JiggleTransform {
            isVirtual = false, position = new float3(1f, 0f, 0f),
            rotation = quaternion.identity, scale = new float3(-3f, -3f, -3f),
        };

        harness.Step();

        Assert.AreEqual(0.3f, harness.Point(2).worldRadius, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void VirtualTip_ReflectsParentThroughItsGrandparent() {
        var tree = JiggleTestTree.Chain(2, float3.zero, new float3(1f, 0f, 0f), 1f);
        harness = new JiggleSimHarness(tree);

        harness.Step();

        var parent = harness.Point(2);
        var tip = harness.Point(tree.TipIndex);
        JiggleAssert.AreEqual(parent.pose * 2f - parent.parentPose, tip.pose, JiggleTestFactory.Tolerance);
        JiggleAssert.AreEqual(parent.pose, tip.parentPose, JiggleTestFactory.Tolerance);
        Assert.AreEqual(0f, tip.worldRadius, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void Extents_CoverRealPointsInflatedByWorldRadius() {
        var tree = JiggleTestTree.Chain(2, new float3(0f, 0f, 0f), new float3(1f, 0f, 0f), 4f,
            JiggleTestFactory.Params(collisionRadius: 0.5f));
        harness = new JiggleSimHarness(tree);

        harness.Step();

        var expectedMin = JiggleTestFactory.Key(new float3(-0.5f, 0f, -0.5f));
        var expectedMax = JiggleTestFactory.Key(new float3(4.5f, 0f, 0.5f));
        Assert.AreEqual(expectedMin.x, harness.Tree.minExtentPosition.x);
        Assert.AreEqual(expectedMin.y, harness.Tree.minExtentPosition.y);
        Assert.AreEqual(expectedMax.x, harness.Tree.maxExtentPosition.x);
        Assert.AreEqual(expectedMax.y, harness.Tree.maxExtentPosition.y);
    }

    [Test]
    public void RootWorkingPosition_IsSeededToItsPose() {
        var tree = JiggleTestTree.Chain(2, new float3(5f, 5f, 5f), new float3(1f, 0f, 0f), 1f);
        harness = new JiggleSimHarness(tree);
        var corrupted = harness.Point(0);
        corrupted.workingPosition = new float3(999f);
        harness.SetPoint(0, corrupted);

        harness.Step();

        JiggleAssert.AreEqual(new float3(4f, 5f, 5f), harness.Point(0).pose, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void GetKeyForPosition_RoundsToTheNearestCellOnXZ() {
        const float inverseCellSize = 2f;
        Assert.AreEqual(new int2(0, 0), JiggleGridCell.GetKeyForPosition(new float3(0.1f, 100f, 0.1f), inverseCellSize));
        Assert.AreEqual(new int2(1, 0), JiggleGridCell.GetKeyForPosition(new float3(0.4f, 0f, 0.2f), inverseCellSize));
        Assert.AreEqual(new int2(2, 4), JiggleGridCell.GetKeyForPosition(new float3(1f, 0f, 2f), inverseCellSize));
        Assert.AreEqual(new int2(-2, -4), JiggleGridCell.GetKeyForPosition(new float3(-1f, 0f, -2f), inverseCellSize));
    }

    [Test]
    public void GetKeyForPosition_ScalesWithTheCellSize() {
        var position = new float3(4f, 0f, 8f);
        Assert.AreEqual(new int2(8, 16), JiggleGridCell.GetKeyForPosition(position, 2f));
        Assert.AreEqual(new int2(4, 8), JiggleGridCell.GetKeyForPosition(position, 1f));
        Assert.AreEqual(new int2(1, 2), JiggleGridCell.GetKeyForPosition(position, 0.25f));
    }

    [Test]
    public void GetKeyForPosition_IgnoresHeight() {
        var low = JiggleGridCell.GetKeyForPosition(new float3(3f, -500f, 3f), 2f);
        var high = JiggleGridCell.GetKeyForPosition(new float3(3f, 500f, 3f), 2f);
        Assert.AreEqual(low, high);
    }
}

}
