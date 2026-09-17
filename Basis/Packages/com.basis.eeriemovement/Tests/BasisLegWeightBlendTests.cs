using System.Text;
using Basis.IK;
using NUnit.Framework;
using UnityEngine;
namespace Basis.Tests.IK
{
    public class BasisLegWeightBlendTests
    {
        const float toleranceDeg = 0.5f;
        static readonly float[] weights = { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f };
        GameObject root;
        BasisPoseSkeleton skeleton;
        Transform hips, leftUpperLeg, leftLowerLeg, leftFoot, rightUpperLeg, rightLowerLeg, rightFoot;
        [TearDown]
        public void TearDown()
        {
            skeleton?.Dispose();
            skeleton = null;
            if (root != null) Object.DestroyImmediate(root);
            root = null;
        }
        static Transform Bone(string name, Transform parent, Vector3 worldPosition)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);
            go.transform.SetParent(parent, true);
            return go.transform;
        }
        void BuildRig()
        {
            root = new GameObject("LegWeightBlendRig");
            hips = Bone("Hips", root.transform, new Vector3(0f, 0.95f, 0f));
            leftUpperLeg = Bone("LeftUpperLeg", hips, new Vector3(-0.09f, 0.90f, 0f));
            leftLowerLeg = Bone("LeftLowerLeg", leftUpperLeg, new Vector3(-0.09f, 0.48f, 0f));
            leftFoot = Bone("LeftFoot", leftLowerLeg, new Vector3(-0.09f, 0.08f, 0f));
            rightUpperLeg = Bone("RightUpperLeg", hips, new Vector3(0.09f, 0.90f, 0f));
            rightLowerLeg = Bone("RightLowerLeg", rightUpperLeg, new Vector3(0.09f, 0.48f, 0f));
            rightFoot = Bone("RightFoot", rightLowerLeg, new Vector3(0.09f, 0.08f, 0f));
            Transform[] bones = { hips, leftUpperLeg, leftLowerLeg, leftFoot, rightUpperLeg, rightLowerLeg, rightFoot };
            skeleton = new BasisPoseSkeleton();
            skeleton.Build(hips, bones);
            skeleton.GatherNow();
        }
        void PoseAnimatedLeg(float kneeOutDeg, float flexDeg, float bendDeg, float footYawDeg)
        {
            leftUpperLeg.rotation = Quaternion.AngleAxis(kneeOutDeg, Vector3.up) * Quaternion.AngleAxis(-flexDeg, Vector3.right);
            leftLowerLeg.rotation = leftUpperLeg.rotation * Quaternion.AngleAxis(bendDeg, Vector3.right);
            leftFoot.rotation = Quaternion.AngleAxis(footYawDeg, Vector3.up);
            skeleton.GatherNow();
        }
        BasisEerieMovement Job(float weight, Vector3 targetPosition, Quaternion targetRotation)
        {
            var job = new BasisEerieMovement
            {
                offsetRotationLeftFoot = Quaternion.identity,
                offsetRotationRightFoot = Quaternion.identity,
                targetPositionLeftLowerLeg = targetPosition,
                targetRotationLeftLowerLeg = targetRotation,
                hintPositionLeftLowerLeg = new Vector3(-0.09f, 0.45f, 0.5f),
                kneeBendPrefLeft = Vector3.forward,
                kneeBendPrefRight = Vector3.forward,
                kneeAnteriorRef = Vector3.forward,
                playerUp = Vector3.up,
            };
            job.handleHips = skeleton.Bind(hips);
            job.handleLeftUpperLeg = skeleton.Bind(leftUpperLeg);
            job.handleLeftLowerLeg = skeleton.Bind(leftLowerLeg);
            job.handleLeftFoot = skeleton.Bind(leftFoot);
            job.handleRightUpperLeg = skeleton.Bind(rightUpperLeg);
            job.handleRightLowerLeg = skeleton.Bind(rightLowerLeg);
            job.handleRightFoot = skeleton.Bind(rightFoot);
            BasisEeriePlanner.Bind(ref job);
            BasisEeriePlanner.Frame(ref job, new BasisEerieFrameFacts { hipsTracked = true, upright = true, footSimReady = true, leftFootSim = weight, leftSimFootRotation = true });
            job.poseStream = skeleton.Stream;
            return job;
        }
        static float TwistDeg(Quaternion q, Vector3 axis)
        {
            float s = q.x * axis.x + q.y * axis.y + q.z * axis.z, c = q.w;
            if (c < 0f) { s = -s; c = -c; }
            return 2f * Mathf.Atan2(s, c) * Mathf.Rad2Deg;
        }
        float Sweep(float kneeOutDeg, float flexDeg, float bendDeg, float footYawDeg, Quaternion targetRotation, StringBuilder report)
        {
            BuildRig();
            PoseAnimatedLeg(kneeOutDeg, flexDeg, bendDeg, footYawDeg);
            Quaternion animatedFoot = leftFoot.rotation;
            Vector3 animatedFootPosition = leftFoot.position;
            float worst = 0f;
            foreach (float w in weights)
            {
                PoseAnimatedLeg(kneeOutDeg, flexDeg, bendDeg, footYawDeg);
                var job = Job(w, animatedFootPosition, targetRotation);
                Assert.IsTrue(job.plan.leftLeg.solve && !job.plan.leftLeg.preserveTip, "the plan must solve the left leg from the sim with the sim foot rotation");
                Assert.AreEqual(w, job.plan.leftLeg.weight, 1e-6f, "the plan weight must be the sim blend");
                job.SolveLeg(0);
                Quaternion expected = Quaternion.Slerp(animatedFoot, targetRotation, w), foot = skeleton.Stream.GetRotation(job.handleLeftFoot);
                Vector3 shinAxis = (skeleton.Stream.GetPosition(job.handleLeftFoot) - skeleton.Stream.GetPosition(job.handleLeftLowerLeg)).normalized;
                float err = Quaternion.Angle(expected, foot), twist = TwistDeg(Quaternion.Inverse(expected) * foot, shinAxis);
                report.AppendLine($"  w {w:F1}: foot {err,6:F2} deg off the animated->target slerp, twist about the shin {twist,6:F2} deg");
                worst = Mathf.Max(worst, err);
            }
            return worst;
        }
        [TestCase(20f, 8f, 16f, -20f)]
        [TestCase(-15f, 25f, 40f, 0f)]
        [TestCase(30f, 50f, 90f, -10f)]
        public void Foot_HoldsTheAnimatedRotation_WhenTheTargetAlreadyMatchesIt(float kneeOutDeg, float flexDeg, float bendDeg, float footYawDeg)
        {
            var report = new StringBuilder();
            float worst = Sweep(kneeOutDeg, flexDeg, bendDeg, footYawDeg, Quaternion.AngleAxis(footYawDeg, Vector3.up), report);
            Debug.Log(report.ToString());
            Assert.LessOrEqual(worst, toleranceDeg, "a foot whose target equals its animated rotation must not move at any blend weight:\n" + report);
        }
        [TestCase(20f, 8f, 16f, -20f)]
        [TestCase(-15f, 25f, 40f, 0f)]
        public void Foot_BlendsStraightFromAnimatedToTarget_AtEveryWeight(float kneeOutDeg, float flexDeg, float bendDeg, float footYawDeg)
        {
            var report = new StringBuilder();
            float worst = Sweep(kneeOutDeg, flexDeg, bendDeg, footYawDeg, Quaternion.AngleAxis(footYawDeg + 25f, Vector3.up) * Quaternion.AngleAxis(10f, Vector3.right), report);
            Debug.Log(report.ToString());
            Assert.LessOrEqual(worst, toleranceDeg, "the foot must blend along the slerp between its animated and target rotations:\n" + report);
        }
        [Test]
        public void Foot_ReachesTheTarget_AtFullWeight()
        {
            BuildRig();
            PoseAnimatedLeg(20f, 8f, 16f, -20f);
            Vector3 target = new Vector3(-0.12f, 0.10f, 0.15f);
            Quaternion targetRotation = Quaternion.AngleAxis(5f, Vector3.up);
            var job = Job(1f, target, targetRotation);
            job.SolveLeg(0);
            Assert.LessOrEqual(Vector3.Distance(skeleton.Stream.GetPosition(job.handleLeftFoot), target), 1e-3f, "reachable target must be reached");
            Assert.LessOrEqual(Quaternion.Angle(skeleton.Stream.GetRotation(job.handleLeftFoot), targetRotation), 0.01f, "the foot takes the target rotation at full weight");
        }
    }
}
