using System;
using System.Collections.Generic;
using Basis.Scripts.Drivers;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.IK
{
    /// <summary>
    /// Regression tests for the device stream record/replay capability — the mechanism that turns a REAL
    /// headset session into permanent, replayable test data.
    ///
    /// WHAT THIS CAPABILITY IS FOR. The IK suite has ~810 tests, a 109-clip CMU mocap corpus and 64 editor
    /// sweeps, and one limitation none of them lift: nothing is verified in a headset. The corpus's own
    /// NOTICE.md states the reason — "A mocap hand is not a controller... A VR controller's rotation is a
    /// GRIP convention" — and that warning was quoted in the very report that then shipped 27
    /// hand-rotation features which were wrong. A recorded device stream carries the grip convention, the
    /// real tracker noise/stand-off/slip/dropout, and the user's real calibration residual, because it is
    /// a real session. These tests guard the property that makes it usable as a fixture: REPLAY IS
    /// DETERMINISTIC.
    ///
    /// WHAT THESE TESTS DO AND DO NOT PROVE. They prove the capture->file->replay path is a pure,
    /// bit-exact function: the codec loses nothing, and the poses handed to the injection seam are
    /// identical on every replay of a given stream. They do NOT prove the IK SOLVE is deterministic —
    /// that needs play mode and is stated as unverified. The line falls exactly at the simulated device's
    /// FollowMovement transform: everything up to and including the pose written there is pinned here;
    /// everything after it is not.
    ///
    /// TOLERANCES ARE MEASURED, NOT GUESSED. The reconstruction round-trip
    /// (ComputeFollowLocalPose -> ApplySimulatedDeviceForwardMap) was swept offline over 400,000 random
    /// pose/offset pairs with Unity's Quaternion/Vector3 float operation order replicated exactly:
    ///     position, offsets +-2 m / poses +-2 m : max 3.66 um, mean 0.44 um
    ///     position, offsets +-5 m / poses +-2.5 m: max 7.51 um
    ///     quaternion, componentwise             : max 2.384e-07 == 1 ULP at unit magnitude, all ranges
    /// The tolerances below are quoted against those numbers. Note that asserting on Quaternion.Angle
    /// instead would be wrong: acos(dot) is ill-conditioned near identity and reported spurious errors up
    /// to 0.105 deg on the SAME data that is exact to 1 ULP componentwise.
    /// </summary>
    public class BasisDeviceStreamReplayTests
    {
        // ---- corpus construction -------------------------------------------------------------------

        // Fixed LCG rather than System.Random: the corpus must be identical on every machine and every
        // runtime version, or "deterministic" is being tested against a moving target.
        private struct Lcg
        {
            private uint _state;
            public Lcg(uint seed) { _state = seed == 0 ? 1u : seed; }
            public uint NextUInt()
            {
                _state = (1664525u * _state) + 1013904223u;
                return _state;
            }
            public float NextUnit() => (NextUInt() >> 8) * (1f / 16777216f);
            public float Range(float min, float max) => min + ((max - min) * NextUnit());
        }

        private const int CorpusFrames = 120;
        private const int CorpusDevices = 6;

        // The six-point setup a real full-body session actually has, as raw BasisBoneTrackedRole values.
        // Written as bytes rather than the enum because BasisBoneTrackedRole lives in the SDK assembly,
        // which this test assembly does not reference (no IK test does) — and because the format stores
        // the role as a byte anyway, so this is the wire value being pinned. Values are from
        // com.basis.sdk/Scripts/Enum/BasisBoneTrackedRole.cs and are load-bearing: they are persisted in
        // every recording, so renumbering that enum silently reinterprets every existing fixture.
        private const byte RoleHead = 1;
        private const byte RoleHips = 4;
        private const byte RoleLeftFoot = 10;
        private const byte RoleRightFoot = 11;
        private const byte RoleLeftHand = 18;
        private const byte RoleRightHand = 19;

        private static readonly byte[] CorpusRoles =
        {
            RoleHead, RoleLeftHand, RoleRightHand, RoleHips, RoleLeftFoot, RoleRightFoot,
        };

        /// <summary>
        /// Synthesises a device stream shaped like a real session: believable poses, a non-uniform
        /// timestep (a real 90 Hz session hitches), a mid-stream dropout, a mid-stream role reassignment,
        /// and a populated calibration block with a non-identity OffsetCoords.
        /// </summary>
        private static BasisDeviceStreamRecording BuildCorpus(uint seed = 20260722u)
        {
            Lcg rng = new Lcg(seed);

            BasisDeviceStreamRecording recording = new BasisDeviceStreamRecording
            {
                SessionLabel = "headset_session",
                ProducerVersion = "test-fixture",
                CapturedUtcTicks = 638500000000000000L,
                NominalHz = 90f,
                WarmupFrames = 12,
                Calibration = BuildCalibration(),
                Devices = new List<BasisDeviceStreamDevice>(CorpusDevices),
                Frames = new List<BasisDeviceStreamFrame>(CorpusFrames),
                Samples = new BasisDeviceStreamSample[CorpusFrames * CorpusDevices],
            };

            for (int Index = 0; Index < CorpusDevices; Index++)
            {
                recording.Devices.Add(new BasisDeviceStreamDevice
                {
                    UniqueDeviceIdentifier = "device_" + Index.ToString(),
                    CommonDeviceIdentifier = "vive_tracker_3_0",
                    // Non-ASCII and a URI-shaped serial on purpose: SlimeVR encodes body part as
                    // "human://WAIST" and the classifier keys off it, so the codec must carry it intact.
                    DeviceSerial = Index == 3 ? "human://WAIST" : "SN-åß-" + Index.ToString(),
                    DeviceControllerType = "vive_tracker_waist",
                    SubSystemIdentifier = "OpenVR",
                    Role = CorpusRoles[Index],
                    TrackingHardware = (byte)(Index == 3 ? 6 : 2),
                    Flags = (byte)(BasisDeviceStreamDevice.FlagHasRoleAssigned
                                 | BasisDeviceStreamDevice.FlagHasCalibratedOffsetSnapshot
                                 | BasisDeviceStreamDevice.FlagUseInverseOffset),
                    CenterEyeVerticalOffset = Index == 0 ? 0.0163f : 0f,
                    CenterEyeOffset = Index == 0 ? new Vector3(0f, 0.0163f, 0.0271f) : Vector3.zero,
                    ScaledControlPositionOffset = Vector3.zero,
                    CalibratedUnscaledPosition = new Vector3(rng.Range(-0.5f, 0.5f), rng.Range(0.1f, 1.7f), rng.Range(-0.5f, 0.5f)),
                    CalibratedUnscaledRotation = RandomRotation(ref rng),
                    CalibratedUnscaledHeadPosition = new Vector3(0.03f, 1.65f, -0.02f),
                    CalibratedUnscaledHeadRotation = Quaternion.Euler(3f, 12f, -1f),
                    InverseOffsetPosition = new Vector3(rng.Range(-0.2f, 0.2f), rng.Range(-0.2f, 0.2f), rng.Range(-0.2f, 0.2f)),
                    InverseOffsetRotation = RandomRotation(ref rng),
                });
            }

            for (int Frame = 0; Frame < CorpusFrames; Frame++)
            {
                recording.Frames.Add(new BasisDeviceStreamFrame
                {
                    FrameIndex = Frame,
                    TimeSeconds = Frame * (1.0 / 90.0),
                    // Jittered around 90 Hz with a deliberate hitch: a constant timestep would let a
                    // framerate bug hide, and real sessions never have one.
                    DeltaTime = Frame == 61 ? 0.0413f : rng.Range(0.0105f, 0.0119f),
                });

                for (int Device = 0; Device < CorpusDevices; Device++)
                {
                    byte flags = BasisDeviceStreamSample.FlagConnected | BasisDeviceStreamSample.FlagHasRoleAssigned;

                    // Dropout: device 4 (left foot) vanishes for 7 frames, exactly as a tracker occluded
                    // behind the user's own leg does. One of the three things mocap cannot supply.
                    if (Device == 4 && Frame >= 40 && Frame < 47)
                    {
                        flags = 0;
                    }

                    // Role reassignment mid-session: device 5 loses its role assignment for a stretch,
                    // which is what a reconnect or a recalibration looks like from here.
                    byte role = CorpusRoles[Device];
                    if (Device == 5 && Frame >= 80 && Frame < 90)
                    {
                        flags &= unchecked((byte)~BasisDeviceStreamSample.FlagHasRoleAssigned);
                        role = 0;
                    }

                    recording.Samples[(Frame * CorpusDevices) + Device] = new BasisDeviceStreamSample
                    {
                        Flags = flags,
                        Role = role,
                        UnscaledPosition = new Vector3(rng.Range(-1f, 1f), rng.Range(0f, 2f), rng.Range(-1f, 1f)),
                        UnscaledRotation = RandomRotation(ref rng),
                        ScaledPosition = new Vector3(rng.Range(-2f, 2f), rng.Range(0f, 2f), rng.Range(-2f, 2f)),
                        ScaledRotation = RandomRotation(ref rng),
                    };
                }
            }

            recording.ValidateStructure();
            return recording;
        }

        private static BasisDeviceStreamCalibration BuildCalibration()
        {
            return new BasisDeviceStreamCalibration
            {
                DeviceScale = 1.0374f,
                ScaledToMatchValue = 1.1123f,
                AppliedUpScale = 1.0f,
                AvatarToPlayerRatioScaled = 0.9641f,
                PlayerToAvatarRatioScaled = 1.0372f,
                PlayerEyeHeight = 1.6431f,
                AvatarEyeHeight = 1.5837f,
                SelectedScaledPlayerHeight = 1.7112f,
                SelectedScaledAvatarHeight = 1.6503f,
                PlayerArmSpan = 1.6892f,
                AvatarArmSpan = 1.6001f,
                PlayerHipHeight = 0.9412f,
                AvatarHipHeight = 0.9033f,
                HeightModeGroundingOffset = -0.0121f,
                Flags = BasisDeviceStreamCalibration.FlagHasGenuinePlayerEyeHeight
                      | BasisDeviceStreamCalibration.FlagHasUserCalibratedHeight,
                // Non-identity on purpose: an identity OffsetCoords would make the reconstruction math
                // trivially correct and prove nothing.
                OffsetCoordsPosition = new Vector3(0.13f, -0.04f, 0.27f),
                OffsetCoordsRotation = Quaternion.Euler(0f, 37f, 0f),
            };
        }

        private static Quaternion RandomRotation(ref Lcg rng)
        {
            return Quaternion.Euler(rng.Range(-180f, 180f), rng.Range(-180f, 180f), rng.Range(-180f, 180f));
        }

        // ---- bit-exact comparison helpers ----------------------------------------------------------

        // Float EQUALITY via raw bits, not ==. Two reasons: NaN != NaN under ==, so an == comparison would
        // silently pass a codec that dropped a NaN; and bit comparison is the only definition of
        // "unchanged" strong enough to make a recording a fixture.
        private static void AssertBitEqual(float expected, float actual, string what)
        {
            int expectedBits = BitConverter.ToInt32(BitConverter.GetBytes(expected), 0);
            int actualBits = BitConverter.ToInt32(BitConverter.GetBytes(actual), 0);
            Assert.AreEqual(expectedBits, actualBits,
                $"{what}: bit pattern changed across the round trip (expected 0x{expectedBits:X8}, got 0x{actualBits:X8}).");
        }

        private static void AssertBitEqual(Vector3 expected, Vector3 actual, string what)
        {
            AssertBitEqual(expected.x, actual.x, what + ".x");
            AssertBitEqual(expected.y, actual.y, what + ".y");
            AssertBitEqual(expected.z, actual.z, what + ".z");
        }

        private static void AssertBitEqual(Quaternion expected, Quaternion actual, string what)
        {
            AssertBitEqual(expected.x, actual.x, what + ".x");
            AssertBitEqual(expected.y, actual.y, what + ".y");
            AssertBitEqual(expected.z, actual.z, what + ".z");
            AssertBitEqual(expected.w, actual.w, what + ".w");
        }

        private static void AssertRecordingsBitEqual(BasisDeviceStreamRecording expected, BasisDeviceStreamRecording actual)
        {
            Assert.AreEqual(expected.SessionLabel, actual.SessionLabel, "SessionLabel");
            Assert.AreEqual(expected.ProducerVersion, actual.ProducerVersion, "ProducerVersion");
            Assert.AreEqual(expected.CapturedUtcTicks, actual.CapturedUtcTicks, "CapturedUtcTicks");
            AssertBitEqual(expected.NominalHz, actual.NominalHz, "NominalHz");
            Assert.AreEqual(expected.WarmupFrames, actual.WarmupFrames, "WarmupFrames");
            Assert.AreEqual(expected.DeviceCount, actual.DeviceCount, "DeviceCount");
            Assert.AreEqual(expected.FrameCount, actual.FrameCount, "FrameCount");

            AssertCalibrationBitEqual(expected.Calibration, actual.Calibration);

            for (int Index = 0; Index < expected.DeviceCount; Index++)
            {
                BasisDeviceStreamDevice e = expected.Devices[Index];
                BasisDeviceStreamDevice a = actual.Devices[Index];
                string tag = "Devices[" + Index + "]";
                Assert.AreEqual(e.UniqueDeviceIdentifier, a.UniqueDeviceIdentifier, tag + ".UniqueDeviceIdentifier");
                Assert.AreEqual(e.CommonDeviceIdentifier, a.CommonDeviceIdentifier, tag + ".CommonDeviceIdentifier");
                Assert.AreEqual(e.DeviceSerial, a.DeviceSerial, tag + ".DeviceSerial");
                Assert.AreEqual(e.DeviceControllerType, a.DeviceControllerType, tag + ".DeviceControllerType");
                Assert.AreEqual(e.SubSystemIdentifier, a.SubSystemIdentifier, tag + ".SubSystemIdentifier");
                Assert.AreEqual(e.Role, a.Role, tag + ".Role");
                Assert.AreEqual(e.TrackingHardware, a.TrackingHardware, tag + ".TrackingHardware");
                Assert.AreEqual(e.Flags, a.Flags, tag + ".Flags");
                AssertBitEqual(e.CenterEyeVerticalOffset, a.CenterEyeVerticalOffset, tag + ".CenterEyeVerticalOffset");
                AssertBitEqual(e.CenterEyeOffset, a.CenterEyeOffset, tag + ".CenterEyeOffset");
                AssertBitEqual(e.ScaledControlPositionOffset, a.ScaledControlPositionOffset, tag + ".ScaledControlPositionOffset");
                AssertBitEqual(e.CalibratedUnscaledPosition, a.CalibratedUnscaledPosition, tag + ".CalibratedUnscaledPosition");
                AssertBitEqual(e.CalibratedUnscaledRotation, a.CalibratedUnscaledRotation, tag + ".CalibratedUnscaledRotation");
                AssertBitEqual(e.CalibratedUnscaledHeadPosition, a.CalibratedUnscaledHeadPosition, tag + ".CalibratedUnscaledHeadPosition");
                AssertBitEqual(e.CalibratedUnscaledHeadRotation, a.CalibratedUnscaledHeadRotation, tag + ".CalibratedUnscaledHeadRotation");
                AssertBitEqual(e.InverseOffsetPosition, a.InverseOffsetPosition, tag + ".InverseOffsetPosition");
                AssertBitEqual(e.InverseOffsetRotation, a.InverseOffsetRotation, tag + ".InverseOffsetRotation");
            }

            for (int Frame = 0; Frame < expected.FrameCount; Frame++)
            {
                BasisDeviceStreamFrame ef = expected.Frames[Frame];
                BasisDeviceStreamFrame af = actual.Frames[Frame];
                Assert.AreEqual(ef.FrameIndex, af.FrameIndex, "Frames[" + Frame + "].FrameIndex");
                Assert.AreEqual(ef.TimeSeconds, af.TimeSeconds, "Frames[" + Frame + "].TimeSeconds");
                AssertBitEqual(ef.DeltaTime, af.DeltaTime, "Frames[" + Frame + "].DeltaTime");

                for (int Device = 0; Device < expected.DeviceCount; Device++)
                {
                    BasisDeviceStreamSample es = expected.SampleAt(Frame, Device);
                    BasisDeviceStreamSample asample = actual.SampleAt(Frame, Device);
                    string tag = "Sample[" + Frame + "," + Device + "]";
                    Assert.AreEqual(es.Flags, asample.Flags, tag + ".Flags");
                    Assert.AreEqual(es.Role, asample.Role, tag + ".Role");
                    AssertBitEqual(es.UnscaledPosition, asample.UnscaledPosition, tag + ".UnscaledPosition");
                    AssertBitEqual(es.UnscaledRotation, asample.UnscaledRotation, tag + ".UnscaledRotation");
                    AssertBitEqual(es.ScaledPosition, asample.ScaledPosition, tag + ".ScaledPosition");
                    AssertBitEqual(es.ScaledRotation, asample.ScaledRotation, tag + ".ScaledRotation");
                }
            }
        }

        private static void AssertCalibrationBitEqual(BasisDeviceStreamCalibration e, BasisDeviceStreamCalibration a)
        {
            AssertBitEqual(e.DeviceScale, a.DeviceScale, "Calibration.DeviceScale");
            AssertBitEqual(e.ScaledToMatchValue, a.ScaledToMatchValue, "Calibration.ScaledToMatchValue");
            AssertBitEqual(e.AppliedUpScale, a.AppliedUpScale, "Calibration.AppliedUpScale");
            AssertBitEqual(e.AvatarToPlayerRatioScaled, a.AvatarToPlayerRatioScaled, "Calibration.AvatarToPlayerRatioScaled");
            AssertBitEqual(e.PlayerToAvatarRatioScaled, a.PlayerToAvatarRatioScaled, "Calibration.PlayerToAvatarRatioScaled");
            AssertBitEqual(e.PlayerEyeHeight, a.PlayerEyeHeight, "Calibration.PlayerEyeHeight");
            AssertBitEqual(e.AvatarEyeHeight, a.AvatarEyeHeight, "Calibration.AvatarEyeHeight");
            AssertBitEqual(e.SelectedScaledPlayerHeight, a.SelectedScaledPlayerHeight, "Calibration.SelectedScaledPlayerHeight");
            AssertBitEqual(e.SelectedScaledAvatarHeight, a.SelectedScaledAvatarHeight, "Calibration.SelectedScaledAvatarHeight");
            AssertBitEqual(e.PlayerArmSpan, a.PlayerArmSpan, "Calibration.PlayerArmSpan");
            AssertBitEqual(e.AvatarArmSpan, a.AvatarArmSpan, "Calibration.AvatarArmSpan");
            AssertBitEqual(e.PlayerHipHeight, a.PlayerHipHeight, "Calibration.PlayerHipHeight");
            AssertBitEqual(e.AvatarHipHeight, a.AvatarHipHeight, "Calibration.AvatarHipHeight");
            AssertBitEqual(e.HeightModeGroundingOffset, a.HeightModeGroundingOffset, "Calibration.HeightModeGroundingOffset");
            Assert.AreEqual(e.Flags, a.Flags, "Calibration.Flags");
            AssertBitEqual(e.OffsetCoordsPosition, a.OffsetCoordsPosition, "Calibration.OffsetCoordsPosition");
            AssertBitEqual(e.OffsetCoordsRotation, a.OffsetCoordsRotation, "Calibration.OffsetCoordsRotation");
        }

        // Reproduces BasisDeviceStreamPlayer.PushFrame's per-frame output without a scene: the local poses
        // it would write onto every simulated device's FollowMovement. This is the exact quantity that
        // must be identical across replays, because it is everything the IK pipeline receives.
        private static void ReplayToBuffer(
            BasisDeviceStreamRecording recording,
            Vector3 offsetPosition,
            Quaternion offsetRotation,
            Vector3[] outPositions,
            Quaternion[] outRotations)
        {
            int deviceCount = recording.DeviceCount;
            for (int Frame = 0; Frame < recording.FrameCount; Frame++)
            {
                for (int Device = 0; Device < deviceCount; Device++)
                {
                    int slot = (Frame * deviceCount) + Device;
                    BasisDeviceStreamSample sample = recording.SampleAt(Frame, Device);
                    if (!sample.Connected)
                    {
                        // Held pose on dropout, matching PushFrame: carry the previous frame forward.
                        outPositions[slot] = Frame > 0 ? outPositions[slot - deviceCount] : Vector3.zero;
                        outRotations[slot] = Frame > 0 ? outRotations[slot - deviceCount] : Quaternion.identity;
                        continue;
                    }
                    BasisDeviceStreamFormat.ComputeFollowLocalPose(
                        sample.ScaledPosition, sample.ScaledRotation,
                        offsetPosition, offsetRotation,
                        out outPositions[slot], out outRotations[slot]);
                }
            }
        }

        // ---- codec: round trip ---------------------------------------------------------------------

        /// <summary>
        /// Guards the whole premise: a recording written and read back is byte-identical in every field.
        /// If this fails the capability is worthless, because a fixture that mutates on load pins nothing.
        /// Headroom: NONE BY DESIGN — every float is compared as a raw bit pattern, so a single ULP of
        /// drift anywhere in a 120-frame x 6-device grid (4,320 poses, 30,240 floats) fails it.
        /// </summary>
        [Test]
        public void RoundTrip_Raw_IsBitExact()
        {
            BasisDeviceStreamRecording original = BuildCorpus();
            byte[] bytes = BasisDeviceStreamFormat.Write(original, BasisDeviceStreamFlags.None);
            BasisDeviceStreamRecording decoded = BasisDeviceStreamFormat.Read(bytes);
            AssertRecordingsBitEqual(original, decoded);
        }

        /// <summary>
        /// Guards the Deflate body path — the compression that keeps a 155 MB/hour raw stream tractable.
        /// Deflate is lossless, so the DECODED data must be bit-identical to both the original and the
        /// raw-path decode. Deliberately asserts nothing about the compressed BYTES: the encoder's output
        /// is a .NET implementation detail and may change between runtimes without anything being wrong.
        /// Headroom: none by design, as above.
        /// </summary>
        [Test]
        public void RoundTrip_Deflated_IsBitExact()
        {
            BasisDeviceStreamRecording original = BuildCorpus();
            byte[] deflated = BasisDeviceStreamFormat.Write(original, BasisDeviceStreamFlags.DeflateBody);
            BasisDeviceStreamRecording decoded = BasisDeviceStreamFormat.Read(deflated);
            AssertRecordingsBitEqual(original, decoded);

            byte[] raw = BasisDeviceStreamFormat.Write(original, BasisDeviceStreamFlags.None);
            AssertRecordingsBitEqual(BasisDeviceStreamFormat.Read(raw), decoded);
        }

        /// <summary>
        /// Guards against silent NaN/Infinity laundering. A tracker that glitched and emitted a non-finite
        /// pose is EVIDENCE; a codec that quietly normalises it to zero destroys the only record of the
        /// glitch and turns the recording into a lie. Note this cannot be written with ordinary equality —
        /// NaN != NaN — which is exactly why every comparison in this file is on raw bits.
        /// Headroom: none by design.
        /// </summary>
        [Test]
        public void NonFiniteSamples_SurviveRoundTripUnlaundered()
        {
            BasisDeviceStreamRecording original = BuildCorpus();
            BasisDeviceStreamSample poisoned = original.Samples[7 * CorpusDevices];
            poisoned.ScaledPosition = new Vector3(float.NaN, float.PositiveInfinity, float.NegativeInfinity);
            poisoned.UnscaledRotation = new Quaternion(float.NaN, 0f, -0f, 1f);
            original.Samples[7 * CorpusDevices] = poisoned;

            BasisDeviceStreamRecording decoded = BasisDeviceStreamFormat.Read(
                BasisDeviceStreamFormat.Write(original, BasisDeviceStreamFlags.None));

            BasisDeviceStreamSample readBack = decoded.SampleAt(7, 0);
            Assert.IsTrue(float.IsNaN(readBack.ScaledPosition.x), "NaN was laundered out of ScaledPosition.x");
            Assert.IsTrue(float.IsPositiveInfinity(readBack.ScaledPosition.y), "+Inf was laundered out of ScaledPosition.y");
            Assert.IsTrue(float.IsNegativeInfinity(readBack.ScaledPosition.z), "-Inf was laundered out of ScaledPosition.z");
            Assert.IsTrue(float.IsNaN(readBack.UnscaledRotation.x), "NaN was laundered out of UnscaledRotation.x");
            // Negative zero is a distinct bit pattern and must also survive.
            AssertBitEqual(-0f, readBack.UnscaledRotation.z, "UnscaledRotation.z (negative zero)");
        }

        // ---- codec: refusal ------------------------------------------------------------------------

        /// <summary>
        /// Guards the version field's whole purpose. These files are fixtures and will outlive the code
        /// that wrote them, so a stream from a future or past layout must be REFUSED, not guessed at — a
        /// silently misparsed recording would produce confident, wrong regression results. The message
        /// must name both versions so the reader knows which build to go find.
        /// </summary>
        [Test]
        public void VersionMismatch_IsRefusedAndNamesBothVersions()
        {
            byte[] bytes = BasisDeviceStreamFormat.Write(BuildCorpus(), BasisDeviceStreamFlags.None);
            uint bogusVersion = BasisDeviceStreamFormat.Version + 41u;
            Buffer.BlockCopy(BitConverter.GetBytes(bogusVersion), 0, bytes, 8, 4);

            BasisDeviceStreamFormatException e = Assert.Throws<BasisDeviceStreamFormatException>(
                () => BasisDeviceStreamFormat.Read(bytes));

            StringAssert.Contains(bogusVersion.ToString(), e.Message, "Refusal must name the version found.");
            StringAssert.Contains(BasisDeviceStreamFormat.Version.ToString(), e.Message, "Refusal must name the version supported.");
        }

        /// <summary>
        /// Guards the magic. A foreign or garbage file must be rejected before any field is parsed, so a
        /// mistyped path fails immediately instead of producing a plausible-looking empty session.
        /// </summary>
        [Test]
        public void ForeignFile_IsRefusedByMagic()
        {
            byte[] bytes = BasisDeviceStreamFormat.Write(BuildCorpus(), BasisDeviceStreamFlags.None);
            bytes[3] ^= 0xFF;
            BasisDeviceStreamFormatException e = Assert.Throws<BasisDeviceStreamFormatException>(
                () => BasisDeviceStreamFormat.Read(bytes));
            StringAssert.Contains("BASISDVS", e.Message);
        }

        /// <summary>
        /// Guards against a partial load. A truncated recording — an interrupted write, a half-copied
        /// file — must throw rather than return the frames it managed to read: a fixture that silently
        /// loses its tail would shorten the session and quietly change every temporal measurement taken
        /// from it. Tested at several truncation points because the failure mode differs by where the cut
        /// lands (header, device table, mid-frame).
        /// </summary>
        [Test]
        public void TruncatedStream_IsRefusedNotSilentlyPartial()
        {
            byte[] bytes = BasisDeviceStreamFormat.Write(BuildCorpus(), BasisDeviceStreamFlags.None);
            int[] cuts = { 4, BasisDeviceStreamFormat.HeaderLengthBytes, 64, bytes.Length / 2, bytes.Length - 1 };

            foreach (int cut in cuts)
            {
                byte[] truncated = new byte[cut];
                Buffer.BlockCopy(bytes, 0, truncated, 0, cut);
                Assert.Throws<BasisDeviceStreamFormatException>(
                    () => BasisDeviceStreamFormat.Read(truncated),
                    $"Truncation to {cut} of {bytes.Length} bytes was not refused.");
            }
        }

        /// <summary>
        /// Guards the allocation ceiling. A corrupt device or frame count must be rejected against the
        /// body length BEFORE it is used to size an array — otherwise a single flipped byte turns a
        /// 2 MB file into an OutOfMemoryException instead of a message naming the corruption.
        /// </summary>
        [Test]
        public void CorruptCounts_AreRefusedBeforeAllocating()
        {
            BasisDeviceStreamRecording corpus = BuildCorpus();
            byte[] bytes = BasisDeviceStreamFormat.Write(corpus, BasisDeviceStreamFlags.None);

            // The device count is the first int32 after the header + two strings + ticks + hz + warmup +
            // the fixed calibration block. Rather than compute that offset, corrupt every int32-aligned
            // position in the first part of the body and require that NOTHING escapes as a non-format
            // exception: every corruption either parses to something structurally valid or is refused
            // cleanly. What must never happen is an OutOfMemoryException or a silent giant allocation.
            for (int offset = BasisDeviceStreamFormat.HeaderLengthBytes; offset < Math.Min(bytes.Length - 4, 320); offset += 4)
            {
                byte[] corrupted = (byte[])bytes.Clone();
                Buffer.BlockCopy(BitConverter.GetBytes(0x3FFFFFFF), 0, corrupted, offset, 4);
                try
                {
                    BasisDeviceStreamFormat.Read(corrupted);
                }
                catch (BasisDeviceStreamFormatException)
                {
                    // Refused cleanly, which is the required behaviour.
                }
                catch (Exception e)
                {
                    Assert.Fail($"Corruption at byte {offset} escaped as {e.GetType().Name} rather than a clean refusal: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Guards the declared-body-length ceiling, and guards it BY MESSAGE rather than merely by "it
        /// threw". This exists because the first version of that ceiling was dead code: it compared an
        /// int32 body length against a long constant of 3.89e9 that no int32 can exceed, so it never fired
        /// and the compiler said so (CS0652). A test that only asserted "some exception" would have passed
        /// against the dead guard, because an oversized length also trips the truncation check further
        /// down. Asserting the specific message is what makes this test able to fail.
        /// </summary>
        [Test]
        public void OversizedDeclaredBodyLength_IsRefusedByTheCeilingNotByTruncation()
        {
            byte[] bytes = BasisDeviceStreamFormat.Write(BuildCorpus(), BasisDeviceStreamFlags.None);
            Buffer.BlockCopy(BitConverter.GetBytes(int.MaxValue), 0, bytes, 16, 4);

            BasisDeviceStreamFormatException e = Assert.Throws<BasisDeviceStreamFormatException>(
                () => BasisDeviceStreamFormat.Read(bytes));
            StringAssert.Contains("impossible body length", e.Message,
                "An oversized declared length must be refused by the ceiling, before any allocation is attempted.");
        }

        /// <summary>
        /// Guards the structural invariant that the sample grid is exactly frames x devices. A ragged grid
        /// is how a recorder bug (a dropped frame, a device appended without back-fill) would manifest, and
        /// it must be caught when the recording is BUILT rather than when it is replayed weeks later.
        /// </summary>
        [Test]
        public void RaggedSampleGrid_IsRejectedAtWrite()
        {
            BasisDeviceStreamRecording corpus = BuildCorpus();
            Array.Resize(ref corpus.Samples, corpus.Samples.Length - 1);
            Assert.Throws<BasisDeviceStreamFormatException>(
                () => BasisDeviceStreamFormat.Write(corpus, BasisDeviceStreamFlags.None));
        }

        // ---- determinism hazards -------------------------------------------------------------------

        /// <summary>
        /// Guards the FRAMERATE hazard. This codebase has shipped real framerate-dependent blends — a
        /// saturate(dt*speed) smoother whose time constant tracked GPU speed, and a self-referential slerp
        /// that converged by frame count rather than elapsed time. A replay at a different rate is
        /// therefore a different experiment, so the recorded timestep must survive exactly or a caller
        /// cannot even tell whether its replay rate matched. The corpus contains a deliberate 41 ms hitch
        /// at frame 61 for this reason; a codec that resampled to a uniform rate would erase it.
        /// Headroom: none by design (bit comparison).
        /// </summary>
        [Test]
        public void RecordedTimestep_SurvivesRoundTripExactly()
        {
            BasisDeviceStreamRecording original = BuildCorpus();
            BasisDeviceStreamRecording decoded = BasisDeviceStreamFormat.Read(
                BasisDeviceStreamFormat.Write(original, BasisDeviceStreamFlags.DeflateBody));

            for (int Frame = 0; Frame < original.FrameCount; Frame++)
            {
                AssertBitEqual(original.Frames[Frame].DeltaTime, decoded.Frames[Frame].DeltaTime, "Frame " + Frame + " DeltaTime");
            }
            AssertBitEqual(0.0413f, decoded.Frames[61].DeltaTime, "the deliberate hitch at frame 61");
            Assert.AreEqual(original.SummedDuration, decoded.SummedDuration, "SummedDuration");
        }

        /// <summary>
        /// Guards the FILTER-STATE hazard's declared half. Filter state is carried across frames even
        /// though bone pose is not, so a replay is only valid played from frame 0 and its leading frames
        /// are warm-up whose output must not be measured. The warm-up count is part of the contract and
        /// must survive the round trip, or a consumer will measure frames whose filters had not converged
        /// and read the difference as a regression.
        /// </summary>
        [Test]
        public void WarmupFrameContract_SurvivesRoundTrip()
        {
            BasisDeviceStreamRecording original = BuildCorpus();
            BasisDeviceStreamRecording decoded = BasisDeviceStreamFormat.Read(
                BasisDeviceStreamFormat.Write(original, BasisDeviceStreamFlags.None));
            Assert.AreEqual(12, decoded.WarmupFrames, "WarmupFrames");
            Assert.Less(decoded.WarmupFrames, decoded.FrameCount, "Warm-up must not consume the whole recording.");
        }

        /// <summary>
        /// Guards the DROPOUT and ROLE-REASSIGNMENT signals — two of the three things mocap structurally
        /// cannot supply. A tracker occluded behind the user's own leg, and a role that moves mid-session
        /// after a reconnect, are real events the solve must handle; a codec that flattened them would
        /// hand the replay a cleaner session than the headset ever saw.
        /// </summary>
        [Test]
        public void DropoutAndRoleReassignment_SurviveRoundTrip()
        {
            BasisDeviceStreamRecording decoded = BasisDeviceStreamFormat.Read(
                BasisDeviceStreamFormat.Write(BuildCorpus(), BasisDeviceStreamFlags.DeflateBody));

            Assert.IsTrue(decoded.SampleAt(39, 4).Connected, "left foot should be connected before the dropout");
            for (int Frame = 40; Frame < 47; Frame++)
            {
                Assert.IsFalse(decoded.SampleAt(Frame, 4).Connected, $"left foot dropout lost at frame {Frame}");
            }
            Assert.IsTrue(decoded.SampleAt(47, 4).Connected, "left foot should reconnect after the dropout");

            Assert.IsTrue(decoded.SampleAt(79, 5).HasRoleAssigned, "right foot should hold its role before reassignment");
            Assert.IsFalse(decoded.SampleAt(85, 5).HasRoleAssigned, "mid-session role loss was flattened");
            Assert.IsTrue(decoded.SampleAt(90, 5).HasRoleAssigned, "right foot should regain its role");
        }

        /// <summary>
        /// Guards the mandatory calibration block. A replay without calibration is not reproducible,
        /// because the same device stream under a different scale / offset / height is a DIFFERENT INPUT —
        /// it is the user's real avatar/body mismatch, the third thing mocap cannot supply. Every field
        /// must survive exactly; a rounded player height silently re-scales the whole session.
        /// Headroom: none by design (bit comparison).
        /// </summary>
        [Test]
        public void CalibrationBlock_SurvivesRoundTripExactly()
        {
            BasisDeviceStreamRecording original = BuildCorpus();
            BasisDeviceStreamRecording decoded = BasisDeviceStreamFormat.Read(
                BasisDeviceStreamFormat.Write(original, BasisDeviceStreamFlags.DeflateBody));
            AssertCalibrationBitEqual(original.Calibration, decoded.Calibration);
        }

        /// <summary>
        /// Guards the calibration drift report — the check that stops a replay being believed when it is
        /// measuring a different body. Identical blocks must report nothing (or every replay warns and the
        /// warning is ignored); a changed field must be named. Comparison is exact on purpose: a tolerance
        /// here would bless a replay whose avatar scale had moved.
        /// </summary>
        [Test]
        public void CalibrationDrift_ReportsNothingWhenIdenticalAndNamesWhatMoved()
        {
            BasisDeviceStreamCalibration recorded = BuildCalibration();
            Assert.IsEmpty(BasisDeviceStreamPlayer.DescribeCalibrationDrift(recorded, recorded),
                "Identical calibration must report no drift, or the warning becomes noise and gets ignored.");

            BasisDeviceStreamCalibration moved = BuildCalibration();
            moved.ScaledToMatchValue += 0.05f;
            moved.PlayerEyeHeight += 0.01f;
            string drift = BasisDeviceStreamPlayer.DescribeCalibrationDrift(recorded, moved);
            StringAssert.Contains("ScaledToMatchValue", drift);
            StringAssert.Contains("PlayerEyeHeight", drift);
            Assert.IsFalse(drift.Contains("AvatarArmSpan"), "Unchanged fields must not be reported as drift.");

            // One ULP of scale drift is still drift: the avatar is a different size and the whole stream
            // means something else. No tolerance.
            BasisDeviceStreamCalibration ulp = BuildCalibration();
            ulp.DeviceScale = BitConverter.ToSingle(
                BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(ulp.DeviceScale), 0) + 1), 0);
            StringAssert.Contains("DeviceScale", BasisDeviceStreamPlayer.DescribeCalibrationDrift(recorded, ulp));
        }

        // ---- the injection seam --------------------------------------------------------------------

        /// <summary>
        /// Guards the injection seam itself. The replay feeds simulated devices by writing a FollowMovement
        /// local pose; BasisInputXRSimulate.LateDoPollData then maps that forward into ScaledDeviceCoord,
        /// which is what actually reaches the bone. This test runs the reconstruction and then the REAL
        /// forward map and requires the recorded scaled pose to come back out — i.e. that the replayed
        /// device publishes exactly what the headset published. Without this the replay could be perfectly
        /// deterministic and perfectly wrong.
        ///
        /// Headroom: tolerance 1e-5 m against a measured max of 3.66 um over 400,000 random pose/offset
        /// pairs at these ranges (mean 0.44 um) = 2.7x. Rotation tolerance 1e-6 componentwise against a
        /// measured max of 2.384e-07 (1 ULP at unit magnitude) = 4.2x. Componentwise, not Quaternion.Angle:
        /// acos(dot) is ill-conditioned near identity and reports up to 0.105 deg of spurious error on data
        /// that is exact to 1 ULP.
        /// </summary>
        [Test]
        public void FollowPoseReconstruction_RepublishesRecordedScaledPose()
        {
            BasisDeviceStreamRecording recording = BuildCorpus();
            Vector3 offsetPosition = recording.Calibration.OffsetCoordsPosition;
            Quaternion offsetRotation = recording.Calibration.OffsetCoordsRotation;

            float worstPosition = 0f;
            float worstComponent = 0f;

            for (int Frame = 0; Frame < recording.FrameCount; Frame++)
            {
                for (int Device = 0; Device < recording.DeviceCount; Device++)
                {
                    BasisDeviceStreamSample sample = recording.SampleAt(Frame, Device);
                    if (!sample.Connected)
                    {
                        continue;
                    }

                    BasisDeviceStreamFormat.ComputeFollowLocalPose(
                        sample.ScaledPosition, sample.ScaledRotation,
                        offsetPosition, offsetRotation,
                        out Vector3 localPosition, out Quaternion localRotation);

                    BasisDeviceStreamFormat.ApplySimulatedDeviceForwardMap(
                        localPosition, localRotation,
                        offsetPosition, offsetRotation,
                        out Vector3 republishedPosition, out Quaternion republishedRotation);

                    worstPosition = Mathf.Max(worstPosition, Vector3.Distance(sample.ScaledPosition, republishedPosition));
                    worstComponent = Mathf.Max(worstComponent, MaxQuaternionComponentError(sample.ScaledRotation, republishedRotation));
                }
            }

            Assert.Less(worstPosition, 1e-5f,
                $"Replayed device position drifted {worstPosition:E3} m from the recorded pose (budget 1e-5 m, measured baseline 3.66e-6 m).");
            Assert.Less(worstComponent, 1e-6f,
                $"Replayed device rotation drifted {worstComponent:E3} componentwise from the recorded pose (budget 1e-6, measured baseline 2.38e-7).");
        }

        /// <summary>
        /// Guards against OffsetCoords drift. OffsetCoords is a process-global that the playspace mover and
        /// calibration both write, so it will generally NOT match the recording at replay time. The
        /// reconstruction solves against the LIVE value precisely so the republished scaled pose is
        /// unchanged — this test proves that immunity by replaying the same stream under a wildly
        /// different offset and requiring the same scaled pose out. If this regressed, every replay run
        /// after a playspace nudge would silently shift the whole body.
        /// Headroom: as above, 2.7x on position / 4.2x on rotation.
        /// </summary>
        [Test]
        public void FollowPoseReconstruction_IsImmuneToOffsetCoordsDrift()
        {
            BasisDeviceStreamRecording recording = BuildCorpus();
            Vector3 liveOffsetPosition = new Vector3(-1.87f, 0.42f, 3.05f);
            Quaternion liveOffsetRotation = Quaternion.Euler(11f, -143f, 6f);

            float worstPosition = 0f;
            float worstComponent = 0f;

            for (int Frame = 0; Frame < recording.FrameCount; Frame++)
            {
                for (int Device = 0; Device < recording.DeviceCount; Device++)
                {
                    BasisDeviceStreamSample sample = recording.SampleAt(Frame, Device);
                    if (!sample.Connected)
                    {
                        continue;
                    }

                    BasisDeviceStreamFormat.ComputeFollowLocalPose(
                        sample.ScaledPosition, sample.ScaledRotation,
                        liveOffsetPosition, liveOffsetRotation,
                        out Vector3 localPosition, out Quaternion localRotation);

                    BasisDeviceStreamFormat.ApplySimulatedDeviceForwardMap(
                        localPosition, localRotation,
                        liveOffsetPosition, liveOffsetRotation,
                        out Vector3 republishedPosition, out Quaternion republishedRotation);

                    worstPosition = Mathf.Max(worstPosition, Vector3.Distance(sample.ScaledPosition, republishedPosition));
                    worstComponent = Mathf.Max(worstComponent, MaxQuaternionComponentError(sample.ScaledRotation, republishedRotation));
                }
            }

            Assert.Less(worstPosition, 1e-5f,
                $"Replay under a drifted OffsetCoords moved the device position by {worstPosition:E3} m.");
            Assert.Less(worstComponent, 1e-6f,
                $"Replay under a drifted OffsetCoords moved the device rotation by {worstComponent:E3} componentwise.");
        }

        // Sign-corrected for the quaternion double cover (q and -q are the same rotation), then the largest
        // absolute component difference. Well-conditioned everywhere, unlike acos(dot).
        private static float MaxQuaternionComponentError(Quaternion expected, Quaternion actual)
        {
            float dot = (expected.x * actual.x) + (expected.y * actual.y) + (expected.z * actual.z) + (expected.w * actual.w);
            float sign = dot < 0f ? -1f : 1f;
            float e = Mathf.Abs(expected.x - (sign * actual.x));
            e = Mathf.Max(e, Mathf.Abs(expected.y - (sign * actual.y)));
            e = Mathf.Max(e, Mathf.Abs(expected.z - (sign * actual.z)));
            e = Mathf.Max(e, Mathf.Abs(expected.w - (sign * actual.w)));
            return e;
        }

        // ---- the headline property: replay determinism ---------------------------------------------

        /// <summary>
        /// THE TEST THAT MATTERS MOST. Replaying one recording twice must hand the IK pipeline the exact
        /// same input, bit for bit, on every frame of every device. If this fails the whole capability is
        /// worthless as a regression tool — a fixture whose input wanders cannot distinguish a code change
        /// from its own noise, and no amount of downstream tolerance would rescue it.
        ///
        /// Scope, stated honestly: this pins the input to the solve — the poses written onto every
        /// simulated device's FollowMovement — and proves that path is a pure function of (recording,
        /// live OffsetCoords). It does NOT pin the solve's OUTPUT, which additionally depends on carried
        /// filter state and on the timestep the pipeline runs at, and which cannot be exercised without
        /// play mode.
        /// Headroom: none by design — 4,320 poses compared as raw bit patterns.
        /// </summary>
        [Test]
        public void ReplayTwice_HandsThePipelineIdenticalInput()
        {
            BasisDeviceStreamRecording recording = BasisDeviceStreamFormat.Read(
                BasisDeviceStreamFormat.Write(BuildCorpus(), BasisDeviceStreamFlags.DeflateBody));

            Vector3 offsetPosition = recording.Calibration.OffsetCoordsPosition;
            Quaternion offsetRotation = recording.Calibration.OffsetCoordsRotation;
            int slots = recording.FrameCount * recording.DeviceCount;

            Vector3[] firstPositions = new Vector3[slots];
            Quaternion[] firstRotations = new Quaternion[slots];
            Vector3[] secondPositions = new Vector3[slots];
            Quaternion[] secondRotations = new Quaternion[slots];

            ReplayToBuffer(recording, offsetPosition, offsetRotation, firstPositions, firstRotations);
            ReplayToBuffer(recording, offsetPosition, offsetRotation, secondPositions, secondRotations);

            for (int Index = 0; Index < slots; Index++)
            {
                AssertBitEqual(firstPositions[Index], secondPositions[Index], "replay position slot " + Index);
                AssertBitEqual(firstRotations[Index], secondRotations[Index], "replay rotation slot " + Index);
            }
        }

        /// <summary>
        /// The same determinism property across the FILE boundary: a recording written, read, written and
        /// read again must replay identically to the original in memory. This is what "permanent test
        /// data" actually requires — the fixture has to survive a trip through the disk format without
        /// changing what it asks the solve to do, however many times it makes that trip.
        /// Headroom: none by design.
        /// </summary>
        [Test]
        public void ReplayAcrossReEncode_IsIdenticalToTheOriginal()
        {
            BasisDeviceStreamRecording original = BuildCorpus();
            BasisDeviceStreamRecording once = BasisDeviceStreamFormat.Read(
                BasisDeviceStreamFormat.Write(original, BasisDeviceStreamFlags.None));
            BasisDeviceStreamRecording twice = BasisDeviceStreamFormat.Read(
                BasisDeviceStreamFormat.Write(once, BasisDeviceStreamFlags.DeflateBody));

            AssertRecordingsBitEqual(original, twice);

            Vector3 offsetPosition = original.Calibration.OffsetCoordsPosition;
            Quaternion offsetRotation = original.Calibration.OffsetCoordsRotation;
            int slots = original.FrameCount * original.DeviceCount;

            Vector3[] originalPositions = new Vector3[slots];
            Quaternion[] originalRotations = new Quaternion[slots];
            Vector3[] reEncodedPositions = new Vector3[slots];
            Quaternion[] reEncodedRotations = new Quaternion[slots];

            ReplayToBuffer(original, offsetPosition, offsetRotation, originalPositions, originalRotations);
            ReplayToBuffer(twice, offsetPosition, offsetRotation, reEncodedPositions, reEncodedRotations);

            for (int Index = 0; Index < slots; Index++)
            {
                AssertBitEqual(originalPositions[Index], reEncodedPositions[Index], "re-encoded replay position slot " + Index);
                AssertBitEqual(originalRotations[Index], reEncodedRotations[Index], "re-encoded replay rotation slot " + Index);
            }
        }
    }
}
