#if !BASIS_DISABLE_MICROPHONE
using Basis.BasisUI.Styling;
using Basis.Scripts.Drivers;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.BasisUI
{
    public class BasisMicrophoneWaveformView : MonoBehaviour
    {
        public const string ShaderName = "Basis/UI/MicrophoneWaveform";
        public const float DefaultHeight = 210f;

        private const int TextureWidth = 512;
        private const int TextureHeight = 256;
        private const float VerticalPadding = 6f;
        private const float StallSeconds = 0.1f;
        private const float WarnAmplitude = 0.85f;
        private const float MinimumBarPixels = 1.5f;
        private const float GlowLevels = 0.05f;

        private static readonly int ColumnsProperty = Shader.PropertyToID("_Columns");
        private static readonly int BackgroundProperty = Shader.PropertyToID("_Background");
        private static readonly int LeftProperty = Shader.PropertyToID("_Left");
        private static readonly int RightProperty = Shader.PropertyToID("_Right");
        private static readonly int HotProperty = Shader.PropertyToID("_Hot");
        private static readonly int CentreLineProperty = Shader.PropertyToID("_CentreLine");
        private static readonly int GateLineProperty = Shader.PropertyToID("_GateLine");
        private static readonly int MutedColourProperty = Shader.PropertyToID("_MutedColour");
        private static readonly int OldestProperty = Shader.PropertyToID("_Oldest");
        private static readonly int StereoProperty = Shader.PropertyToID("_Stereo");
        private static readonly int ScaleProperty = Shader.PropertyToID("_Scale");
        private static readonly int CentreHalfProperty = Shader.PropertyToID("_CentreHalf");
        private static readonly int LineHalfProperty = Shader.PropertyToID("_LineHalf");
        private static readonly int MinimumBarProperty = Shader.PropertyToID("_MinimumBar");
        private static readonly int GateLevelProperty = Shader.PropertyToID("_GateLevel");
        private static readonly int WarnLevelProperty = Shader.PropertyToID("_WarnLevel");
        private static readonly int MutedProperty = Shader.PropertyToID("_Muted");
        private static readonly int GlowProperty = Shader.PropertyToID("_Glow");

        private RawImage image;
        private RenderTexture target;
        private Material material;
        private long lastPublished = -1;
        private long lastCaptured = -1;
        private float stalledFor;
        private Color lastBackground;
        private Color lastGuide;
        private int lastChannels = -1;
        private bool constantsPushed;
        private bool shaderMissingReported;

        public static BasisMicrophoneWaveformView Create(RectTransform parent, float height = DefaultHeight)
        {
            GameObject hosting = new GameObject("Microphone Waveform", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            hosting.layer = parent != null ? parent.gameObject.layer : hosting.layer;

            RectTransform rect = (RectTransform)hosting.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(parent != null ? parent.rect.width : 0f, height);

            RawImage raw = hosting.GetComponent<RawImage>();
            raw.raycastTarget = false;
            raw.color = Color.white;
            raw.material = BasisWaveformGraphic.ResolveOverlayMaterial();

            return hosting.AddComponent<BasisMicrophoneWaveformView>();
        }

        private void OnEnable()
        {
            lastPublished = -1;
            lastCaptured = -1;
            stalledFor = 0f;
            EnsureResources();
            BasisMicrophoneWaveform.AddSubscriber();
            BasisFrameClock.AddRequest();
            BasisFrameClock.OnTick += OnFrameTick;
            Redraw();
        }

        private void OnDisable()
        {
            BasisFrameClock.OnTick -= OnFrameTick;
            BasisFrameClock.RemoveRequest();
            BasisMicrophoneWaveform.RemoveSubscriber();
        }

        private void OnDestroy()
        {
            if (image != null) image.texture = null;

            if (target != null)
            {
                target.Release();
                Destroy(target);
                target = null;
            }

            if (material != null)
            {
                Destroy(material);
                material = null;
            }
        }

        private bool EnsureResources()
        {
            if (image == null) image = GetComponent<RawImage>();

            if (image != null)
            {
                Material overlay = BasisWaveformGraphic.ResolveOverlayMaterial();
                if (overlay != null && image.material != overlay) image.material = overlay;
            }

            if (material == null)
            {
                Shader shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    if (!shaderMissingReported)
                    {
                        shaderMissingReported = true;
                        BasisDebug.LogError($"Shader '{ShaderName}' not found — the microphone waveform will not render.");
                    }
                    return false;
                }

                material = new Material(shader) { name = "Microphone Waveform", hideFlags = HideFlags.HideAndDontSave };
                constantsPushed = false;
            }

            if (target == null)
            {
                target = new RenderTexture(TextureWidth, TextureHeight, 0, RenderTextureFormat.ARGB32)
                {
                    name = "Microphone Waveform",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                target.Create();
            }

            if (image != null && image.texture != target) image.texture = target;
            return true;
        }

        private void OnFrameTick()
        {
            float delta = Time.unscaledDeltaTime;

            long capturedNow = BasisMicrophoneWaveform.Captured;
            if (capturedNow != lastCaptured)
            {
                lastCaptured = capturedNow;
                stalledFor = 0f;
            }
            else
            {
                stalledFor += delta;
                if (stalledFor > StallSeconds) BasisMicrophoneWaveform.PushIdle(delta);
            }

            if (BasisMicrophoneWaveform.Published == lastPublished) return;
            lastPublished = BasisMicrophoneWaveform.Published;
            Redraw();
        }

        private void Redraw()
        {
            if (!EnsureResources()) return;

            UiStylePalette palette = UiStyleSettings.GetActivePalette();
            Color background = palette != null ? palette.InputFieldColor : new Color(0.13f, 0.13f, 0.15f);
            Color guide = palette != null ? palette.FontColor3 : new Color(0.65f, 0.67f, 0.69f);
            int channels = BasisMicrophoneWaveform.Channels;

            if (!constantsPushed || channels != lastChannels || background != lastBackground || guide != lastGuide)
            {
                lastBackground = background;
                lastGuide = guide;
                lastChannels = channels;
                PushConstants(palette, background, guide, channels);
            }

            SMDMicrophone.MicSettings settings = SMDMicrophone.Current;
            float gate = settings.UseNoiseGate ? Mathf.Clamp01(BasisLocalMicrophoneDriver.GateThreshold) : 0f;

            material.SetVectorArray(ColumnsProperty, BasisMicrophoneWaveform.Packed);
            material.SetFloat(OldestProperty, BasisMicrophoneWaveform.Oldest);
            material.SetFloat(StereoProperty, channels > 1 ? 1f : 0f);
            material.SetFloat(GateLevelProperty, gate);
            material.SetFloat(MutedProperty, BasisLocalMicrophoneDriver.isPaused ? 1f : 0f);

            Graphics.Blit(Texture2D.whiteTexture, target, material);
        }

        private void PushConstants(UiStylePalette palette, Color background, Color guide, int channels)
        {
            constantsPushed = true;

            Color accent = palette != null ? palette.AccentColor : new Color(0.14f, 0.46f, 0.93f);
            Color danger = palette != null ? palette.DangerColor : new Color(0.97f, 0.34f, 0.34f);
            Color caution = palette != null ? palette.CautionColor : new Color(1f, 0.82f, 0.34f);

            // Stereo reads as red left / blue right; a mono device keeps the theme accent so the
            // colouring only ever means "these are two separate channels".
            material.SetColor(BackgroundProperty, background);
            material.SetColor(LeftProperty, channels > 1 ? new Color(0.93f, 0.28f, 0.33f) : accent);
            material.SetColor(RightProperty, new Color(0.29f, 0.55f, 0.98f));
            material.SetColor(HotProperty, danger);
            material.SetColor(CentreLineProperty, Color.Lerp(background, guide, 0.35f));
            material.SetColor(GateLineProperty, Color.Lerp(background, caution, 0.45f));
            material.SetColor(MutedColourProperty, Color.Lerp(background, guide, 0.55f));

            float centre = TextureHeight * 0.5f;
            float half = centre - VerticalPadding;

            material.SetFloat(ScaleProperty, half / centre);
            material.SetFloat(CentreHalfProperty, 1.5f / TextureHeight);
            material.SetFloat(LineHalfProperty, 2f / half);
            material.SetFloat(MinimumBarProperty, MinimumBarPixels / half);
            material.SetFloat(WarnLevelProperty, WarnAmplitude);
            material.SetFloat(GlowProperty, GlowLevels);
        }
    }
}
#endif
