using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.BasisUI
{
    public class PanelToggle : PanelDataComponent<bool>
    {
        public static class Styles
        {
            public static string Default => "Packages/com.basis.framework/BasisUI/Prefabs/Panel Elements/PE Toggle.prefab";
            public static string Entry => "Packages/com.basis.framework/BasisUI/Prefabs/Panel Elements/PE Toggle - Entry Variant.prefab";
        }

        public Toggle ToggleComponent;
        public RectTransform ToggleVisual;

        [Header("Visual Elements")]
        public Graphic Background;                     // <= ADD THIS (Image or any UI Graphic)

        [Min(0)] public float ToggleVisualOffset = 20f;

        [Header("Tween Settings")]
        [Min(0)] public float TweenDuration = 0.2f;
        public AnimationCurve EaseCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Color Settings")]                     // <= NICE INSPECTOR BLOCK
        public Color OffColor = new Color(0.4f, 0.4f, 0.4f, 1f);
        public Color OnColor = new Color(0.2f, 0.8f, 0.4f, 1f);

        private Coroutine _tweenRoutine;
        private bool _initialized;

        private PanelToggle() { }

        public static PanelToggle CreateNew(Component parent) =>
            CreateNew<PanelToggle>(Styles.Default, parent);

        public static PanelToggle CreateNewEntry(Component parent) =>
            CreateNew<PanelToggle>(Styles.Entry, parent);

        public static PanelToggle CreateNew(Component parent, string style) =>
            CreateNew<PanelToggle>(style, parent);

        public override void AssignBinding(BasisSettingsBinding<bool> binding)
        {
            base.AssignBinding(binding);
            ToggleComponent.SetIsOnWithoutNotify(binding.RawValue);

            SetVisualInstant(binding.RawValue); // Instant slide + color
        }

        public override void OnComponentUsed()
        {
            base.OnComponentUsed();
            _initialized = true;
            SetValue(ToggleComponent.isOn);
        }

        protected override void ApplyValue()
        {
            base.ApplyValue();

            float targetX = Value ? ToggleVisualOffset : -ToggleVisualOffset;

            if (!Application.isPlaying || !_initialized || TweenDuration <= 0f)
            {
                SetVisualInstant(Value);
                return;
            }

            if (_tweenRoutine != null)
                StopCoroutine(_tweenRoutine);

            _tweenRoutine = StartCoroutine(TweenToX(targetX, Value));
        }

        private void SetVisualInstant(bool on)
        {
            if (ToggleVisual != null)
            {
                float x = on ? ToggleVisualOffset : -ToggleVisualOffset;
                var pos = ToggleVisual.anchoredPosition;
                ToggleVisual.anchoredPosition = new Vector2(x, pos.y);
            }

            if (Background != null)
            {
                Background.color = on ? OnColor : OffColor;
            }
        }

        private IEnumerator TweenToX(float targetX, bool turningOn)
        {
            if (ToggleVisual == null && Background == null)
                yield break;

            Vector2 startPos = ToggleVisual ? ToggleVisual.anchoredPosition : Vector2.zero;
            float startX = startPos.x;
            float y = startPos.y;

            Color startColor = Background ? Background.color : Color.white;
            Color targetColor = turningOn ? OnColor : OffColor;

            float time = 0f;

            while (time < TweenDuration)
            {
                time += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(time / TweenDuration);

                float eased = EaseCurve != null ? EaseCurve.Evaluate(t) : t;

                if (ToggleVisual != null)
                {
                    float x = Mathf.Lerp(startX, targetX, eased);
                    ToggleVisual.anchoredPosition = new Vector2(x, y);
                }

                if (Background != null)
                {
                    Background.color = Color.Lerp(startColor, targetColor, eased);
                }

                yield return null;
            }

            if (ToggleVisual != null)
                ToggleVisual.anchoredPosition = new Vector2(targetX, y);

            if (Background != null)
                Background.color = targetColor;

            _tweenRoutine = null;
        }
    }
}
