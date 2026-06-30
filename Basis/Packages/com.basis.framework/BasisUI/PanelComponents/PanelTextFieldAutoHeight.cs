using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.BasisUI
{
    /// <summary>
    /// Grows a <see cref="PanelTextField"/>'s input box to fit its text instead of clipping or
    /// scrolling it. Drives the input's <see cref="LayoutElement"/> height from the measured text;
    /// the element's existing ContentSizeFitter/LayoutGroup chain reflows the row from there.
    /// Opt-in per field — most panel fields are intentionally fixed-height.
    /// </summary>
    public sealed class PanelTextFieldAutoHeight : MonoBehaviour
    {
        private TMP_InputField _input;
        private TMP_Text _text;
        private LayoutElement _fieldLayout;
        private RectTransform _fieldRect;
        private float _minHeight;
        private float _padding = -1f;
        private bool _dirty;

        public void Initialize(TMP_InputField input)
        {
            _input = input;
            _text = input.textComponent;
            _fieldRect = (RectTransform)input.transform;
            if (!input.TryGetComponent(out _fieldLayout))
            {
                _fieldLayout = input.gameObject.AddComponent<LayoutElement>();
            }
            _minHeight = _fieldLayout.minHeight > 0f ? _fieldLayout.minHeight : _fieldRect.rect.height;

            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.overflowMode = TextOverflowModes.Overflow;

            _input.onValueChanged.AddListener(OnTextChanged);
            _dirty = true;
        }

        private void OnEnable() => _dirty = true;

        private void OnTextChanged(string value) => _dirty = true;

        public void Refresh() => _dirty = true;

        private void LateUpdate()
        {
            if (!_dirty || _text == null || _fieldLayout == null)
            {
                return;
            }

            float width = _text.rectTransform.rect.width;
            if (width <= 0f)
            {
                return;
            }
            _dirty = false;

            if (_padding < 0f)
            {
                _padding = Mathf.Max(0f, _minHeight - _text.GetPreferredValues("Ay", width, 0f).y);
            }

            string content = string.IsNullOrEmpty(_input.text) ? "Ay" : _input.text;
            float target = Mathf.Max(_minHeight, _text.GetPreferredValues(content, width, 0f).y + _padding);
            if (Mathf.Approximately(_fieldLayout.preferredHeight, target))
            {
                return;
            }

            _fieldLayout.minHeight = target;
            _fieldLayout.preferredHeight = target;
            _fieldRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, target);

            // Rebuild the parent layout group synchronously: it recomputes this row's grown/shrunk
            // height and repositions the rows below it in the same pass. Marking only this field
            // resizes it but leaves the siblings stacked off the stale height.
            RectTransform reflowTarget = transform.parent as RectTransform;
            if (reflowTarget == null)
            {
                reflowTarget = (RectTransform)transform;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(reflowTarget);
        }

        private void OnDestroy()
        {
            if (_input != null)
            {
                _input.onValueChanged.RemoveListener(OnTextChanged);
            }
        }
    }
}
