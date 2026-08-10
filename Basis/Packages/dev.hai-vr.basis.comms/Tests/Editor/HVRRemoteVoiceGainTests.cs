using System.Collections.Generic;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using NUnit.Framework;
using UnityEngine;

namespace HVR.Basis.Comms.Tests
{
    /// The reported bug end to end, across both sides of the seam: a real BasisAudioAndVisemeDriver
    /// recycling its pooled OpenLipSync context, read frame by frame by the real publisher, over a
    /// speak / pause / speak cycle. Voice Gain used to survive the first utterance and then sit at
    /// 0 for the rest of the session.
    public class HVRRemoteVoiceGainTests
    {
        private const int VisemeCount = BasisOpenLipSyncContext.VisemeCount;
        private const int AaIndex = 10;
        private const int GainAddress = 9001;
        private const HVRBasisBuiltInAddressesVisemeFlags Gain = HVRBasisBuiltInAddressesVisemeFlags.Gain;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<Mesh> _meshes = new List<Mesh>();

        private int[] _addressIds;
        private HVRVariableStore _store;

        [SetUp]
        public void SetUp()
        {
            _addressIds = new int[VisemeCount];
            for (var index = 0; index < VisemeCount; index++)
            {
                _addressIds[index] = index + 1;
            }
            _store = new HVRVariableStore();
        }

        [TearDown]
        public void TearDown()
        {
            for (var index = 0; index < _spawned.Count; index++)
            {
                if (_spawned[index] != null) Object.DestroyImmediate(_spawned[index]);
            }
            for (var index = 0; index < _meshes.Count; index++)
            {
                if (_meshes[index] != null) Object.DestroyImmediate(_meshes[index]);
            }
            _spawned.Clear();
            _meshes.Clear();
        }

        private BasisAudioAndVisemeDriver BuildRemoteDriver()
        {
            var root = new GameObject("RemoteVoiceAvatar");
            _spawned.Add(root);

            var mesh = new Mesh();
            _meshes.Add(mesh);
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            var delta = new[] { Vector3.up, Vector3.up, Vector3.up };
            for (var index = 0; index < VisemeCount; index++)
            {
                mesh.AddBlendShapeFrame($"viseme{index}", 100f, delta, null, null);
            }

            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;

            var avatar = root.AddComponent<BasisAvatar>();
            avatar.FaceVisemeMesh = renderer;
            avatar.FaceVisemeMovement = new int[VisemeCount];
            for (var index = 0; index < VisemeCount; index++)
            {
                avatar.FaceVisemeMovement[index] = index;
            }

            var mouth = new GameObject("Mouth");
            _spawned.Add(mouth);

            var remote = new BasisRemotePlayer
            {
                DisplayName = "remote-voice-gain-test",
                UUID = System.Guid.NewGuid().ToString("N"),
                MouthTransform = mouth.transform,
                BasisAvatar = avatar,
                FaceIsVisible = true,
            };

            var driver = new BasisAudioAndVisemeDriver();
            Assert.IsTrue(driver.TryInitialize(remote));
            driver.InVisemeRange = true;
            driver.FaceVisible = true;

            var source = new GameObject("SpatialSource");
            _spawned.Add(source);
            driver.TrackedAudioSource = source.AddComponent<AudioSource>();
            driver.AudioSourceInactive = false;
            return driver;
        }

        /// The backend is not available offline, so speech is staged by planting a context the way
        /// TryAcquireOpenLipSyncContext would and writing the weight Apply would have left behind.
        private static void StartSpeaking(BasisAudioAndVisemeDriver driver, float visemeWeight)
        {
            driver.openLipSyncContext = new BasisOpenLipSyncContext();
            driver.UseOpenLipSync = true;
            driver.AudioSourceInactive = false;
            driver.openLipSyncContext.LastApplied[AaIndex] = visemeWeight;
        }

        [Test]
        public void It_should_keep_reporting_gain_after_a_pause_recycles_the_context()
        {
            // Given
            var publisher = new HVRBuiltInAddressPublisher(_addressIds, GainAddress);
            var driver = BuildRemoteDriver();

            StartSpeaking(driver, 60f);
            driver.Simulate(0.016f);
            publisher.Publish(_store, driver.openLipSyncContext, Gain);
            Assert.AreEqual(0.6f, _store.GetValue(GainAddress), 1e-5f, "the first utterance always worked");

            // When — they go quiet long enough for BasisAudioReceiver to disable the source
            driver.AudioSourceInactive = true;
            driver.Simulate(0.016f);
            publisher.Publish(_store, driver.openLipSyncContext, Gain);
            Assert.AreEqual(0f, _store.GetValue(GainAddress), 1e-5f, "silence has to read as silence");

            // ...and then speak again, which acquires a fresh context
            StartSpeaking(driver, 80f);
            driver.Simulate(0.016f);
            publisher.Publish(_store, driver.openLipSyncContext, Gain);

            // Then
            Assert.AreEqual(0.8f, _store.GetValue(GainAddress), 1e-5f, "voice gain stayed at 0 for the rest of the session");
        }

        [Test]
        public void It_should_keep_reporting_gain_after_a_range_round_trip()
        {
            // Given
            var publisher = new HVRBuiltInAddressPublisher(_addressIds, GainAddress);
            var driver = BuildRemoteDriver();

            StartSpeaking(driver, 60f);
            driver.Simulate(0.016f);
            publisher.Publish(_store, driver.openLipSyncContext, Gain);
            Assert.AreEqual(0.6f, _store.GetValue(GainAddress), 1e-5f);

            // When — walking out of viseme range recycles the context just as an idle gap does
            driver.InVisemeRange = false;
            driver.Simulate(0.016f);
            publisher.Publish(_store, driver.openLipSyncContext, Gain);
            Assert.AreEqual(0f, _store.GetValue(GainAddress), 1e-5f);

            driver.InVisemeRange = true;
            StartSpeaking(driver, 45f);
            driver.Simulate(0.016f);
            publisher.Publish(_store, driver.openLipSyncContext, Gain);

            // Then
            Assert.AreEqual(0.45f, _store.GetValue(GainAddress), 1e-5f, "walking back into range never restored the glow");
        }

        [Test]
        public void It_should_track_the_gain_within_a_single_utterance()
        {
            // Given
            var publisher = new HVRBuiltInAddressPublisher(_addressIds, GainAddress);
            var driver = BuildRemoteDriver();
            StartSpeaking(driver, 20f);

            // When
            driver.Simulate(0.016f);
            publisher.Publish(_store, driver.openLipSyncContext, Gain);
            Assert.AreEqual(0.2f, _store.GetValue(GainAddress), 1e-5f);

            driver.openLipSyncContext.LastApplied[AaIndex] = 90f;
            driver.Simulate(0.016f);
            publisher.Publish(_store, driver.openLipSyncContext, Gain);

            // Then
            Assert.AreEqual(0.9f, _store.GetValue(GainAddress), 1e-5f);
        }
    }
}
