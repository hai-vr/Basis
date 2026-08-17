using Basis.Scripts.Common;
using Basis.Scripts.Device_Management;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// On desktop the handheld camera holds the player's look and movement locks for as long as it
    /// is out, so the mouse drives the camera's panel rather than the avatar. Hiding the camera
    /// takes it off the screen without destroying it, and anything it is still holding at that
    /// point is held by something the user can no longer see or close — a look lockout with no
    /// camera on screen to explain it.
    /// </summary>
    public class BasisHandHeldCameraHiddenLockTests
    {
        private const string CameraOwner = "BasisHandHeldCameraInteractable";

        private BasisCameraSettingsRig _rig;
        private GameObject _deviceManagement;
        private BasisDeviceManagement _previousInstance;

        private static BasisLocks.LockContext Look => BasisLocks.GetContext(BasisLocks.LookRotation);
        private static BasisLocks.LockContext Movement => BasisLocks.GetContext(BasisLocks.Movement);

        [SetUp]
        public void SetUp()
        {
            _previousInstance = BasisDeviceManagement.Instance;

            _deviceManagement = new GameObject("DeviceManagementUnderTest");
            BasisDeviceManagement device = _deviceManagement.AddComponent<BasisDeviceManagement>();
            device.CurrentMode = BasisConstants.Desktop;
            BasisDeviceManagement.Instance = device;

            Look.Clear();
            Movement.Clear();

            _rig = new BasisCameraSettingsRig();
        }

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;

            BasisDeviceManagement.Instance = _previousInstance;
            if (_deviceManagement != null) Object.DestroyImmediate(_deviceManagement);

            Look.Clear();
            Movement.Clear();
            BasisCursorManagement.OnReset();
        }

        [Test]
        public void HidingTheCamera_HandsTheLookAndMovementLocksBack()
        {
            _rig.Camera.AcquireCursorLock();
            Assert.That(Look.Contains(CameraOwner), Is.True,
                "The camera takes the look lock while it is out, which is what this is about releasing.");

            _rig.Camera.SetCameraHidden(true);

            Assert.That(Look.Contains(CameraOwner), Is.False,
                "A hidden camera cannot be seen or closed, so a look lock it keeps can never be lifted.");
            Assert.That(Movement.Contains(CameraOwner), Is.False);
        }

        [Test]
        public void ShowingTheCameraAgain_TakesTheLocksBack()
        {
            _rig.Camera.AcquireCursorLock();
            _rig.Camera.SetCameraHidden(true);

            _rig.Camera.SetCameraHidden(false);

            Assert.That(Look.Contains(CameraOwner), Is.True,
                "A camera back on screen drives its own controls again.");
            Assert.That(Movement.Contains(CameraOwner), Is.True);
        }

        [Test]
        public void HidingAFlyingCamera_LeavesThePlayersControlsWhereFlightPutThem()
        {
            // Flight is driven by the same keys the player moves with, and it carries on while the
            // camera is hidden. Handing the controls back mid-flight would fly the camera and walk
            // the avatar with one stick.
            _rig.Camera.SetFlyModeEnabled(true);

            _rig.Camera.SetCameraHidden(true);

            Assert.That(Look.Contains(CameraOwner), Is.True);
            Assert.That(Movement.Contains(CameraOwner), Is.True);

            _rig.Camera.SetFlyModeEnabled(false);
        }
    }
}
