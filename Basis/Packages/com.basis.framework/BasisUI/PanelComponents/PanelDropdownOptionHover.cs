using UnityEngine;
using UnityEngine.EventSystems;

namespace Basis.BasisUI
{
    /// <summary>
    /// Reports which option of an open <see cref="PanelDropdown"/> the pointer is on, so the
    /// tooltip bar can describe the individual choice rather than the dropdown as a whole.
    /// Attached to TMP_Dropdown's item template by the dropdown itself; each option the list
    /// spawns is a clone and carries one.
    /// </summary>
    public class PanelDropdownOptionHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private PanelDropdown _owner;
        private bool _ownerResolved;
        private int _reportedIndex = -1;

        public void OnPointerEnter(PointerEventData eventData)
        {
            PanelDropdown owner = ResolveOwner();
            if (owner == null) return;

            _reportedIndex = ResolveOptionIndex();
            if (_reportedIndex >= 0) owner.SetHoveredOption(_reportedIndex);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Release();
        }

        // The list is destroyed outright when the dropdown closes, which is not a pointer exit.
        private void OnDisable()
        {
            Release();
        }

        private void Release()
        {
            if (_reportedIndex < 0) return;

            int index = _reportedIndex;
            _reportedIndex = -1;
            if (_owner != null) _owner.ClearHoveredOption(index);
        }

        private PanelDropdown ResolveOwner()
        {
            if (_ownerResolved) return _owner;

            _ownerResolved = true;
            _owner = GetComponentInParent<PanelDropdown>(true);
            return _owner;
        }

        /// <summary>
        /// Option index by position among the live items. The item template shares the same parent
        /// but is left inactive once the list is built, so skipping inactive siblings keeps this
        /// aligned with the dropdown's own option order.
        /// </summary>
        private int ResolveOptionIndex()
        {
            Transform parent = transform.parent;
            if (parent == null) return -1;

            int index = 0;
            int count = parent.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform child = parent.GetChild(i);
                if (child == transform) return index;
                if (child.gameObject.activeSelf) index++;
            }

            return -1;
        }
    }
}
