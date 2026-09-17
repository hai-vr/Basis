using Basis.Scripts.Drivers;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Drivers
{
    public class BasisAvatarPreviewHeadScaleTests
    {
        static readonly Vector3 AuthoredScale = new Vector3(1.5f, 1.25f, 1f);
        BasisLocalAvatarDriver savedInstance;
        Transform savedHead;
        bool savedHasHead, savedIsNormalHead;
        Vector3 savedHeadScale;
        BasisLocalAvatarDriver.HeadChopEntry[] savedChop;
        int savedChopLength;
        GameObject headGO, previewGO, otherGO;
        BasisLocalAvatarPreviewDriver driver;

        [SetUp]
        public void SetUp()
        {
            savedInstance = BasisLocalAvatarDriver.Instance;
            savedHead = BasisLocalAvatarDriver.Mapping.head;
            savedHasHead = BasisLocalAvatarDriver.Mapping.Hashead;
            savedIsNormalHead = BasisLocalAvatarDriver.IsNormalHead;
            savedHeadScale = BasisLocalAvatarDriver.HeadScale;
            savedChop = BasisLocalAvatarDriver.HeadChopEntries;
            savedChopLength = BasisLocalAvatarDriver.HeadChopEntriesLength;

            headGO = new GameObject("BasisAvatarPreviewHeadScaleTests.Head");
            previewGO = new GameObject("BasisAvatarPreviewHeadScaleTests.Preview");
            otherGO = new GameObject("BasisAvatarPreviewHeadScaleTests.Other");

            BasisLocalAvatarDriver.Instance = new BasisLocalAvatarDriver();
            BasisLocalAvatarDriver.Mapping.head = headGO.transform;
            BasisLocalAvatarDriver.Mapping.Hashead = true;
            BasisLocalAvatarDriver.HeadScale = AuthoredScale;
            BasisLocalAvatarDriver.HeadChopEntries = System.Array.Empty<BasisLocalAvatarDriver.HeadChopEntry>();
            BasisLocalAvatarDriver.HeadChopEntriesLength = 0;
            headGO.transform.localScale = AuthoredScale;
            BasisLocalAvatarDriver.IsNormalHead = true;

            driver = new BasisLocalAvatarPreviewDriver { PreviewCamera = previewGO.AddComponent<UnityEngine.Camera>() };
        }

        [TearDown]
        public void TearDown()
        {
            BasisLocalAvatarDriver.Instance = savedInstance;
            BasisLocalAvatarDriver.Mapping.head = savedHead;
            BasisLocalAvatarDriver.Mapping.Hashead = savedHasHead;
            BasisLocalAvatarDriver.IsNormalHead = savedIsNormalHead;
            BasisLocalAvatarDriver.HeadScale = savedHeadScale;
            BasisLocalAvatarDriver.HeadChopEntries = savedChop;
            BasisLocalAvatarDriver.HeadChopEntriesLength = savedChopLength;
            Object.DestroyImmediate(headGO);
            Object.DestroyImmediate(previewGO);
            Object.DestroyImmediate(otherGO);
        }

        static void MirrorBracket()
        {
            BasisLocalAvatarDriver.ScaleHeadToNormal();
            BasisLocalAvatarDriver.ScaleHeadToZero();
        }

        [Test]
        public void PreviewCameraRestoresAHeadTheMirrorLeftZeroed()
        {
            MirrorBracket();
            Assert.AreEqual(Vector3.zero, headGO.transform.localScale);
            Assert.IsFalse(BasisLocalAvatarDriver.IsNormalHead);

            driver.OnBeginCameraRendering(default, driver.PreviewCamera);

            Assert.AreEqual(AuthoredScale, headGO.transform.localScale);
            Assert.IsTrue(BasisLocalAvatarDriver.IsNormalHead);
        }

        [Test]
        public void OtherCamerasLeaveTheHeadAlone()
        {
            MirrorBracket();

            driver.OnBeginCameraRendering(default, otherGO.AddComponent<UnityEngine.Camera>());

            Assert.AreEqual(Vector3.zero, headGO.transform.localScale);
            Assert.IsFalse(BasisLocalAvatarDriver.IsNormalHead);
        }

        [Test]
        public void NoPreviewCameraIsInert()
        {
            MirrorBracket();
            driver.PreviewCamera = null;

            driver.OnBeginCameraRendering(default, otherGO.AddComponent<UnityEngine.Camera>());

            Assert.AreEqual(Vector3.zero, headGO.transform.localScale);
            Assert.IsFalse(BasisLocalAvatarDriver.IsNormalHead);
        }

        [Test]
        public void FirstPersonMainCameraStillHidesTheHeadAfterThePreview()
        {
            MirrorBracket();
            driver.OnBeginCameraRendering(default, driver.PreviewCamera);
            Assert.AreEqual(AuthoredScale, headGO.transform.localScale);

            BasisLocalAvatarDriver.ScaleHeadToZero();

            Assert.AreEqual(Vector3.zero, headGO.transform.localScale);
            Assert.IsFalse(BasisLocalAvatarDriver.IsNormalHead);
        }

        [Test]
        public void PreviewClaimIsIdempotentWhenTheHeadIsAlreadyNormal()
        {
            driver.OnBeginCameraRendering(default, driver.PreviewCamera);
            driver.OnBeginCameraRendering(default, driver.PreviewCamera);

            Assert.AreEqual(AuthoredScale, headGO.transform.localScale);
            Assert.IsTrue(BasisLocalAvatarDriver.IsNormalHead);
        }
    }
}
