using Basis.Cinematics;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    public class BasisCameraSubjectAimTests
    {
        private static readonly Vector3 Normal = new Vector3(1f, 1.6f, 2f);
        private static readonly Vector3 Head = new Vector3(1.1f, 1.55f, 2.2f);
        private static readonly Vector3 Ground = new Vector3(1f, 0f, 2f);
        private const float Top = 1.8f;

        [Test]
        public void NormalAimsAtTheHeadHeightPointFollowAlwaysUsed()
        {
            Assert.That(BasisCameraSubjectAim.LookPoint(BasisCameraAimPoint.Normal, Normal, Head, Ground, Top, 0f), Is.EqualTo(Normal));
        }

        [Test]
        public void HeadAimsAtTheHeadItself()
        {
            Assert.That(BasisCameraSubjectAim.LookPoint(BasisCameraAimPoint.Head, Normal, Head, Ground, Top, 0f), Is.EqualTo(Head));
        }

        [Test]
        public void FullBodyAimsAtTheMiddleOfTheirHeight()
        {
            Vector3 point = BasisCameraSubjectAim.LookPoint(BasisCameraAimPoint.FullBody, Normal, Head, Ground, Top, 0f);

            Assert.That(point.y, Is.EqualTo(0.9f).Within(1e-4f));
            Assert.That(point.x, Is.EqualTo(Normal.x).Within(1e-4f));
            Assert.That(point.z, Is.EqualTo(Normal.z).Within(1e-4f),
                "The full-body aim stays on the body's centre line rather than following the head.");
        }

        [Test]
        public void TheHeightOffsetMovesEveryAimPoint()
        {
            foreach (BasisCameraAimPoint aim in BasisCameraModifiers.AimPoints)
            {
                Vector3 plain = BasisCameraSubjectAim.LookPoint(aim, Normal, Head, Ground, Top, 0f);
                Vector3 raised = BasisCameraSubjectAim.LookPoint(aim, Normal, Head, Ground, Top, 0.3f);

                Assert.That((raised - plain - Vector3.up * 0.3f).magnitude, Is.LessThan(1e-5f), aim.ToString());
            }
        }

        [Test]
        public void FullBodyWithNoMeasuredHeightFallsBackToNormal()
        {
            Assert.That(BasisCameraSubjectAim.LookPoint(BasisCameraAimPoint.FullBody, Normal, Head, Ground, Ground.y, 0f), Is.EqualTo(Normal));
        }

        [Test]
        public void FullBodySizesTheFramingToHalfTheirHeight()
        {
            Assert.That(BasisCameraSubjectAim.FramingRadius(BasisCameraAimPoint.FullBody, 0.45f, 1.8f, 1f), Is.EqualTo(0.9f).Within(1e-4f));
        }

        [Test]
        public void TheMeasuredRadiusIsHandedOverAtDefaultScale()
        {
            Assert.That(BasisCameraSubjectAim.FramingRadius(BasisCameraAimPoint.FullBody, 0.45f, 3.6f, 2f), Is.EqualTo(0.9f).Within(1e-4f),
                "The solver multiplies the radius by the subject's scale, so a measured world height must be divided back out.");
        }

        [Test]
        public void TheOtherAimPointsKeepTheAuthoredSubjectSize()
        {
            Assert.That(BasisCameraSubjectAim.FramingRadius(BasisCameraAimPoint.Normal, 0.45f, 1.8f, 1f), Is.EqualTo(0.45f).Within(1e-4f));
            Assert.That(BasisCameraSubjectAim.FramingRadius(BasisCameraAimPoint.Head, 0.45f, 1.8f, 1f), Is.EqualTo(0.45f).Within(1e-4f));
        }

        [Test]
        public void AnUnmeasuredAvatarKeepsTheAuthoredSubjectSize()
        {
            Assert.That(BasisCameraSubjectAim.FramingRadius(BasisCameraAimPoint.FullBody, 0.45f, 0f, 1f), Is.EqualTo(0.45f).Within(1e-4f));
        }

        [Test]
        public void TheFallbackTopSitsALittleAboveTheHead()
        {
            float top = BasisCameraSubjectAim.FallbackTop(0f, 1.5f);

            Assert.That(top, Is.GreaterThan(1.5f));
            Assert.That(top, Is.LessThan(1.8f));
        }
    }
}
