using Basis.BasisUI.HandHeldCamera;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// The fly switch on the main menu's hotbar. It is opt-in per camera and gated on a camera
    /// being there at all, so a bar carrying a switch that drives nothing fails here.
    /// </summary>
    public class BasisHandHeldCameraFlyMenuTests
    {
        private readonly System.Collections.Generic.List<GameObject> _spawned = new System.Collections.Generic.List<GameObject>();

        private BasisHandHeldCamera NewCamera(string name)
        {
            GameObject go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<BasisHandHeldCamera>();
        }

        [SetUp]
        public void SetUp() => DrainRegistry();

        [TearDown]
        public void TearDown()
        {
            DrainRegistry();
            for (int Index = 0; Index < _spawned.Count; Index++)
            {
                if (_spawned[Index] != null) Object.DestroyImmediate(_spawned[Index]);
            }
            _spawned.Clear();
        }

        // Process-wide static state, drained from a snapshot for the same reason the registry
        // tests do it: Remove early-returns for a destroyed camera, so a count-driven loop spins.
        private static void DrainRegistry()
        {
            var snapshot = new System.Collections.Generic.List<BasisHandHeldCamera>(
                BasisHandHeldCameraRegistry.Cameras);

            for (int Index = 0; Index < snapshot.Count; Index++)
            {
                BasisHandHeldCameraRegistry.Remove(snapshot[Index]);
            }
        }

        [Test]
        public void TheSwitch_StaysOffTheHotbarUntilACameraAsksForIt()
        {
            BasisHandHeldCameraFlyProvider provider = new BasisHandHeldCameraFlyProvider();
            BasisHandHeldCamera camera = NewCamera("FlyMenuOptIn");
            BasisHandHeldCameraRegistry.Add(camera);

            Assert.That(provider.Hidden, Is.True, "a spawned camera alone must not put the switch on the bar");

            camera.SetShowFlyOnMainMenu(true);

            Assert.That(provider.Hidden, Is.False);
        }

        [Test]
        public void TheSwitch_LeavesTheHotbarWithTheLastCamera()
        {
            BasisHandHeldCameraFlyProvider provider = new BasisHandHeldCameraFlyProvider();
            BasisHandHeldCamera camera = NewCamera("FlyMenuDespawn");
            camera.SetShowFlyOnMainMenu(true);
            BasisHandHeldCameraRegistry.Add(camera);

            Assert.That(provider.Hidden, Is.False);

            BasisHandHeldCameraRegistry.Remove(camera);

            Assert.That(provider.Hidden, Is.True, "the switch would otherwise drive a camera that is gone");
        }

        [Test]
        public void SetShowFlyOnMainMenu_OnlyRaisesOnAnActualChange()
        {
            // The handler rebuilds every hotbar button, so a settings load that re-asserts the
            // value it already holds must not tear the bar down and put it back.
            int raised = 0;
            System.Action handler = () => raised++;
            BasisHandHeldCameraInteractable.OnFlyMenuVisibilityChanged += handler;

            try
            {
                BasisHandHeldCamera camera = NewCamera("FlyMenuEvent");

                camera.SetShowFlyOnMainMenu(true);
                camera.SetShowFlyOnMainMenu(true);

                Assert.That(raised, Is.EqualTo(1));
                Assert.That(camera.showFlyOnMainMenu, Is.True);

                camera.SetShowFlyOnMainMenu(false);

                Assert.That(raised, Is.EqualTo(2));
            }
            finally
            {
                BasisHandHeldCameraInteractable.OnFlyMenuVisibilityChanged -= handler;
            }
        }
    }
}
