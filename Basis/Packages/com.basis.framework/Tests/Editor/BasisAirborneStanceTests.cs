using Basis.Scripts.BasisCharacterController;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Framework.Tests
{
    public class BasisAirborneStanceTests
    {
        private const float Tolerance = 1e-4f;
        private const float HeadHeight = 1.6f;

        private static BasisLocalCharacterDriver GroundedDriver()
        {
            BasisLocalCharacterDriver driver = new BasisLocalCharacterDriver
            {
                MinimumMovementSpeed = 0.5f,
                DefaultMovementSpeed = 2.5f,
                MaximumMovementSpeed = 4f,
                MinimumCrouchPercent = 0.5f,
                ProneMovementSpeed = 0.35f,
                CrouchBlend = 1f,
                groundedPlayer = true,
            };
            driver.SetMovementVector(Vector2.up);
            driver.UpdateMovementSpeed(false);
            return driver;
        }

        /// <summary>
        /// The horizontal speed the movement modes derive from the published multipliers, before the
        /// avatar-size factor (1 at default size).
        /// </summary>
        private static float HorizontalSpeed(BasisLocalCharacterDriver driver)
        {
            return Mathf.Lerp(driver.MinimumMovementSpeed, driver.MaximumMovementSpeed, driver.MovementSpeedScale)
                + driver.MinimumMovementSpeed * driver.MovementSpeedBoost;
        }

        [Test]
        public void CrouchingOnTheGroundStillSlowsYouDown()
        {
            BasisLocalCharacterDriver driver = GroundedDriver();
            float standing = HorizontalSpeed(driver);

            driver.CrouchBlend = 0f;
            driver.SyncStanceSpeedSource();

            Assert.That(HorizontalSpeed(driver), Is.LessThan(standing * 0.5f));
        }

        [Test]
        public void CrouchingInMidAirLeavesSpeedAlone()
        {
            BasisLocalCharacterDriver driver = GroundedDriver();
            float takeoff = HorizontalSpeed(driver);

            driver.groundedPlayer = false;
            driver.CrouchBlend = 0f;
            driver.SyncStanceSpeedSource();

            Assert.That(HorizontalSpeed(driver), Is.EqualTo(takeoff).Within(Tolerance));
        }

        [Test]
        public void UncrouchingInMidAirLeavesSpeedAlone()
        {
            BasisLocalCharacterDriver driver = GroundedDriver();
            driver.CrouchBlend = 0f;
            driver.SyncStanceSpeedSource();
            float takeoff = HorizontalSpeed(driver);

            driver.groundedPlayer = false;
            driver.CrouchBlend = 1f;
            driver.SyncStanceSpeedSource();

            Assert.That(HorizontalSpeed(driver), Is.EqualTo(takeoff).Within(Tolerance));
        }

        [Test]
        public void GoingProneInMidAirLeavesSpeedAlone()
        {
            BasisLocalCharacterDriver driver = GroundedDriver();

            driver.groundedPlayer = false;
            driver.IsProne = true;
            driver.SyncStanceSpeedSource();

            Assert.That(driver.StanceSpeedProne, Is.False);
        }

        [Test]
        public void LandingWhileCrouchedRestoresTheSlowdown()
        {
            BasisLocalCharacterDriver driver = GroundedDriver();
            float standing = HorizontalSpeed(driver);

            driver.groundedPlayer = false;
            driver.CrouchBlend = 0f;
            driver.SyncStanceSpeedSource();
            Assert.That(HorizontalSpeed(driver), Is.EqualTo(standing).Within(Tolerance));

            driver.groundedPlayer = true;
            driver.SyncStanceSpeedSource();

            Assert.That(HorizontalSpeed(driver), Is.LessThan(standing * 0.5f));
        }

        [Test]
        public void FlyingTracksTheLiveStanceBecauseThereIsNoGroundToLeave()
        {
            BasisLocalCharacterDriver driver = GroundedDriver();
            float standing = HorizontalSpeed(driver);

            driver.CurrentModeKind = BasisLocalCharacterDriver.Mode.Fly;
            driver.groundedPlayer = false;
            driver.CrouchBlend = 0f;
            driver.SyncStanceSpeedSource();

            Assert.That(HorizontalSpeed(driver), Is.LessThan(standing * 0.5f));
        }

        [Test]
        public void CrouchDropSpansTheStanceRange()
        {
            Assert.That(BasisLocalCharacterDriver.CrouchHeightDrop(HeadHeight, 0.5f, 1f), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(BasisLocalCharacterDriver.CrouchHeightDrop(HeadHeight, 0.5f, 0f), Is.EqualTo(0.8f).Within(Tolerance));
            Assert.That(BasisLocalCharacterDriver.CrouchHeightDrop(HeadHeight, 0.5f, 0.5f), Is.EqualTo(0.4f).Within(Tolerance));
        }

        [Test]
        public void CrouchDropRejectsUnusableHeadHeights()
        {
            Assert.That(BasisLocalCharacterDriver.CrouchHeightDrop(float.NaN, 0.5f, 0f), Is.EqualTo(0f));
            Assert.That(BasisLocalCharacterDriver.CrouchHeightDrop(float.PositiveInfinity, 0.5f, 0f), Is.EqualTo(0f));
            Assert.That(BasisLocalCharacterDriver.CrouchHeightDrop(0f, 0.5f, 0f), Is.EqualTo(0f));
            Assert.That(BasisLocalCharacterDriver.CrouchHeightDrop(-1f, 0.5f, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void GroundedStanceChangesMoveNothing()
        {
            float takeoff = 0f;
            float applied = 0f;

            Assert.That(BasisLocalCharacterDriver.ResolveStanceLift(true, 0.8f, ref takeoff, ref applied), Is.EqualTo(0f));
            Assert.That(takeoff, Is.EqualTo(0.8f).Within(Tolerance));
            Assert.That(applied, Is.EqualTo(0f));
        }

        [Test]
        public void CrouchingInMidAirLiftsTheRootByTheViewpointDrop()
        {
            float takeoff = 0f;
            float applied = 0f;
            BasisLocalCharacterDriver.ResolveStanceLift(true, 0f, ref takeoff, ref applied);

            float first = BasisLocalCharacterDriver.ResolveStanceLift(false, 0.4f, ref takeoff, ref applied);
            float second = BasisLocalCharacterDriver.ResolveStanceLift(false, 0.8f, ref takeoff, ref applied);

            Assert.That(first, Is.EqualTo(0.4f).Within(Tolerance));
            Assert.That(second, Is.EqualTo(0.4f).Within(Tolerance));
            Assert.That(applied, Is.EqualTo(0.8f).Within(Tolerance));
        }

        [Test]
        public void UncrouchingInMidAirPushesTheFeetBackDown()
        {
            float takeoff = 0f;
            float applied = 0f;
            BasisLocalCharacterDriver.ResolveStanceLift(true, 0.8f, ref takeoff, ref applied);

            float extend = BasisLocalCharacterDriver.ResolveStanceLift(false, 0f, ref takeoff, ref applied);

            Assert.That(extend, Is.EqualTo(-0.8f).Within(Tolerance));
        }

        [Test]
        public void ACrouchUncrouchCycleInMidAirIsANetZeroLift()
        {
            float takeoff = 0f;
            float applied = 0f;
            BasisLocalCharacterDriver.ResolveStanceLift(true, 0f, ref takeoff, ref applied);

            float total = 0f;
            total += BasisLocalCharacterDriver.ResolveStanceLift(false, 0.4f, ref takeoff, ref applied);
            total += BasisLocalCharacterDriver.ResolveStanceLift(false, 0.8f, ref takeoff, ref applied);
            total += BasisLocalCharacterDriver.ResolveStanceLift(false, 0.4f, ref takeoff, ref applied);
            total += BasisLocalCharacterDriver.ResolveStanceLift(false, 0f, ref takeoff, ref applied);

            Assert.That(total, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(applied, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void LandingReArmsAgainstTheStanceTheNextJumpLeavesIn()
        {
            float takeoff = 0f;
            float applied = 0f;
            BasisLocalCharacterDriver.ResolveStanceLift(true, 0f, ref takeoff, ref applied);
            BasisLocalCharacterDriver.ResolveStanceLift(false, 0.8f, ref takeoff, ref applied);

            BasisLocalCharacterDriver.ResolveStanceLift(true, 0.8f, ref takeoff, ref applied);

            Assert.That(applied, Is.EqualTo(0f));
            Assert.That(BasisLocalCharacterDriver.ResolveStanceLift(false, 0.8f, ref takeoff, ref applied), Is.EqualTo(0f).Within(Tolerance));
        }
    }
}
