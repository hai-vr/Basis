using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Basis.Tests.Camera
{
    public class BasisHandHeldCameraDirectToScreenTests
    {
        private const string EncodeKeyword = HDROutputUtils.ShaderKeywords.HDR_COLORSPACE_CONVERSION_AND_ENCODING;
        private Material _material;

        [SetUp]
        public void SetUp()
        {
            Shader shader = Resources.Load<Shader>("BasisDirectToScreen");
            Assert.That(shader, Is.Not.Null, "BasisDirectToScreen shader is missing from the camera package's Resources folder.");
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        [TearDown]
        public void TearDown()
        {
            if (_material != null) Object.DestroyImmediate(_material);
        }

        [Test]
        public void EncodesOnlyWhenTheEngineDrawsTheOverlayIntoAnHdrBackbuffer()
        {
            Assert.That(BasisHandHeldCamera.DirectToScreenNeedsHdrEncode(true, false), Is.True);
            Assert.That(BasisHandHeldCamera.DirectToScreenNeedsHdrEncode(true, true), Is.False);
            Assert.That(BasisHandHeldCamera.DirectToScreenNeedsHdrEncode(false, false), Is.False);
            Assert.That(BasisHandHeldCamera.DirectToScreenNeedsHdrEncode(false, true), Is.False);
        }

        [Test]
        public void AnHdrDisplayTurnsOnTheEncodeAndCarriesItsLimits()
        {
            BasisHandHeldCamera.ConfigureDirectToScreenMaterial(_material, true, ColorGamut.HDR10, 1000f);

            Assert.That(_material.IsKeywordEnabled(EncodeKeyword), Is.True);
            Assert.That(_material.GetFloat("_PaperWhite"), Is.EqualTo(BasisHandHeldCamera.DirectToScreenPaperWhiteNits));
            Assert.That(_material.GetFloat("_MaxNits"), Is.EqualTo(1000f));
        }

        [Test]
        public void AnSdrDisplayLeavesTheFeedUntouched()
        {
            BasisHandHeldCamera.ConfigureDirectToScreenMaterial(_material, true, ColorGamut.HDR10, 1000f);
            BasisHandHeldCamera.ConfigureDirectToScreenMaterial(_material, false, ColorGamut.sRGB, 0f);

            Assert.That(_material.IsKeywordEnabled(EncodeKeyword), Is.False);
        }

        [Test]
        public void ADisplayWithNoPeakFallsBackToPaperWhite()
        {
            BasisHandHeldCamera.ConfigureDirectToScreenMaterial(_material, true, ColorGamut.Rec709, 0f);

            Assert.That(_material.GetFloat("_MaxNits"), Is.EqualTo(BasisHandHeldCamera.DirectToScreenPaperWhiteNits));
        }

        [Test]
        public void PaperWhiteMatchesTheXrMirrorViewsSdrWhite()
        {
            Assert.That(BasisHandHeldCamera.DirectToScreenPaperWhiteNits, Is.EqualTo(160f));
        }

        [Test]
        public void AMissingMaterialIsIgnored()
        {
            Assert.DoesNotThrow(() => BasisHandHeldCamera.ConfigureDirectToScreenMaterial(null, true, ColorGamut.HDR10, 1000f));
        }
    }
}
