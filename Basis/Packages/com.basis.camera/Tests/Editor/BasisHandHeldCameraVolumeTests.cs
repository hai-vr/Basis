using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityCamera = UnityEngine.Camera;

namespace Basis.Tests.Camera
{
    public class BasisHandHeldCameraVolumeTests
    {
        private const string PrefabPath = "Packages/com.basis.camera/Prefabs/Player Held Camera.prefab";
        private const int StaleLayer = 8;
        private const int VolumeLayer = 11;

        private GameObject _go;
        private GameObject _captureGo;
        private GameObject _volumeGo;
        private BasisHandHeldCamera _camera;
        private Volume _volume;
        private VolumeProfile _profile;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("HandHeldCameraUnderTest");
            _camera = _go.AddComponent<BasisHandHeldCamera>();

            _captureGo = new GameObject("CaptureCamera");
            _captureGo.transform.SetParent(_go.transform, false);
            _camera.captureCamera = _captureGo.AddComponent<UnityCamera>();
            _camera.CameraData = _camera.captureCamera.GetUniversalAdditionalCameraData();
            _camera.CameraData.volumeLayerMask = 1 << StaleLayer;
            _camera.CameraData.volumeTrigger = null;
            _camera.CameraData.renderPostProcessing = false;

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _camera.MetaData.Profile = _profile;

            _volumeGo = new GameObject("Camera PP");
            _volumeGo.transform.SetParent(_captureGo.transform, false);
            _volumeGo.layer = VolumeLayer;
            _volume = _volumeGo.AddComponent<Volume>();
            _volume.isGlobal = false;
            _volume.sharedProfile = _profile;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            if (_profile != null) Object.DestroyImmediate(_profile);
        }

        [Test]
        public void TheCaptureCameraSeesTheLayerItsOwnVolumeIsOn()
        {
            _camera.InitializePostProcessingVolume();

            Assert.That(_camera.CameraData.volumeLayerMask.value & (1 << VolumeLayer), Is.Not.Zero);
            Assert.That(_camera.CameraData.volumeTrigger, Is.EqualTo(_volume.transform));
            Assert.That(_camera.CameraData.renderPostProcessing, Is.True);
        }

        [Test]
        public void TheMaskFollowsTheVolumeToWhateverLayerItIsMovedTo()
        {
            _volumeGo.layer = 20;
            _camera.InitializePostProcessingVolume();

            Assert.That(_camera.CameraData.volumeLayerMask.value & (1 << 20), Is.Not.Zero);
            Assert.That(_camera.CameraData.volumeLayerMask.value & (1 << StaleLayer), Is.Zero);
        }

        [Test]
        public void TheVolumeRendersTheProfileTheSettingsWriteTo()
        {
            VolumeProfile other = ScriptableObject.CreateInstance<VolumeProfile>();
            try
            {
                _volume.sharedProfile = other;
                _camera.InitializePostProcessingVolume();

                Assert.That(_volume.sharedProfile, Is.EqualTo(_profile));
                Assert.That(_camera.MetaData.Profile, Is.EqualTo(_profile));
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void AProfileMissingFromTheMetadataIsTakenFromTheVolume()
        {
            _camera.MetaData.Profile = null;
            _camera.InitializePostProcessingVolume();

            Assert.That(_camera.MetaData.Profile, Is.EqualTo(_profile));
        }

        [Test]
        public void ACameraWithNoVolumeLeavesItsCameraDataAlone()
        {
            Object.DestroyImmediate(_volumeGo);

            Assert.DoesNotThrow(() => _camera.InitializePostProcessingVolume());
            Assert.That(_camera.CameraData.volumeLayerMask.value, Is.EqualTo(1 << StaleLayer));
        }

        [Test]
        public void ThePrefabsCaptureCameraSeesItsOwnVolume()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);

            BasisHandHeldCamera camera = prefab.GetComponentInChildren<BasisHandHeldCamera>(true);
            Volume volume = prefab.GetComponentInChildren<Volume>(true);
            Assert.That(camera, Is.Not.Null);
            Assert.That(volume, Is.Not.Null);
            Assert.That(camera.CameraData, Is.Not.Null);

            int volumeLayer = volume.gameObject.layer;
            Assert.That(camera.CameraData.volumeLayerMask.value & (1 << volumeLayer), Is.Not.Zero,
                $"Volume mask {camera.CameraData.volumeLayerMask.value} does not include layer {volumeLayer} ({LayerMask.LayerToName(volumeLayer)}), the layer the camera's own post-processing volume is on.");
            Assert.That(camera.CameraData.volumeTrigger, Is.EqualTo(volume.transform));
            Assert.That(camera.CameraData.renderPostProcessing, Is.True);
            Assert.That(volume.sharedProfile, Is.EqualTo(camera.MetaData.Profile));
            Assert.That(volume.isGlobal, Is.False);
            Assert.That(volume.GetComponent<Collider>(), Is.Not.Null);
        }
    }
}
