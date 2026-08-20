using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using CameraSettings = BasisHandHeldCameraUI.CameraSettings;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// Modes people save themselves: the store they live in, the comparison that decides whether
    /// one is still in control, and what putting one on does to the camera.
    ///
    /// <para>The load-bearing test here is <see cref="EveryStoredSettingIsComparedByMatches"/>. The
    /// comparison is written out by hand — it runs while the panel is open and a reflective walk
    /// would box every float on every check — so nothing about the code itself stops a field added
    /// to <c>CameraSettings</c> next month from being invisible to it. That test moves every field
    /// in turn and demands the comparison notice, which turns the omission into a red test on the
    /// day the field is added rather than a mode that silently claims a shot it no longer
    /// describes.</para>
    ///
    /// <para>Every test here points the store at a temporary directory. The real file is the
    /// player's own list of saved modes, and these tests save, overwrite and delete.</para>
    /// </summary>
    public class BasisCameraUserModeTests
    {
        private string _storeDirectory;

        [SetUp]
        public void SetUp()
        {
            _storeDirectory = Path.Combine(Path.GetTempPath(), "BasisCameraUserModeTests-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_storeDirectory);
            BasisCameraUserModes.DirectoryOverrideForTest = _storeDirectory;
            BasisCameraUserModes.ResetCacheForTest();
        }

        [TearDown]
        public void TearDown()
        {
            BasisCameraUserModes.DirectoryOverrideForTest = null;

            // Before the directory goes, or the next test's first read would come off a cache
            // holding modes whose file no longer exists.
            BasisCameraUserModes.ResetCacheForTest();

            if (Directory.Exists(_storeDirectory)) Directory.Delete(_storeDirectory, true);
        }

        private static BasisCameraUserMode NewMode(string name, CameraSettings settings = null)
        {
            return new BasisCameraUserMode
            {
                name = name,
                tint = new Color(0.2f, 0.4f, 0.6f, 1f),
                pinSpace = (int)BasisHandHeldCameraInteractable.CameraPinSpace.WorldSpace,
                settings = settings ?? BasisCameraSettingsRig.DistinctiveSettings(),
            };
        }

        // ---------- The comparison ----------

        /// <summary>
        /// Every field of a settings file has to be one the comparison looks at, or a mode goes on
        /// claiming to be in control of a value that has since been changed underneath it.
        ///
        /// <para>Walks <c>CameraSettings</c> by reflection, moves each field somewhere it was not
        /// already, and asserts the comparison notices. The four skipped are skipped for reasons
        /// that are part of the design, and each one is named with its reason.</para>
        /// </summary>
        [Test]
        public void EveryStoredSettingIsComparedByMatches()
        {
            StringBuilder unseen = new StringBuilder();

            foreach (FieldInfo field in SettingsFields())
            {
                if (ComparisonExclusions.ContainsKey(field.Name)) continue;

                CameraSettings left = BasisCameraSettingsRig.DistinctiveSettings();
                CameraSettings right = BasisCameraSettingsRig.DistinctiveSettings();

                Assert.That(BasisCameraUserMode.SettingsMatch(left, right), Is.True,
                    $"two identical settings files disagreed before {field.Name} was even touched");

                object moved = Perturb(field.GetValue(right), field.FieldType);
                field.SetValue(right, moved);

                if (BasisCameraUserMode.SettingsMatch(left, right))
                {
                    unseen.AppendLine($"  {field.Name} ({field.FieldType.Name}) changed and the comparison did not notice");
                }
            }

            Assert.That(unseen.Length, Is.Zero,
                "BasisCameraUserMode.SettingsMatch is blind to settings that a saved mode stores. " +
                "A mode will go on claiming these values after they have been changed:\n" + unseen);
        }

        /// <summary>
        /// The four fields the comparison deliberately ignores. Kept as a test so removing a reason
        /// from the design means removing it from here too.
        /// </summary>
        private static readonly Dictionary<string, string> ComparisonExclusions = new Dictionary<string, string>
        {
            { "settingsVersion", "describes the file format, not the camera" },
            { "cameraMode", "a label derived from the values around it" },
            { "userMode", "the answer this comparison is being asked for" },
            { "exposuresRemaining", "spent by taking a photograph, which is not a change of settings" },
        };

        [Test]
        public void TheFieldsMatchesIgnoresAreTheOnesListedHere()
        {
            foreach (KeyValuePair<string, string> excluded in ComparisonExclusions)
            {
                CameraSettings left = BasisCameraSettingsRig.DistinctiveSettings();
                CameraSettings right = BasisCameraSettingsRig.DistinctiveSettings();

                FieldInfo field = typeof(CameraSettings).GetField(excluded.Key);
                Assert.That(field, Is.Not.Null, $"{excluded.Key} is no longer a field of CameraSettings");

                field.SetValue(right, Perturb(field.GetValue(right), field.FieldType));

                Assert.That(BasisCameraUserMode.SettingsMatch(left, right), Is.True,
                    $"{excluded.Key} is now compared, but it is excluded because it {excluded.Value}");
            }
        }

        [Test]
        public void MatchesToleratesASliderRoundingButNotARealEdit()
        {
            CameraSettings stored = BasisCameraSettingsRig.DistinctiveSettings();
            BasisCameraUserMode mode = NewMode("Rounding", stored);

            CameraSettings rounded = BasisCameraSettingsRig.DistinctiveSettings();
            rounded.fov = stored.fov + 0.2f;
            Assert.That(mode.Matches(rounded), Is.True, "a rounded field of view left the mode");

            CameraSettings edited = BasisCameraSettingsRig.DistinctiveSettings();
            edited.fov = stored.fov + 5f;
            Assert.That(mode.Matches(edited), Is.False, "a five-degree change did not leave the mode");
        }

        // ---------- Names ----------

        [Test]
        public void ANameOfNothingButSpaceIsNotAName()
        {
            Assert.That(BasisCameraUserMode.SanitizeName(null), Is.Null);
            Assert.That(BasisCameraUserMode.SanitizeName(string.Empty), Is.Null);
            Assert.That(BasisCameraUserMode.SanitizeName("   \t  "), Is.Null);
        }

        [Test]
        public void ANameIsTrimmedCollapsedAndCapped()
        {
            Assert.That(BasisCameraUserMode.SanitizeName("  Night   Shot  "), Is.EqualTo("Night Shot"));
            Assert.That(BasisCameraUserMode.SanitizeName("Two\nLines"), Is.EqualTo("Two Lines"));

            string tooLong = new string('a', BasisCameraUserMode.MaxNameLength + 20);
            Assert.That(BasisCameraUserMode.SanitizeName(tooLong).Length,
                Is.EqualTo(BasisCameraUserMode.MaxNameLength));
        }

        /// <summary>
        /// A cap that landed mid-word used to leave the trailing space behind, so "aaa… bbb"
        /// truncated to a name ending in a space that no round trip could reproduce.
        /// </summary>
        [Test]
        public void ANameCappedOnASpaceDoesNotKeepIt()
        {
            string name = new string('a', BasisCameraUserMode.MaxNameLength - 1) + " bbb";
            Assert.That(BasisCameraUserMode.SanitizeName(name), Does.Not.EndWith(" "));
        }

        // ---------- The store ----------

        [Test]
        public void ASavedModeComesBackOffDisk()
        {
            BasisCameraUserMode saved = NewMode("Night");
            Assert.That(BasisCameraUserModes.Store(saved, out string error), Is.True, error);

            BasisCameraUserModes.ResetCacheForTest();

            BasisCameraUserMode loaded = BasisCameraUserModes.Find("Night");
            Assert.That(loaded, Is.Not.Null, "the mode did not reach the file");
            Assert.That(loaded.tint, Is.EqualTo(saved.tint));
            Assert.That(loaded.pinSpace, Is.EqualTo(saved.pinSpace));
            Assert.That(BasisCameraUserMode.SettingsMatch(saved.settings, loaded.settings), Is.True,
                "the settings did not survive the file");
        }

        [Test]
        public void SavingOverANameReplacesThatModeWhereItSits()
        {
            BasisCameraUserModes.Store(NewMode("First"), out _);
            BasisCameraUserModes.Store(NewMode("Second"), out _);
            BasisCameraUserModes.Store(NewMode("Third"), out _);

            BasisCameraUserMode replacement = NewMode("Second");
            replacement.tint = Color.red;
            Assert.That(BasisCameraUserModes.Store(replacement, out string error), Is.True, error);

            Assert.That(BasisCameraUserModes.Count, Is.EqualTo(3), "the overwrite added a mode");
            Assert.That(BasisCameraUserModes.Modes[1].name, Is.EqualTo("Second"),
                "updating a mode moved it to the end of the list it was picked from");
            Assert.That(BasisCameraUserModes.Modes[1].tint, Is.EqualTo(Color.red));
        }

        [Test]
        public void TwoNamesThatDifferOnlyInCaseAreOneMode()
        {
            BasisCameraUserModes.Store(NewMode("Night"), out _);
            BasisCameraUserModes.Store(NewMode("NIGHT"), out _);

            Assert.That(BasisCameraUserModes.Count, Is.EqualTo(1));
            Assert.That(BasisCameraUserModes.Find("night"), Is.Not.Null);
        }

        [Test]
        public void AModeWithNoNameIsRefusedRatherThanStored()
        {
            Assert.That(BasisCameraUserModes.Store(NewMode("   "), out string error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            Assert.That(BasisCameraUserModes.Count, Is.Zero);
        }

        [Test]
        public void RemovingTakesTheModeOffDiskToo()
        {
            BasisCameraUserModes.Store(NewMode("Night"), out _);
            Assert.That(BasisCameraUserModes.Remove("night"), Is.True, "remove did not match on case");

            BasisCameraUserModes.ResetCacheForTest();
            Assert.That(BasisCameraUserModes.Find("Night"), Is.Null);
            Assert.That(BasisCameraUserModes.Remove("Night"), Is.False, "removing nothing reported success");
        }

        [Test]
        public void TheStoreRefusesToGrowWithoutBound()
        {
            for (int Index = 0; Index < BasisCameraUserModes.MaxModes; Index++)
            {
                Assert.That(BasisCameraUserModes.Store(NewMode("Mode" + Index), out string error), Is.True, error);
            }

            Assert.That(BasisCameraUserModes.Store(NewMode("OneTooMany"), out string full), Is.False);
            Assert.That(full, Is.Not.Null.And.Not.Empty, "the refusal did not say why");
            Assert.That(BasisCameraUserModes.Count, Is.EqualTo(BasisCameraUserModes.MaxModes));
        }

        /// <summary>
        /// The panel rebuilds its dropdown off the revision, so a change that leaves the count
        /// still — a recolour, or saving over a name — has to move it or the page would go on
        /// showing the old colour.
        /// </summary>
        [Test]
        public void TheRevisionMovesForAChangeThatLeavesTheCountStill()
        {
            BasisCameraUserModes.Store(NewMode("Night"), out _);
            int before = BasisCameraUserModes.Revision;

            BasisCameraUserMode recoloured = NewMode("Night");
            recoloured.tint = Color.green;
            BasisCameraUserModes.Store(recoloured, out _);

            Assert.That(BasisCameraUserModes.Count, Is.EqualTo(1), "the count moved, so it proves nothing");
            Assert.That(BasisCameraUserModes.Revision, Is.GreaterThan(before));
        }

        [Test]
        public void ARecordWithNoNameIsDroppedButItsNeighbourIsNot()
        {
            // Written straight to the file: the store refuses to save a nameless mode, so one can
            // only arrive by a hand edit or a truncated write.
            WriteModesFile("{\"version\":1,\"modes\":[" +
                           "{\"name\":\"   \",\"settings\":{}}," +
                           "{\"name\":\"Fine\",\"settings\":{}}]}");

            Assert.That(BasisCameraUserModes.Count, Is.EqualTo(1),
                "a nameless record can be neither selected nor deleted, so it must not be listed");
            Assert.That(BasisCameraUserModes.Find("Fine"), Is.Not.Null,
                "a good record next to a broken one was thrown out with it");
        }

        /// <summary>
        /// A record missing its settings block is usable, not broken: <c>CameraSettings</c> has a
        /// constructor, and JsonUtility leaves a field the JSON does not carry holding whatever
        /// that constructor gave it. So the mode loads as the shipped defaults rather than as
        /// nothing — which is why the loader's null-settings guard is a guard rather than a path
        /// a hand-edited file can take.
        /// </summary>
        [Test]
        public void ARecordWithNoSettingsBlockLoadsTheDefaults()
        {
            WriteModesFile("{\"version\":1,\"modes\":[{\"name\":\"Sparse\"}]}");

            BasisCameraUserMode loaded = BasisCameraUserModes.Find("Sparse");
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.settings, Is.Not.Null);
            Assert.That(loaded.settings.fov, Is.EqualTo(new CameraSettings().fov).Within(0.001f));
        }

        [Test]
        public void TwoRecordsSharingANameCollapseToTheFirst()
        {
            WriteModesFile("{\"version\":1,\"modes\":[" +
                           "{\"name\":\"Night\",\"settings\":{\"fov\":33}}," +
                           "{\"name\":\"NIGHT\",\"settings\":{\"fov\":88}}]}");

            Assert.That(BasisCameraUserModes.Count, Is.EqualTo(1),
                "two rows with the same name would both resolve to the first one picked");
            Assert.That(BasisCameraUserModes.Find("Night").settings.fov, Is.EqualTo(33f).Within(0.001f));
        }

        /// <summary>Writes the file behind the store's back and makes the next read come off it.</summary>
        private void WriteModesFile(string json)
        {
            File.WriteAllText(Path.Combine(_storeDirectory, BasisCameraUserModes.CameraModesJson), json);
            BasisCameraUserModes.ResetCacheForTest();
        }

        // ---------- Putting one on ----------

        [Test]
        public void PuttingOnASavedModeAppliesItsSettingsAndItsPlacement()
        {
            using (BasisCameraSettingsRig rig = new BasisCameraSettingsRig())
            {
                CameraSettings stored = BasisCameraSettingsRig.DistinctiveSettings();
                BasisCameraUserMode mode = NewMode("Night", stored);
                BasisCameraUserModes.Store(mode, out _);

                rig.Camera.ApplyUserMode(mode);

                Assert.That(rig.Camera.UserModeName, Is.EqualTo("Night"));
                Assert.That(rig.Camera.PinSpace,
                    Is.EqualTo(BasisHandHeldCameraInteractable.CameraPinSpace.WorldSpace));
                Assert.That(rig.Camera.Modifiers.DrivesPosition, Is.True, "the mode's position modifier was not fitted");
                
                Assert.That(rig.CaptureCamera.fieldOfView, Is.EqualTo(stored.fov).Within(0.5f));
                Assert.That(rig.Vignette.intensity.value, Is.EqualTo(stored.vignette).Within(0.001f),
                    "a mode carries the whole settings file, not just the shot-defining half");
            }
        }

        /// <summary>
        /// The camera is handed a copy, never the stored object. It keeps the last file it applied
        /// as the baseline for the next save and hands the shot list straight to the rig, so
        /// sharing would have ordinary use edit the saved mode underneath its owner.
        /// </summary>
        [Test]
        public void PuttingOnAModeDoesNotHandTheCameraTheStoredObject()
        {
            using (BasisCameraSettingsRig rig = new BasisCameraSettingsRig())
            {
                BasisCameraUserMode mode = NewMode("Night");
                BasisCameraUserModes.Store(mode, out _);

                rig.Camera.ApplyUserMode(mode);

                Assert.That(rig.Camera.Modifiers, Is.Not.Null);
                Assert.That(rig.Camera.Modifiers, Is.Not.SameAs(mode.settings.modifiers),
                    "the camera is editing straight into the saved mode's own stack");

                // And the mode still gives back what it stored, after the camera has been used.
                float storedVignette = mode.settings.vignette;
                rig.Vignette.intensity.value = storedVignette + 0.4f;
                rig.UI.CreateCurrentCameraSettingsForTest();

                Assert.That(mode.settings.vignette, Is.EqualTo(storedVignette),
                    "using the camera rewrote the mode it was wearing");
            }
        }

        [Test]
        public void ChangingASettingLeavesTheSavedMode()
        {
            using (BasisCameraSettingsRig rig = new BasisCameraSettingsRig())
            {
                BasisCameraUserMode mode = NewMode("Night");
                BasisCameraUserModes.Store(mode, out _);
                rig.Camera.ApplyUserMode(mode);
                Assert.That(rig.Camera.UserModeName, Is.EqualTo("Night"));

                // Straight at the live effect, which is where the harvest reads it from — the same
                // path a change made from the prop's own HUD takes.
                rig.Vignette.intensity.value = mode.settings.vignette + 0.4f;
                rig.Camera.RefreshUserMode(rig.UI.CreateCurrentCameraSettingsForTest());

                Assert.That(rig.Camera.UserModeName, Is.Null,
                    "the camera went on claiming a mode whose values it no longer holds");
            }
        }

        [Test]
        public void PickingABuiltInModeDropsTheSavedOne()
        {
            using (BasisCameraSettingsRig rig = new BasisCameraSettingsRig())
            {
                BasisCameraUserMode mode = NewMode("Night");
                BasisCameraUserModes.Store(mode, out _);
                rig.Camera.ApplyUserMode(mode);

                rig.Camera.ApplyCameraMode(BasisCameraMode.Photo);

                Assert.That(rig.Camera.UserModeName, Is.Null);
                Assert.That(rig.Camera.CameraMode, Is.EqualTo(BasisCameraMode.Photo));
            }
        }

        [Test]
        public void SavingAModeAdoptsItWithoutReapplyingAnything()
        {
            using (BasisCameraSettingsRig rig = new BasisCameraSettingsRig())
            {
                // The slider, not the camera: saving harvests the field of view from the prop's
                // own control, so that is where a change has to be made for the harvest to see it.
                rig.FovSlider.value = 63f;
                float fieldOfViewBefore = rig.CaptureCamera.fieldOfView;

                BasisCameraUserMode captured = rig.Camera.CaptureUserMode("Mine", Color.cyan);
                Assert.That(captured.settings.fov, Is.EqualTo(63f).Within(0.01f));
                Assert.That(BasisCameraUserModes.Store(captured, out string error), Is.True, error);

                rig.Camera.AdoptUserMode(captured);

                Assert.That(rig.Camera.UserModeName, Is.EqualTo("Mine"));
                Assert.That(rig.CaptureCamera.fieldOfView, Is.EqualTo(fieldOfViewBefore).Within(0.001f),
                    "adopting a mode re-applied it and moved the camera");
                Assert.That(rig.Camera.RefreshUserMode(rig.UI.CreateCurrentCameraSettingsForTest()), Is.False,
                    "a mode taken straight off the camera did not match the camera it came from");
            }
        }

        /// <summary>
        /// A saved mode records the camera, not the label the camera happened to be wearing —
        /// otherwise picking it would announce the mode it was saved from.
        /// </summary>
        [Test]
        public void ACapturedModeCarriesNoModeOfItsOwn()
        {
            using (BasisCameraSettingsRig rig = new BasisCameraSettingsRig())
            {
                rig.Camera.ApplyCameraMode(BasisCameraMode.Cinematic);

                BasisCameraUserMode captured = rig.Camera.CaptureUserMode("Mine", Color.cyan);

                Assert.That(captured.settings.cameraMode, Is.EqualTo((int)BasisCameraMode.Custom));
                Assert.That(captured.settings.userMode, Is.Null.Or.Empty);
            }
        }

        // ---------- Coming back after a restart ----------

        [Test]
        public void ALoadedFileRestoresTheSavedModeItNames()
        {
            using (BasisCameraSettingsRig rig = new BasisCameraSettingsRig())
            {
                BasisCameraUserMode mode = NewMode("Night");
                BasisCameraUserModes.Store(mode, out _);

                // The file a camera wearing this mode would have written: the same values, plus
                // the mode's name. NewMode stores DistinctiveSettings, so these agree.
                CameraSettings file = BasisCameraSettingsRig.DistinctiveSettings();
                file.userMode = "Night";

                rig.UI.ApplySettingsForTest(file);

                Assert.That(rig.Camera.UserModeName, Is.EqualTo("Night"));
                Assert.That(rig.Camera.Modifiers.DrivesPosition, Is.True,
                    "placement was not re-armed, so a saved flying camera came back inert");
            }
        }

        [Test]
        public void AFileNamingADeletedModeComesBackWithoutOne()
        {
            using (BasisCameraSettingsRig rig = new BasisCameraSettingsRig())
            {
                CameraSettings file = BasisCameraSettingsRig.DistinctiveSettings();
                file.userMode = "ModeThatWasDeleted";

                rig.UI.ApplySettingsForTest(file);

                Assert.That(rig.Camera.UserModeName, Is.Null);
            }
        }

        // ---------- Presentation ----------

        [Test]
        public void ASavedModeIsPresentedInItsOwnColourAndName()
        {
            BasisCameraUserMode mode = NewMode("Night");
            mode.tint = new Color(0.9f, 0.1f, 0.1f, 1f);

            BasisCameraModeDescriptor descriptor = BasisCameraModes.DescribeUserMode(mode);

            Assert.That(descriptor.IsUserMode, Is.True);
            Assert.That(descriptor.LiteralTitle, Is.EqualTo("Night"),
                "a saved mode's name must be shown as typed, never looked up as a key");
            Assert.That(descriptor.Tint, Is.EqualTo(mode.tint));
        }

        [Test]
        public void ASavedModeGreysOutOnlyWhatItDoesNotRun()
        {
            BasisCameraUserMode following = NewMode("Following");
            following.settings.modifiers.positionModifier = Basis.Cinematics.BasisCameraPositionModifier.FollowSubject;
            BasisCameraModeDescriptor followDescriptor = BasisCameraModes.DescribeUserMode(following);

            Assert.That(followDescriptor.RoleOf(BasisCameraPanelSection.Subject),
                Is.EqualTo(BasisCameraSectionRole.Driven));
            Assert.That(followDescriptor.RoleOf(BasisCameraPanelSection.Dolly),
                Is.EqualTo(BasisCameraSectionRole.Inactive));

            BasisCameraUserMode filming = NewMode("Filming");
            filming.settings.modifiers.positionModifier = Basis.Cinematics.BasisCameraPositionModifier.FreeFly;
            filming.settings.modifiers.rotationModifier = Basis.Cinematics.BasisCameraRotationModifier.FreeLook;
            BasisCameraModeDescriptor filmDescriptor = BasisCameraModes.DescribeUserMode(filming);

            Assert.That(filmDescriptor.RoleOf(BasisCameraPanelSection.Subject),
                Is.EqualTo(BasisCameraSectionRole.Inactive));
            Assert.That(filmDescriptor.RoleOf(BasisCameraPanelSection.PositionModifier),
                Is.EqualTo(BasisCameraSectionRole.Inactive),
                "Free Fly hands the position channel back, so the section is not driven.");

            // A saved mode is a whole settings file, so anything with somewhere to be saved is
            // driven — but the three that have nowhere must not claim to be.
            Assert.That(filmDescriptor.RoleOf(BasisCameraPanelSection.Lens),
                Is.EqualTo(BasisCameraSectionRole.Driven));
            Assert.That(filmDescriptor.RoleOf(BasisCameraPanelSection.Layers),
                Is.EqualTo(BasisCameraSectionRole.Available));
            Assert.That(filmDescriptor.RoleOf(BasisCameraPanelSection.Performance),
                Is.EqualTo(BasisCameraSectionRole.Available));
            Assert.That(filmDescriptor.RoleOf(BasisCameraPanelSection.Gizmos),
                Is.EqualTo(BasisCameraSectionRole.Available));
        }

        /// <summary>
        /// A record naming a section in both the driven list and the inactive one used to take
        /// whichever the builder happened to write second.
        /// </summary>
        [Test]
        public void AModeThatArmsBothDoesNotGiveASectionTwoRoles()
        {
            BasisCameraUserMode both = NewMode("Both");
            both.settings.modifiers.positionModifier = Basis.Cinematics.BasisCameraPositionModifier.FollowSubject;
            both.settings.modifiers.rotationModifier = Basis.Cinematics.BasisCameraRotationModifier.Compose;

            BasisCameraModeDescriptor descriptor = BasisCameraModes.DescribeUserMode(both);

            Assert.That(descriptor.RoleOf(BasisCameraPanelSection.Subject),
                Is.EqualTo(BasisCameraSectionRole.Driven),
                "follow is armed, so the section that configures it cannot read as doing nothing");
        }

        [Test]
        public void ATransparentColourIsReplacedRatherThanDarkeningThePage()
        {
            WriteModesFile("{\"version\":1,\"modes\":[{\"name\":\"Hand Edited\"," +
                           "\"tint\":{\"r\":0,\"g\":0,\"b\":0,\"a\":0},\"settings\":{}}]}");

            BasisCameraUserMode loaded = BasisCameraUserModes.Find("Hand Edited");
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.tint.a, Is.GreaterThan(0f),
                "a transparent tint would blend every section toward nothing");
        }

        // ---------- The readout ----------

        [Test]
        public void TheReadoutShowsEverySectionAndResolvesItsIndexes()
        {
            CameraSettings settings = BasisCameraSettingsRig.DistinctiveSettings();
            BasisHandHeldCameraMetaData metaData = new BasisHandHeldCameraMetaData();

            string text = BasisCameraSettingsReadout.Build(
                settings,
                (int)BasisHandHeldCameraInteractable.CameraPinSpace.WorldSpace,
                metaData);

            Assert.That(text, Is.Not.Null.And.Not.Empty);

            // The values that reach the camera as an index into a preset table are the ones a raw
            // readout would render meaningless.
            Assert.That(text, Does.Contain(metaData.apertures[settings.apertureIndex]));
            Assert.That(text, Does.Contain(metaData.shutterSpeeds[settings.shutterSpeedIndex]));
            Assert.That(text, Does.Contain(metaData.isoValues[settings.isoIndex]));
            Assert.That(text, Does.Contain(
                $"{metaData.resolutions[settings.resolutionIndex].width} x {metaData.resolutions[settings.resolutionIndex].height}"));

            // A value from each of the settings groups, so a group dropped from the readout fails
            // here rather than going unnoticed until someone reads the page.
            Assert.That(text, Does.Contain("77"), "the field of view is missing");
            Assert.That(text, Does.Contain("0.35"), "the vignette is missing");
            Assert.That(text, Does.Contain("0.8"), "the framing radius is missing");
            Assert.That(text.Split('\n').Length, Is.GreaterThan(40),
                "the readout is meant to be every setting, not a summary");
        }

        [Test]
        public void TheReadoutSurvivesACameraWithNoPresetTables()
        {
            Assert.That(
                BasisCameraSettingsReadout.Build(BasisCameraSettingsRig.DistinctiveSettings(), 0, null),
                Is.Not.Null.And.Not.Empty);

            Assert.That(
                BasisCameraSettingsReadout.Build(null, 0, null),
                Is.Empty);
        }

        // ---------- Helpers ----------

        private static IEnumerable<FieldInfo> SettingsFields() =>
            typeof(CameraSettings).GetFields(BindingFlags.Public | BindingFlags.Instance);

        /// <summary>
        /// Moves a value somewhere it was not, for every type <c>CameraSettings</c> uses. Adding a
        /// field of a type this does not know throws rather than silently passing the test that
        /// depends on it.
        /// </summary>
        private static object Perturb(object value, System.Type type)
        {
            if (type == typeof(int)) return (int)value + 3;
            if (type == typeof(float)) return (float)value + 7.5f;
            if (type == typeof(bool)) return !(bool)value;
            if (type == typeof(string)) return (string)value == "moved" ? "moved twice" : "moved";
            if (type == typeof(Vector3)) return (Vector3)value + new Vector3(1.5f, 2.5f, 3.5f);
            if (type == typeof(Color)) return new Color(0.77f, 0.11f, 0.33f, 1f);
            if (type == typeof(Basis.Cinematics.BasisCameraModifierStack))
            {
                Basis.Cinematics.BasisCameraModifierStack moved =
                    ((Basis.Cinematics.BasisCameraModifierStack)value).Clone();
                moved.follow.positionOffset += new Vector3(1.5f, 2.5f, 3.5f);
                moved.subject.aimHeightOffset += 1.25f;
                return moved;
            }
            if (type == typeof(Basis.Cinematics.BasisCameraSubjectSettings))
            {
                Basis.Cinematics.BasisCameraSubjectSettings moved =
                    (Basis.Cinematics.BasisCameraSubjectSettings)value;
                moved.aimHeightOffset += 1.25f;
                moved.framingRadius += 0.3f;
                moved.anchorToBody = !moved.anchorToBody;
                moved.groupIncludesLocal = !moved.groupIncludesLocal;
                return moved;
            }

            throw new AssertionException(
                $"BasisCameraUserModeTests cannot move a {type.Name}. A settings field of a new type " +
                "was added; teach Perturb about it or the comparison coverage test silently skips it.");
        }
    }
}
