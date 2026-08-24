using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.Tests.Camera
{
    public class BasisHandHeldCameraDirectToScreenRenderTests
    {
        private const int Size = 32;
        private const float Tolerance = 0.03f;
        private GameObject _root;
        private UnityEngine.Camera _camera;
        private RawImage _image;
        private RenderTexture _target;
        private Texture2D _source;
        private Texture2D _readback;
        private Material _material;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("DirectToScreenRenderRig");

            _camera = new GameObject("Camera").AddComponent<UnityEngine.Camera>();
            _camera.transform.SetParent(_root.transform, false);
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 1f);
            _camera.orthographic = true;
            _camera.allowHDR = true;
            _camera.allowMSAA = false;
            _target = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBHalf) { name = "DirectToScreenRenderTarget" };
            _target.Create();
            _camera.targetTexture = _target;

            GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform));
            canvasObject.transform.SetParent(_root.transform, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _camera;
            canvas.planeDistance = 1f;

            GameObject feed = new GameObject("Feed", typeof(RectTransform));
            feed.transform.SetParent(canvasObject.transform, false);
            _image = feed.AddComponent<RawImage>();
            RectTransform rect = _image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _source = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            Color32[] pixels = new Color32[4];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 0, 0, 255);
            _source.SetPixels32(pixels);
            _source.Apply();
            _image.texture = _source;

            Shader shader = Resources.Load<Shader>("BasisDirectToScreen");
            Assert.That(shader, Is.Not.Null);
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _readback = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
        }

        [TearDown]
        public void TearDown()
        {
            RenderTexture.active = null;
            if (_camera != null) _camera.targetTexture = null;
            if (_root != null) Object.DestroyImmediate(_root);
            if (_material != null) Object.DestroyImmediate(_material);
            if (_source != null) Object.DestroyImmediate(_source);
            if (_readback != null) Object.DestroyImmediate(_readback);
            if (_target != null) { _target.Release(); Object.DestroyImmediate(_target); }
        }

        private Color RenderCentre()
        {
            Canvas.ForceUpdateCanvases();
            _camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _target;
            _readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            _readback.Apply();
            RenderTexture.active = previous;
            return _readback.GetPixel(Size / 2, Size / 2);
        }

        private static void AssertColour(Color actual, float r, float g, float b, string why)
        {
            Assert.That(actual.r, Is.EqualTo(r).Within(Tolerance), why + " (red)");
            Assert.That(actual.g, Is.EqualTo(g).Within(Tolerance), why + " (green)");
            Assert.That(actual.b, Is.EqualTo(b).Within(Tolerance), why + " (blue)");
        }

        private static float LinearToPq(float nits)
        {
            float y = nits / 10000f;
            float ym1 = Mathf.Pow(y, 2610f / 4096f / 4f);
            float n = 3424f / 4096f + (2413f / 4096f * 32f) * ym1;
            float d = 1f + (2392f / 4096f * 32f) * ym1;
            return Mathf.Pow(n / d, 2523f / 4096f * 128f);
        }

        [Test]
        public void TheDefaultUiMaterialDrawsTheFeed()
        {
            _image.material = null;
            AssertColour(RenderCentre(), 1f, 0f, 0f, "UI/Default control draw");
        }

        [Test]
        public void WithoutHdrTheShaderDrawsExactlyWhatTheDefaultMaterialDraws()
        {
            _image.material = _material;
            BasisHandHeldCamera.ConfigureDirectToScreenMaterial(_material, false, ColorGamut.sRGB, 0f);
            AssertColour(RenderCentre(), 1f, 0f, 0f, "encode off");
        }

        [Test]
        public void AnScRgbDisplayGetsPaperWhiteOverReferenceWhite()
        {
            _image.material = _material;
            BasisHandHeldCamera.ConfigureDirectToScreenMaterial(_material, true, ColorGamut.Rec709, 1000f);
            float expected = BasisHandHeldCamera.DirectToScreenPaperWhiteNits / 80f;
            AssertColour(RenderCentre(), expected, 0f, 0f, "scRGB encode");
        }

        [Test]
        public void AnHdr10DisplayGetsRec2020PrimariesAndAPqCurve()
        {
            _image.material = _material;
            BasisHandHeldCamera.ConfigureDirectToScreenMaterial(_material, true, ColorGamut.HDR10, 1000f);
            float nits = BasisHandHeldCamera.DirectToScreenPaperWhiteNits;
            AssertColour(RenderCentre(), LinearToPq(0.627402f * nits), LinearToPq(0.069095f * nits), LinearToPq(0.016394f * nits), "HDR10 encode");
        }

        [Test]
        public void SwitchingTheEncodeOffAgainRestoresThePlainFeed()
        {
            _image.material = _material;
            BasisHandHeldCamera.ConfigureDirectToScreenMaterial(_material, true, ColorGamut.Rec709, 1000f);
            RenderCentre();
            BasisHandHeldCamera.ConfigureDirectToScreenMaterial(_material, false, ColorGamut.sRGB, 0f);
            AssertColour(RenderCentre(), 1f, 0f, 0f, "encode off after on");
        }
    }
}
