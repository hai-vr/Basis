using NUnit.Framework;
using UnityEngine;
using SSGIPass = ScreenSpaceGlobalIlluminationURP.ScreenSpaceGlobalIlluminationPass;

namespace SSGIURP.Tests
{
    public class ScreenSpaceGlobalIlluminationHistoryTests
    {
        private SSGIPass pass;

        [SetUp]
        public void SetUp()
        {
            pass = new SSGIPass(null);
        }

        [TearDown]
        public void TearDown()
        {
            pass.Dispose();
        }

        private void RegisterCamera(int hash)
        {
            Assert.AreEqual(-1, pass.GetCameraHistoryDataIndex(hash), "camera " + hash + " should be new");
            pass.UpdateCameraHistoryData(true);
            pass.cameraHistoryData[0].hash = hash;
            pass.cameraHistoryData[0].hasMatrices = true;
            pass.cameraHistoryData[0].textureValid = true;
        }

        [Test]
        public void EmptyHistoryKnowsNoCamera()
        {
            Assert.AreEqual(-1, pass.GetCameraHistoryDataIndex(1234));
        }

        [Test]
        public void KnownCameraKeepsItsSlot()
        {
            RegisterCamera(7);
            Assert.AreEqual(0, pass.GetCameraHistoryDataIndex(7));
            pass.UpdateCameraHistoryData(false);
            Assert.AreEqual(0, pass.GetCameraHistoryDataIndex(7));
            Assert.IsTrue(pass.cameraHistoryData[0].hasMatrices);
        }

        [Test]
        public void NewCameraShiftsOthersBackAndStartsWithoutHistory()
        {
            RegisterCamera(7);
            pass.UpdateCameraHistoryData(true);

            Assert.AreEqual(7, pass.cameraHistoryData[1].hash);
            Assert.IsTrue(pass.cameraHistoryData[1].hasMatrices);
            Assert.AreEqual(0, pass.cameraHistoryData[0].hash);
            Assert.IsFalse(pass.cameraHistoryData[0].hasMatrices);
            Assert.IsFalse(pass.cameraHistoryData[0].textureValid);
            Assert.AreEqual(1, pass.GetCameraHistoryDataIndex(7));
        }

        [Test]
        public void FifthCameraEvictsTheOldest()
        {
            for (int hash = 1; hash <= 5; hash++)
                RegisterCamera(hash);

            Assert.AreEqual(0, pass.GetCameraHistoryDataIndex(5));
            Assert.AreEqual(1, pass.GetCameraHistoryDataIndex(4));
            Assert.AreEqual(2, pass.GetCameraHistoryDataIndex(3));
            Assert.AreEqual(3, pass.GetCameraHistoryDataIndex(2));
            Assert.AreEqual(-1, pass.GetCameraHistoryDataIndex(1));
        }

        [Test]
        public void ReleasingASlotClearsIt()
        {
            SSGIPass.CameraHistoryData slot = new SSGIPass.CameraHistoryData
            {
                hash = 3,
                hasMatrices = true,
                textureValid = true,
                scaledWidth = 640,
            };
            SSGIPass.ReleaseHistory(ref slot);
            Assert.AreEqual(0, slot.hash);
            Assert.IsFalse(slot.hasMatrices);
            Assert.IsFalse(slot.textureValid);
            Assert.AreEqual(0f, slot.scaledWidth);
        }

        [Test]
        public void HashWithoutXrIsTheCameraHash()
        {
            GameObject go = new GameObject("history-hash-camera");
            try
            {
                Camera camera = go.AddComponent<Camera>();
                Assert.AreEqual(camera.GetHashCode(), SSGIPass.ComputeCameraHistoryHash(camera, null));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void HistoryPairsSwapEveryFrame()
        {
            SSGIPass.CameraHistoryData slot = default;
            SSGIPass.SelectHistory(ref slot, out int write0, out int read0);
            SSGIPass.SelectHistory(ref slot, out int write1, out int read1);
            Assert.AreNotEqual(write0, read0);
            Assert.AreEqual(read0, write1);
            Assert.AreEqual(write0, read1);
            Assert.IsTrue(write0 == 0 || write0 == 1);
        }

        [Test]
        public void SlotCountLeavesRoomForBothEyesAndTwoMoreCameras()
        {
            Assert.GreaterOrEqual(SSGIPass.MAX_CAMERA_COUNT, 4);
            Assert.AreEqual(SSGIPass.MAX_CAMERA_COUNT, pass.cameraHistoryData.Length);
        }
    }
}
