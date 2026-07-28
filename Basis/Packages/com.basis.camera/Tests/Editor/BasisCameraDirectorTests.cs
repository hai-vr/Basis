using System.Collections.Generic;
using Basis.Cinematics;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// Shot selection, blending and list ordering. The director is a plain class holding plain
    /// data, so the whole state machine is exercised here without a camera or a scene.
    /// </summary>
    public class BasisCameraDirectorTests
    {
        private static BasisCameraSolveContext Context(float deltaTime = 1f / 60f)
            => new BasisCameraSolveContext
            {
                Subject = new BasisCameraSubject
                {
                    Valid = true,
                    AnchorPos = Vector3.zero,
                    LookPoint = Vector3.up * 1.6f,
                    GroundPos = Vector3.zero,
                    Yaw = Quaternion.identity,
                    Scale = 1f,
                    Radius = 0.45f,
                },
                Fov = 40f,
                Aspect = 16f / 9f,
                DeltaTime = deltaTime,
                Time = 0f,
                ManualRotation = Quaternion.identity,
            };

        [Test]
        public void TheHighestPriorityEnabledShotGoesLive()
        {
            var director = new BasisCameraDirector();
            director.AddShot().priority = 1;
            BasisCameraShot winner = director.AddShot();
            winner.priority = 50;
            director.AddShot().priority = 10;

            Assert.That(director.ResolveLiveShot(), Is.SameAs(winner));
        }

        [Test]
        public void ADisabledShotIsSkippedHoweverHighItsPriority()
        {
            var director = new BasisCameraDirector();
            BasisCameraShot disabled = director.AddShot();
            disabled.priority = 100;
            disabled.enabled = false;
            BasisCameraShot live = director.AddShot();
            live.priority = 1;

            Assert.That(director.ResolveLiveShot(), Is.SameAs(live));
        }

        [Test]
        public void AnExplicitSelectionBeatsPriority()
        {
            var director = new BasisCameraDirector();
            BasisCameraShot low = director.AddShot();
            low.priority = 0;
            director.AddShot().priority = 99;

            director.SelectedShotId = low.id;

            Assert.That(director.ResolveLiveShot(), Is.SameAs(low));
        }

        [Test]
        public void SelectingADisabledShotFallsBackToPriority()
        {
            var director = new BasisCameraDirector();
            BasisCameraShot disabled = director.AddShot();
            disabled.enabled = false;
            BasisCameraShot other = director.AddShot();
            other.priority = 5;

            director.SelectedShotId = disabled.id;

            Assert.That(director.ResolveLiveShot(), Is.SameAs(other),
                "A shot switched off from the panel must not strand the camera with nothing driving it.");
        }

        [Test]
        public void AnEmptyRigResolvesToNothingRatherThanThrowing()
        {
            var director = new BasisCameraDirector();
            Assert.That(director.ResolveLiveShot(), Is.Null);
            Assert.DoesNotThrow(() => director.Solve(Context()));
        }

        [Test]
        public void ShotIdsSurviveReordering()
        {
            var director = new BasisCameraDirector();
            BasisCameraShot first = director.AddShot();
            BasisCameraShot second = director.AddShot();
            BasisCameraShot third = director.AddShot();

            director.MoveShot(third.id, 0);

            Assert.That(director.IndexOf(third.id), Is.EqualTo(0));
            Assert.That(director.IndexOf(first.id), Is.EqualTo(1));
            Assert.That(director.IndexOf(second.id), Is.EqualTo(2));
            Assert.That(director.GetShot(third.id), Is.SameAs(third),
                "Solver state is keyed by id, so a shuffle must not renumber shots.");
        }

        [Test]
        public void MovingBeyondTheEndClampsInsteadOfFailing()
        {
            var director = new BasisCameraDirector();
            BasisCameraShot first = director.AddShot();
            director.AddShot();

            Assert.That(director.MoveShot(first.id, 99), Is.True);
            Assert.That(director.IndexOf(first.id), Is.EqualTo(1));
        }

        [Test]
        public void RemovingTheSelectedShotHandsControlBackToPriority()
        {
            var director = new BasisCameraDirector();
            BasisCameraShot selected = director.AddShot();
            BasisCameraShot survivor = director.AddShot();

            director.SelectedShotId = selected.id;
            director.RemoveShot(selected.id);

            Assert.That(director.SelectedShotId, Is.EqualTo(-1));
            Assert.That(director.ResolveLiveShot(), Is.SameAs(survivor));
        }

        [Test]
        public void RemovingAnUnknownShotIsANoOp()
        {
            var director = new BasisCameraDirector();
            director.AddShot();

            Assert.That(director.RemoveShot(12345), Is.False);
            Assert.That(director.Count, Is.EqualTo(1));
        }

        [Test]
        public void TheFirstShotCutsInRatherThanEasingFromNowhere()
        {
            var director = new BasisCameraDirector();
            BasisCameraShot shot = director.AddShot();
            shot.blendTime = 5f;

            director.Solve(Context());

            Assert.That(director.IsBlending, Is.False,
                "With no outgoing shot there is nothing to blend from, so easing would sweep in from the origin.");
        }

        [Test]
        public void SwitchingShotsStartsABlendThatCompletesInItsOwnTime()
        {
            var director = new BasisCameraDirector();
            BasisCameraShot first = director.AddShot();
            first.priority = 10;
            BasisCameraShot second = director.AddShot();
            second.priority = 1;
            second.blendTime = 1f;
            second.blendStyle = BasisCameraBlendStyle.Linear;

            director.Solve(Context());
            director.SelectedShotId = second.id;
            director.Solve(Context());

            Assert.That(director.IsBlending, Is.True);

            for (int Frame = 0; Frame < 70; Frame++)
            {
                director.Solve(Context());
            }

            Assert.That(director.IsBlending, Is.False);
            Assert.That(director.LiveShotId, Is.EqualTo(second.id));
        }

        [Test]
        public void ACutStyleShotSwitchesWithNoBlendAtAll()
        {
            var director = new BasisCameraDirector();
            BasisCameraShot first = director.AddShot();
            BasisCameraShot second = director.AddShot();
            second.blendTime = 10f;
            second.blendStyle = BasisCameraBlendStyle.Cut;

            director.SelectedShotId = first.id;
            director.Solve(Context());
            director.SelectedShotId = second.id;
            director.Solve(Context());

            Assert.That(director.IsBlending, Is.False, "Cut must ignore the blend time entirely.");
        }

        [Test]
        public void BlendCurvesAllStartAtZeroAndEndAtOne()
        {
            foreach (BasisCameraBlendStyle style in System.Enum.GetValues(typeof(BasisCameraBlendStyle)))
            {
                Assert.That(BasisCameraBlend.Evaluate(style, 0f), Is.EqualTo(0f).Within(1e-4f), style.ToString());
                Assert.That(BasisCameraBlend.Evaluate(style, 1f), Is.EqualTo(1f).Within(1e-4f), style.ToString());
            }
        }

        [Test]
        public void ACutHoldsTheOutgoingShotUntilTheVeryEnd()
        {
            Assert.That(BasisCameraBlend.Evaluate(BasisCameraBlendStyle.Cut, 0.99f), Is.EqualTo(0f));
        }

        [Test]
        public void EaseInOutIsHalfwayAtTheMidpoint()
        {
            Assert.That(BasisCameraBlend.Evaluate(BasisCameraBlendStyle.EaseInOut, 0.5f), Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void ATransposerShotSettlesOnItsAuthoredOffset()
        {
            var director = new BasisCameraDirector();
            BasisCameraShot shot = director.AddShot();
            shot.bodyMode = BasisCameraBodyMode.Transposer;
            shot.aimMode = BasisCameraAimMode.HardLookAt;
            shot.positionOffset = new Vector3(0f, 0f, 2f);
            shot.positionDamping = Vector3.zero;
            shot.rotationDamping = Vector3.zero;
            shot.noise = BasisCameraNoiseSettings.ForProfile(BasisCameraNoiseProfile.Off);

            BasisCameraPose pose = default;
            for (int Frame = 0; Frame < 5; Frame++)
            {
                pose = director.Solve(Context());
            }

            Assert.That(pose.Position.z, Is.EqualTo(2f).Within(1e-3f));
        }

        [Test]
        public void AHardLookAtShotEndsUpPointingAtTheSubject()
        {
            var director = new BasisCameraDirector();
            BasisCameraShot shot = director.AddShot();
            shot.bodyMode = BasisCameraBodyMode.Transposer;
            shot.aimMode = BasisCameraAimMode.HardLookAt;
            shot.positionOffset = new Vector3(0f, 0f, 3f);
            shot.positionDamping = Vector3.zero;
            shot.rotationDamping = Vector3.zero;
            shot.noise = BasisCameraNoiseSettings.ForProfile(BasisCameraNoiseProfile.Off);

            BasisCameraPose pose = default;
            for (int Frame = 0; Frame < 10; Frame++)
            {
                pose = director.Solve(Context());
            }

            Vector3 toSubject = (Context().Subject.LookPoint - pose.Position).normalized;
            Assert.That(Vector3.Dot(pose.Rotation * Vector3.forward, toSubject), Is.EqualTo(1f).Within(1e-2f));
        }

        [Test]
        public void ALockedOffShotDoesNotDrift()
        {
            var director = new BasisCameraDirector();
            BasisCameraShot shot = director.AddShot();
            shot.bodyMode = BasisCameraBodyMode.HardLock;
            shot.aimMode = BasisCameraAimMode.HardLookAt;
            shot.noise = BasisCameraNoiseSettings.ForProfile(BasisCameraNoiseProfile.Off);

            BasisCameraPose first = director.Solve(Context());
            for (int Frame = 0; Frame < 30; Frame++)
            {
                director.Solve(Context());
            }
            BasisCameraPose last = director.Solve(Context());

            Assert.That(Vector3.Distance(first.Position, last.Position), Is.LessThan(1e-4f));
        }

        [Test]
        public void AnInvalidSubjectHoldsTheLastPoseInsteadOfCollapsingToTheOrigin()
        {
            var director = new BasisCameraDirector();
            BasisCameraShot shot = director.AddShot();
            shot.positionOffset = new Vector3(0f, 0f, 2f);
            shot.positionDamping = Vector3.zero;
            shot.noise = BasisCameraNoiseSettings.ForProfile(BasisCameraNoiseProfile.Off);

            for (int Frame = 0; Frame < 5; Frame++)
            {
                director.Solve(Context());
            }

            BasisCameraSolveContext lost = Context();
            lost.Subject = default;
            BasisCameraPose pose = director.Solve(lost);

            Assert.That(pose.Position.z, Is.EqualTo(2f).Within(1e-3f),
                "An avatar swap or a joining player must not drop the camera to world zero for a frame.");
        }

        [Test]
        public void SnapToReseedsEveryShotSoATeleportDoesNotSweep()
        {
            var director = new BasisCameraDirector();
            BasisCameraShot shot = director.AddShot();
            shot.positionDamping = new Vector3(2f, 2f, 2f);
            shot.noise = BasisCameraNoiseSettings.ForProfile(BasisCameraNoiseProfile.Off);

            director.Solve(Context());
            director.SnapTo(new Vector3(100f, 0f, 100f), Quaternion.identity, 40f);
            BasisCameraPose pose = director.Solve(Context());

            Assert.That(Vector3.Distance(pose.Position, new Vector3(100f, 0f, 100f)), Is.LessThan(1f),
                "After a snap the shot reseeds at the new place rather than flying across the map.");
        }

        [Test]
        public void DollyPositionDampingTakesTheShortWayAroundALoop()
        {
            float result = BasisCameraDirector.DampDollyPosition(0.1f, 3.9f, 4, true, 0f, 1f / 60f);

            Assert.That(result, Is.EqualTo(3.9f).Within(1e-3f));

            float stepped = BasisCameraDirector.DampDollyPosition(0.1f, 3.9f, 4, true, 0.2f, 1f / 60f);
            Assert.That(stepped, Is.GreaterThan(3.5f).Or.LessThan(0.1f),
                "Wrapping backwards past zero is one step; sweeping forward through the whole loop is not.");
        }

        [Test]
        public void ClearEmptiesTheRigCompletely()
        {
            var director = new BasisCameraDirector();
            director.AddShot();
            director.AddShot();
            director.SelectedShotId = 1;

            director.Clear();

            Assert.That(director.Count, Is.EqualTo(0));
            Assert.That(director.SelectedShotId, Is.EqualTo(-1));
            Assert.That(director.ResolveLiveShot(), Is.Null);
        }
    }

    /// <summary>
    /// Queue ordering for the placed dolly points. The reorder is the operation behind the panel's
    /// position field, and it is pure list work so it is asserted directly rather than through
    /// spawned markers.
    /// </summary>
    public class BasisCameraDollyQueueTests
    {
        [Test]
        public void MovingAPointForwardShiftsTheRestBack()
        {
            var list = new List<string> { "a", "b", "c", "d" };

            Assert.That(BasisCameraDollyTrack.MoveInList(list, 0, 2), Is.True);
            Assert.That(list, Is.EqualTo(new List<string> { "b", "c", "a", "d" }));
        }

        [Test]
        public void MovingAPointBackwardShiftsTheRestForward()
        {
            var list = new List<string> { "a", "b", "c", "d" };

            Assert.That(BasisCameraDollyTrack.MoveInList(list, 3, 1), Is.True);
            Assert.That(list, Is.EqualTo(new List<string> { "a", "d", "b", "c" }));
        }

        [Test]
        public void MovingPastTheEndLandsAtTheEnd()
        {
            var list = new List<string> { "a", "b", "c" };

            Assert.That(BasisCameraDollyTrack.MoveInList(list, 0, 99), Is.True);
            Assert.That(list, Is.EqualTo(new List<string> { "b", "c", "a" }),
                "A position typed past the end should park the point last, not silently do nothing.");
        }

        [Test]
        public void MovingBelowZeroLandsFirst()
        {
            var list = new List<string> { "a", "b", "c" };

            Assert.That(BasisCameraDollyTrack.MoveInList(list, 2, -5), Is.True);
            Assert.That(list, Is.EqualTo(new List<string> { "c", "a", "b" }));
        }

        [Test]
        public void MovingAPointToWhereItAlreadyIsChangesNothing()
        {
            var list = new List<string> { "a", "b", "c" };

            Assert.That(BasisCameraDollyTrack.MoveInList(list, 1, 1), Is.False);
            Assert.That(list, Is.EqualTo(new List<string> { "a", "b", "c" }));
        }

        [Test]
        public void AnOutOfRangeSourceIsRejected()
        {
            var list = new List<string> { "a", "b" };

            Assert.That(BasisCameraDollyTrack.MoveInList(list, 5, 0), Is.False);
            Assert.That(BasisCameraDollyTrack.MoveInList(list, -1, 0), Is.False);
            Assert.That(list, Is.EqualTo(new List<string> { "a", "b" }));
        }

        [Test]
        public void AnEmptyOrMissingQueueIsRejected()
        {
            Assert.That(BasisCameraDollyTrack.MoveInList(new List<string>(), 0, 0), Is.False);
            Assert.That(BasisCameraDollyTrack.MoveInList<string>(null, 0, 0), Is.False);
        }

        [Test]
        public void PointColoursRunFromTheHeadOfTheQueueToTheTail()
        {
            Color first = BasisCameraDollyTrack.ColorForIndex(0, 4);
            Color last = BasisCameraDollyTrack.ColorForIndex(3, 4);

            Assert.That(first.g, Is.GreaterThan(first.r), "The start of the track reads green.");
            Assert.That(last.r, Is.GreaterThan(last.g), "The end reads red, so direction of travel is obvious.");
        }

        [Test]
        public void ASinglePointStillGetsAColour()
        {
            Color only = BasisCameraDollyTrack.ColorForIndex(0, 1);
            Assert.That(only.g, Is.GreaterThan(0f));
        }
    }
}
