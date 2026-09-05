using Basis.Scripts.Device_Management.Devices.OpenVR;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.OpenVR
{
    public sealed class BasisOpenVRWristLatchTests
    {
        static readonly Vector3 NeutralPosition = new Vector3(-0.0427f, 0.0374f, -0.1630f);
        static readonly Quaternion NeutralRotation = new Quaternion(0.3819f, 0.0754f, -0.0841f, 0.9173f).normalized;
        static readonly Vector3 TouchedPosition = new Vector3(-0.0405f, 0.0399f, -0.1549f);
        static readonly Quaternion TouchedRotation = new Quaternion(0.4414f, 0.0583f, -0.0889f, 0.8910f).normalized;

        [Test]
        public void FirstSample_SeedsEvenWhileHeld()
        {
            BasisOpenVRWristLatch latch = default;

            latch.Update(true, TouchedPosition, TouchedRotation);

            Assert.IsTrue(latch.Latched);
            Assert.AreEqual(TouchedPosition, latch.Position);
            Assert.AreEqual(TouchedRotation, latch.Rotation);
        }

        [Test]
        public void ThumbTouch_HoldsNeutralWrist()
        {
            BasisOpenVRWristLatch latch = default;
            latch.Update(false, NeutralPosition, NeutralRotation);

            latch.Update(true, TouchedPosition, TouchedRotation);

            Assert.AreEqual(NeutralPosition, latch.Position);
            Assert.AreEqual(NeutralRotation, latch.Rotation);
        }

        [Test]
        public void Release_RefreshesOnceNotHeld()
        {
            BasisOpenVRWristLatch latch = default;
            latch.Update(false, NeutralPosition, NeutralRotation);
            latch.Update(true, TouchedPosition, TouchedRotation);

            latch.Update(false, TouchedPosition, TouchedRotation);

            Assert.AreEqual(TouchedPosition, latch.Position);
            Assert.AreEqual(TouchedRotation, latch.Rotation);
        }

        [Test]
        public void Reset_ReseedsFromNextSample()
        {
            BasisOpenVRWristLatch latch = default;
            latch.Update(false, NeutralPosition, NeutralRotation);

            latch.Reset();
            Assert.IsFalse(latch.Latched);
            latch.Update(true, TouchedPosition, TouchedRotation);

            Assert.AreEqual(TouchedPosition, latch.Position);
        }

        [Test]
        public void Settled_TrueForIdenticalFrames()
        {
            Assert.IsTrue(BasisOpenVRWristLatch.Settled(NeutralPosition, NeutralRotation, NeutralPosition, NeutralRotation));
        }

        [Test]
        public void Settled_FalseWhileBlendMoves()
        {
            Vector3 step = Vector3.Lerp(NeutralPosition, TouchedPosition, 0.1f);
            Quaternion stepRotation = Quaternion.Slerp(NeutralRotation, TouchedRotation, 0.1f);

            Assert.IsFalse(BasisOpenVRWristLatch.Settled(step, NeutralRotation, NeutralPosition, NeutralRotation));
            Assert.IsFalse(BasisOpenVRWristLatch.Settled(NeutralPosition, stepRotation, NeutralPosition, NeutralRotation));
        }

        [Test]
        public void Settled_FalseAgainstUnwrittenPreviousFrame()
        {
            Assert.IsFalse(BasisOpenVRWristLatch.Settled(NeutralPosition, NeutralRotation, Vector3.zero, default));
        }
    }
}
