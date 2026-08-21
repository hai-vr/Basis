using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    public class BasisCameraSubjectPickerTests
    {
        private static Bounds Box(Vector3 centre, Vector3 size) => new Bounds(centre, size);

        [Test]
        public void IntersectRayBounds_ReportsEntryAndExitAlongTheRay()
        {
            Ray ray = new Ray(Vector3.zero, Vector3.forward);

            Assert.That(BasisCameraSubjectPicker.IntersectRayBounds(ray, Box(new Vector3(0f, 0f, 5f), Vector3.one), out float entry, out float exit), Is.True);
            Assert.That(entry, Is.EqualTo(4.5f).Within(1e-4f));
            Assert.That(exit, Is.EqualTo(5.5f).Within(1e-4f));
        }

        [Test]
        public void IntersectRayBounds_MissesABoxBesideTheRay()
        {
            Ray ray = new Ray(Vector3.zero, Vector3.forward);

            Assert.That(BasisCameraSubjectPicker.IntersectRayBounds(ray, Box(new Vector3(4f, 0f, 5f), Vector3.one), out _, out _), Is.False);
        }

        [Test]
        public void IntersectRayBounds_RejectsABoxEntirelyBehindTheRay()
        {
            Ray ray = new Ray(Vector3.zero, Vector3.forward);

            Assert.That(BasisCameraSubjectPicker.IntersectRayBounds(ray, Box(new Vector3(0f, 0f, -5f), Vector3.one), out _, out _), Is.False,
                "Clicking forwards must never focus on somebody stood behind the lens.");
        }

        [Test]
        public void IntersectRayBounds_ReportsANegativeEntryWhenTheLensIsInsideTheBox()
        {
            // A hand-held camera sits well inside its own operator's T-pose bounds, so the pick has
            // to be able to tell "inside" from "in front" — inside is not a click target.
            Ray ray = new Ray(Vector3.zero, Vector3.forward);

            Assert.That(BasisCameraSubjectPicker.IntersectRayBounds(ray, Box(Vector3.zero, new Vector3(2f, 2f, 2f)), out float entry, out float exit), Is.True);
            Assert.That(entry, Is.LessThan(0f));
            Assert.That(exit, Is.GreaterThan(0f));
            Assert.That(entry, Is.LessThan(BasisCameraSubjectPicker.MinimumEntryDistance));
        }

        [Test]
        public void IntersectRayBounds_HandlesARayParallelToASlab()
        {
            Ray ray = new Ray(new Vector3(0f, 10f, 0f), Vector3.forward);

            Assert.That(BasisCameraSubjectPicker.IntersectRayBounds(ray, Box(new Vector3(0f, 0f, 5f), Vector3.one), out _, out _), Is.False);
            Assert.That(BasisCameraSubjectPicker.IntersectRayBounds(new Ray(Vector3.zero, Vector3.forward), Box(new Vector3(0f, 0f, 5f), new Vector3(1f, 1f, 1f)), out _, out _), Is.True);
        }

        [Test]
        public void IntersectRayBounds_IsIndependentOfDirectionMagnitude()
        {
            Bounds bounds = Box(new Vector3(0f, 0f, 5f), Vector3.one);

            BasisCameraSubjectPicker.IntersectRayBounds(new Ray(Vector3.zero, Vector3.forward), bounds, out float unit, out _);
            BasisCameraSubjectPicker.IntersectRayBounds(new Ray(Vector3.zero, Vector3.forward * 7f), bounds, out float scaled, out _);

            Assert.That(scaled, Is.EqualTo(unit).Within(1e-4f),
                "Entry is a distance in metres; a longer direction vector must not shrink it.");
        }

        [Test]
        public void ResolveFocusDepth_LandsOnTheBodyRatherThanItsFrontFace()
        {
            // The front face of an avatar's bounds can be an outstretched hand. Focusing there
            // leaves the face the shot is about outside the sharp band.
            Ray ray = new Ray(Vector3.zero, Vector3.forward);
            Bounds bounds = Box(new Vector3(0f, 0f, 15f), new Vector3(2f, 2f, 10f));

            BasisCameraSubjectPicker.IntersectRayBounds(ray, bounds, out float entry, out float exit);

            Assert.That(entry, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(BasisCameraSubjectPicker.ResolveFocusDepth(ray, bounds, entry, exit), Is.EqualTo(15f).Within(1e-4f));
        }

        [Test]
        public void ResolveFocusDepth_StaysInsideTheSpanTheRayActuallyCrosses()
        {
            Ray ray = new Ray(Vector3.zero, Vector3.forward);
            Bounds bounds = Box(new Vector3(0f, 0f, 15f), new Vector3(2f, 2f, 10f));

            Assert.That(BasisCameraSubjectPicker.ResolveFocusDepth(ray, bounds, 16f, 18f), Is.EqualTo(16f).Within(1e-4f));
            Assert.That(BasisCameraSubjectPicker.ResolveFocusDepth(ray, bounds, 11f, 13f), Is.EqualTo(13f).Within(1e-4f));
        }

        [Test]
        public void ResolveFocusDepth_NeverGoesBehindTheLens()
        {
            Ray ray = new Ray(Vector3.zero, Vector3.forward);
            Bounds bounds = Box(new Vector3(0f, 0f, 0f), new Vector3(2f, 2f, 2f));

            Assert.That(BasisCameraSubjectPicker.ResolveFocusDepth(ray, bounds, -1f, 1f), Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void TryGetPlayerBounds_RejectsAPlayerWithNoAvatar()
        {
            Assert.That(BasisCameraSubjectPicker.TryGetPlayerBounds(null, out _), Is.False);
        }
    }
}
