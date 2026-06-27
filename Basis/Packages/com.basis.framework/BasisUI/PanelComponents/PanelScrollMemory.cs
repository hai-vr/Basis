using UnityEngine;
using UnityEngine.UI;

namespace Basis.BasisUI
{
    public class PanelScrollMemory : MonoBehaviour
    {
        private ScrollRect _scroll;
        private string _key = string.Empty;
        private int _restoreFramesLeft;

        public static PanelScrollMemory Attach(RectTransform anyChild, string key)
        {
            if (anyChild == null)
            {
                return null;
            }
            ScrollRect scroll = anyChild.GetComponentInParent<ScrollRect>();
            return scroll != null ? Attach(scroll, key) : null;
        }

        public static PanelScrollMemory Attach(ScrollRect scroll, string key)
        {
            if (scroll == null)
            {
                return null;
            }
            if (!scroll.TryGetComponent(out PanelScrollMemory memory))
            {
                memory = scroll.gameObject.AddComponent<PanelScrollMemory>();
            }
            memory._scroll = scroll;
            memory._key = key ?? string.Empty;
            return memory;
        }

        private void OnEnable()
        {
            if (!BasisMenuStateMemory.Enabled)
            {
                return;
            }
            _restoreFramesLeft = 3;
        }

        private void LateUpdate()
        {
            if (_restoreFramesLeft <= 0)
            {
                return;
            }
            _restoreFramesLeft--;

            if (!IsScrollable())
            {
                return;
            }
            _scroll.verticalNormalizedPosition = Mathf.Clamp01(BasisMenuStateMemory.GetScroll(_key, 1f));
        }

        private void OnDisable()
        {
            if (!BasisMenuStateMemory.Enabled || !IsScrollable())
            {
                return;
            }
            BasisMenuStateMemory.SetScroll(_key, Mathf.Clamp01(_scroll.verticalNormalizedPosition));
        }

        private bool IsScrollable()
        {
            if (_scroll == null || _scroll.content == null)
            {
                return false;
            }
            RectTransform viewport = _scroll.viewport != null ? _scroll.viewport : (RectTransform)_scroll.transform;
            return _scroll.content.rect.height > viewport.rect.height + 0.5f;
        }
    }
}
