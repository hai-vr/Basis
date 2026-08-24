using System.Collections.Generic;
using System.IO;
using Basis.Cinematics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// Saved dolly tracks: the frame they are stored in, the store they live in, and the folder
    /// they are traded through.
    ///
    /// <para>The frame is the part worth testing hardest. A track is stored against where its
    /// author stood rather than in world coordinates, which is what lets it be laid out again
    /// somewhere else — and it is also what makes a wrong sign or a missed inverse produce a track
    /// that is a plausible shape in the wrong place, rather than an obvious failure.</para>
    ///
    /// <para>Every test here points the store at a temporary directory. The real one is somebody's
    /// own saved tracks, and these save, overwrite, export and delete.</para>
    /// </summary>
    public class BasisCameraDollyPresetTests
    {
        private string _storeDirectory;

        [SetUp]
        public void SetUp()
        {
            _storeDirectory = Path.Combine(Path.GetTempPath(), "BasisCameraDollyPresetTests-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_storeDirectory);
            BasisCameraDollyPresets.DirectoryOverrideForTest = _storeDirectory;
            BasisCameraDollyPresets.ResetCacheForTest();
        }

        [TearDown]
        public void TearDown()
        {
            BasisCameraDollyPresets.DirectoryOverrideForTest = null;

            // Before the directory goes, or the next test's first read comes off a cache holding
            // presets whose file no longer exists.
            BasisCameraDollyPresets.ResetCacheForTest();

            try
            {
                if (Directory.Exists(_storeDirectory)) Directory.Delete(_storeDirectory, true);
            }
            catch (IOException)
            {
            }
        }

        private static readonly Vector3[] Track =
        {
            new Vector3(2f, 1f, 3f),
            new Vector3(-1f, 1.5f, 4f),
            new Vector3(0f, 2f, -6f),
        };

        private static BasisCameraDollyPreset Captured(string name, Vector3 anchor, float yaw, float scale)
        {
            var preset = new BasisCameraDollyPreset { name = name };
            var rotations = new List<Quaternion>();
            for (int Index = 0; Index < Track.Length; Index++)
            {
                rotations.Add(Quaternion.Euler(0f, Index * 30f, 0f));
            }

            preset.Capture(new List<Vector3>(Track), rotations, anchor, yaw, scale);
            return preset;
        }

        // ---- The frame --------------------------------------------------------------------

        [Test]
        public void ATrackPutBackAtItsOwnAnchorLandsExactlyWhereItWas()
        {
            BasisCameraDollyPreset preset = Captured("In Place", new Vector3(10f, 0f, -4f), 47f, 1.3f);

            for (int Index = 0; Index < Track.Length; Index++)
            {
                preset.Resolve(Index, preset.anchorPosition, preset.anchorYaw, preset.anchorScale,
                    out Vector3 position, out Quaternion rotation);

                Assert.That(Vector3.Distance(position, Track[Index]), Is.LessThan(0.001f), $"point {Index}");
                Assert.That(Quaternion.Angle(rotation, Quaternion.Euler(0f, Index * 30f, 0f)),
                    Is.LessThan(0.05f), $"point {Index} facing");
            }
        }

        [Test]
        public void LaidOutSomewhereElseTheShapeIsTheSameSizeAndTheSameShape()
        {
            BasisCameraDollyPreset preset = Captured("Reused", new Vector3(10f, 0f, -4f), 47f, 1f);
            Vector3 elsewhere = new Vector3(-30f, 5f, 90f);

            preset.Resolve(0, elsewhere, 200f, 1f, out Vector3 first, out _);
            preset.Resolve(1, elsewhere, 200f, 1f, out Vector3 second, out _);

            Assert.That(Vector3.Distance(first, second),
                Is.EqualTo(Vector3.Distance(Track[0], Track[1])).Within(0.001f),
                "Moving and turning a track must not stretch it.");
        }

        [Test]
        public void LaidOutOnABiggerAvatarTheTrackGrowsWithIt()
        {
            BasisCameraDollyPreset preset = Captured("Scaled", Vector3.zero, 0f, 1f);

            preset.Resolve(0, Vector3.zero, 0f, 2f, out Vector3 doubled, out _);

            Assert.That(Vector3.Distance(doubled, Track[0] * 2f), Is.LessThan(0.001f),
                "A track built around a body has to come back the size of the body wearing it.");
        }

        [Test]
        public void TurningTheAnchorTurnsTheWholeTrackAroundIt()
        {
            BasisCameraDollyPreset preset = Captured("Turned", Vector3.zero, 0f, 1f);

            preset.Resolve(0, Vector3.zero, 90f, 1f, out Vector3 turned, out _);

            Assert.That(Vector3.Distance(turned, Quaternion.Euler(0f, 90f, 0f) * Track[0]), Is.LessThan(0.001f));
        }

        [Test]
        public void TheAuthorsPitchDoesNotTipTheTrack()
        {
            // Capture takes a yaw, not a rotation, so a track saved while looking at the floor is
            // still a track rather than a ramp.
            BasisCameraDollyPreset preset = Captured("Level", Vector3.zero, 0f, 1f);
            preset.Resolve(0, Vector3.zero, 0f, 1f, out Vector3 position, out _);

            Assert.That(position.y, Is.EqualTo(Track[0].y).Within(0.001f));
        }

        [Test]
        public void ACaptureWithNoScaleDoesNotDivideTheShapeAway()
        {
            BasisCameraDollyPreset preset = Captured("Zero", Vector3.zero, 0f, 0f);

            Assert.That(preset.anchorScale, Is.EqualTo(1f));
            preset.Resolve(0, Vector3.zero, 0f, 1f, out Vector3 position, out _);
            Assert.That(Vector3.Distance(position, Track[0]), Is.LessThan(0.001f));
        }

        [Test]
        public void ResolvingASlotThatIsNotThereAnswersWithTheAnchor()
        {
            BasisCameraDollyPreset preset = Captured("Short", Vector3.one, 0f, 1f);

            preset.Resolve(99, Vector3.one, 0f, 1f, out Vector3 position, out Quaternion rotation);

            Assert.That(position, Is.EqualTo(Vector3.one));
            Assert.That(rotation, Is.EqualTo(Quaternion.identity));
        }

        [Test]
        public void ATrackLongerThanTheCapIsCutToIt()
        {
            var positions = new List<Vector3>();
            for (int Index = 0; Index < BasisCameraDollyPreset.MaxPoints + 20; Index++)
            {
                positions.Add(new Vector3(Index, 0f, 0f));
            }

            var preset = new BasisCameraDollyPreset { name = "Long" };
            preset.Capture(positions, null, Vector3.zero, 0f, 1f);

            Assert.That(preset.Count, Is.EqualTo(BasisCameraDollyPreset.MaxPoints));
        }

        // ---- Names ------------------------------------------------------------------------

        [Test]
        public void ANameCannotCarryAnythingThatWouldEscapeTheFolder()
        {
            string cleaned = BasisCameraDollyPreset.SanitizeName("../../etc/passwd");

            Assert.That(cleaned, Is.Not.Null);
            Assert.That(cleaned, Does.Not.Contain("/"));
            Assert.That(cleaned, Does.Not.Contain("\\"));
            Assert.That(cleaned, Does.Not.Contain(".."));
        }

        [Test]
        public void ANameOfNothingIsRefused()
        {
            Assert.That(BasisCameraDollyPreset.SanitizeName("   "), Is.Null);
            Assert.That(BasisCameraDollyPreset.SanitizeName("..."), Is.Null);
            Assert.That(BasisCameraDollyPreset.SanitizeName(null), Is.Null);
        }

        [Test]
        public void NamesAreComparedTheWayPeopleReadThem()
        {
            Assert.That(BasisCameraDollyPreset.NamesMatch("Crane Up", "crane up"), Is.True);
        }

        // ---- The store --------------------------------------------------------------------

        [Test]
        public void ASavedTrackComesBackOffDisk()
        {
            Assert.That(BasisCameraDollyPresets.Store(Captured("Push In", Vector3.zero, 0f, 1f), out string error),
                Is.True, error);

            BasisCameraDollyPresets.ResetCacheForTest();

            BasisCameraDollyPreset loaded = BasisCameraDollyPresets.Find("Push In");
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Count, Is.EqualTo(Track.Length));
        }

        [Test]
        public void SavingOntoANameYouAlreadyHaveReplacesItWhereItSits()
        {
            BasisCameraDollyPresets.Store(Captured("First", Vector3.zero, 0f, 1f), out _);
            BasisCameraDollyPresets.Store(Captured("Second", Vector3.zero, 0f, 1f), out _);

            var replacement = new BasisCameraDollyPreset { name = "First" };
            replacement.Capture(new List<Vector3> { Vector3.zero, Vector3.up }, null, Vector3.zero, 0f, 1f);
            Assert.That(BasisCameraDollyPresets.Store(replacement, out _), Is.True);

            Assert.That(BasisCameraDollyPresets.Count, Is.EqualTo(2));
            Assert.That(BasisCameraDollyPresets.Presets[0].name, Is.EqualTo("First"),
                "Updating the preset you just picked must not move it to the bottom of the list.");
            Assert.That(BasisCameraDollyPresets.Find("First").Count, Is.EqualTo(2));
        }

        [Test]
        public void ATrackWithNoPointsIsNotSomethingToSave()
        {
            var empty = new BasisCameraDollyPreset { name = "Nothing" };

            Assert.That(BasisCameraDollyPresets.Store(empty, out string error), Is.False);
            Assert.That(error, Is.EqualTo("camera.dollyPreset.error.noPoints"));
        }

        [Test]
        public void ANamelessPresetIsRefusedRatherThanStoredBlank()
        {
            BasisCameraDollyPreset preset = Captured("   ", Vector3.zero, 0f, 1f);

            Assert.That(BasisCameraDollyPresets.Store(preset, out string error), Is.False);
            Assert.That(error, Is.EqualTo("camera.dollyPreset.error.empty"));
        }

        [Test]
        public void TheStoreFillsUpRatherThanGrowingWithoutBound()
        {
            for (int Index = 0; Index < BasisCameraDollyPresets.MaxPresets; Index++)
            {
                Assert.That(BasisCameraDollyPresets.Store(
                    Captured("Take " + Index, Vector3.zero, 0f, 1f), out _), Is.True);
            }

            Assert.That(BasisCameraDollyPresets.Store(Captured("One Too Many", Vector3.zero, 0f, 1f),
                out string error), Is.False);
            Assert.That(error, Is.EqualTo("camera.dollyPreset.error.full"));
        }

        [Test]
        public void RemovingAPresetTakesItOffDiskToo()
        {
            BasisCameraDollyPresets.Store(Captured("Doomed", Vector3.zero, 0f, 1f), out _);

            Assert.That(BasisCameraDollyPresets.Remove("doomed"), Is.True, "Names match case-insensitively.");
            BasisCameraDollyPresets.ResetCacheForTest();
            Assert.That(BasisCameraDollyPresets.Exists("Doomed"), Is.False);
        }

        [Test]
        public void EverySaveMovesTheRevisionSoAnOpenPanelKnowsToRebuild()
        {
            int before = BasisCameraDollyPresets.Revision;
            BasisCameraDollyPresets.Store(Captured("Watched", Vector3.zero, 0f, 1f), out _);

            Assert.That(BasisCameraDollyPresets.Revision, Is.GreaterThan(before));
        }

        // ---- The traded folder ------------------------------------------------------------

        [Test]
        public void AnExportedPresetComesBackInThroughImport()
        {
            BasisCameraDollyPreset preset = Captured("Crane Up", new Vector3(4f, 0f, 2f), 33f, 1.1f);
            BasisCameraDollyPresets.Store(preset, out _);

            Assert.That(BasisCameraDollyPresets.Export(preset, out string path, out string error), Is.True, error);
            Assert.That(File.Exists(path), Is.True);

            BasisCameraDollyPresets.ClearForTest();
            Assert.That(BasisCameraDollyPresets.Count, Is.EqualTo(0));

            Assert.That(BasisCameraDollyPresets.Import(out int imported, out error), Is.True, error);
            Assert.That(imported, Is.EqualTo(1));

            BasisCameraDollyPreset back = BasisCameraDollyPresets.Find("Crane Up");
            Assert.That(back, Is.Not.Null);
            Assert.That(back.Count, Is.EqualTo(preset.Count));
            Assert.That(back.anchorScale, Is.EqualTo(preset.anchorScale).Within(0.0001f));

            for (int Index = 0; Index < preset.Count; Index++)
            {
                preset.Resolve(Index, Vector3.zero, 0f, 1f, out Vector3 expected, out _);
                back.Resolve(Index, Vector3.zero, 0f, 1f, out Vector3 actual, out _);
                Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.001f), $"point {Index}");
            }
        }

        [Test]
        public void ImportingFromAnEmptyFolderIsNotAFailure()
        {
            Assert.That(BasisCameraDollyPresets.Import(out int imported, out string error), Is.True);
            Assert.That(imported, Is.EqualTo(0));
            Assert.That(error, Is.Null);
        }

        [Test]
        public void AHandWrittenPresetFileIsTakenInAndMadeSafe()
        {
            // These files are meant to be passed around, so every one has been somewhere this code
            // has not: no rotation at all, a zero scale, and an ease that does not exist.
            Directory.CreateDirectory(BasisCameraDollyPresets.ExportFolder);
            File.WriteAllText(Path.Combine(BasisCameraDollyPresets.ExportFolder, "handmade.json"),
                "{\"name\":\"Handmade\",\"anchorScale\":0.0,\"gridSize\":0.0," +
                "\"motion\":{\"easeIn\":42,\"easeOut\":-9,\"easeInPortion\":8.0,\"speed\":2.0,\"playing\":true," +
                "\"syncMode\":1},\"points\":[{\"position\":{\"x\":0,\"y\":0,\"z\":0}}," +
                "{\"position\":{\"x\":1,\"y\":0,\"z\":0}}]}");

            Assert.That(BasisCameraDollyPresets.Import(out int imported, out string error), Is.True, error);
            Assert.That(imported, Is.EqualTo(1));

            BasisCameraDollyPreset preset = BasisCameraDollyPresets.Find("Handmade");
            Assert.That(preset, Is.Not.Null);
            Assert.That(preset.anchorScale, Is.EqualTo(1f), "A zero scale would divide the shape away.");
            Assert.That(preset.gridSize, Is.GreaterThan(0f));
            Assert.That(BasisCameraEasing.IsDefined(preset.motion.easeIn), Is.True);
            Assert.That(BasisCameraEasing.IsDefined(preset.motion.easeOut), Is.True);
            Assert.That(preset.motion.easeInPortion,
                Is.LessThanOrEqualTo(BasisCameraDollySpeed.MaximumEasePortion));
            Assert.That(preset.motion.playing, Is.False,
                "A preset that started running as it loaded would fly the camera off.");
            Assert.That(preset.motion.syncMode, Is.EqualTo(BasisCameraDollySync.LocalOnly),
                "Loading somebody's preset must not publish a track to the instance.");

            preset.Resolve(0, Vector3.zero, 0f, 1f, out _, out Quaternion rotation);
            Assert.That(rotation, Is.EqualTo(Quaternion.identity),
                "An absent rotation arrives as four zeroes, which is not a rotation at all.");
        }

        [Test]
        public void AFileThatIsNotAPresetIsSkippedRatherThanStoppingTheImport()
        {
            Directory.CreateDirectory(BasisCameraDollyPresets.ExportFolder);
            File.WriteAllText(Path.Combine(BasisCameraDollyPresets.ExportFolder, "rubbish.json"), "not json at all {");

            BasisCameraDollyPreset preset = Captured("Good One", Vector3.zero, 0f, 1f);
            BasisCameraDollyPresets.Export(preset, out _, out _);
            BasisCameraDollyPresets.ClearForTest();

            // Saying so in the log is the wanted behaviour, and the runner counts an unexpected
            // error as a failed test. The message is written through BasisDebug, which wraps it in
            // colour tags, so it is turned off rather than matched.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                Assert.That(BasisCameraDollyPresets.Import(out int imported, out _), Is.True);
                Assert.That(imported, Is.EqualTo(1), "One bad file must not cost the folder its good ones.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void ExportingSomethingWithNoTrackInItIsRefused()
        {
            var empty = new BasisCameraDollyPreset { name = "Nothing" };

            Assert.That(BasisCameraDollyPresets.Export(empty, out _, out string error), Is.False);
            Assert.That(error, Is.EqualTo("camera.dollyPreset.error.noPoints"));
        }

        [Test]
        public void ANameOfNothingButSpaceIsNotAName()
        {
            Assert.That(BasisCameraDollyPreset.SanitizeName(null), Is.Null);
            Assert.That(BasisCameraDollyPreset.SanitizeName(string.Empty), Is.Null);
            Assert.That(BasisCameraDollyPreset.SanitizeName("   \t  "), Is.Null);
        }

        [Test]
        public void ANameIsTrimmedCollapsedAndCapped()
        {
            Assert.That(BasisCameraDollyPreset.SanitizeName("  Night   Shot  "), Is.EqualTo("Night Shot"));
            Assert.That(BasisCameraDollyPreset.SanitizeName("Two\nLines"), Is.EqualTo("Two Lines"));

            string tooLong = new string('a', BasisCameraDollyPreset.MaxNameLength + 20);
            Assert.That(BasisCameraDollyPreset.SanitizeName(tooLong).Length,
                Is.EqualTo(BasisCameraDollyPreset.MaxNameLength));
        }

        /// <summary>
        /// A cap that landed mid-word used to leave the trailing space behind, so "aaa… bbb"
        /// truncated to a name ending in a space that no round trip could reproduce.
        /// </summary>
        [Test]
        public void ANameCappedOnASpaceDoesNotKeepIt()
        {
            string name = new string('a', BasisCameraDollyPreset.MaxNameLength - 1) + " bbb";
            Assert.That(BasisCameraDollyPreset.SanitizeName(name), Does.Not.EndWith(" "));
        }
    }
}
