using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Basis.BasisUI
{
    [RequireComponent(typeof(PanelElementDescriptor))]
    public abstract class PanelComponent : AddressableUIInstanceBase, IPointerEnterHandler, IPointerExitHandler
    {

        public PanelElementDescriptor Descriptor
        {
            get
            {
                if (!_descriptor) _descriptor = GetComponent<PanelElementDescriptor>();
                return _descriptor;
            }
        }

        private PanelElementDescriptor _descriptor;

        private string _disabledReason;
        private bool _pointerInside;

        // The panel the pointer is currently over. Set from the same enter/exit events that
        // drive the tooltip, so it is correct even for panels nested inside a scroll view.
        public static PanelComponent CurrentHovered { get; private set; }

        /// <summary>
        /// The Selectable that owns this control's interactable state (dropdown, toggle, slider,
        /// button, input field). Null for elements that have no interactive control.
        /// </summary>
        protected virtual Selectable InteractableTarget => null;

        /// <summary>True when this control has no Selectable, or its Selectable is interactable.</summary>
        public bool IsInteractable
        {
            get
            {
                Selectable target = InteractableTarget;
                return target == null || target.interactable;
            }
        }

        /// <summary>
        /// Enables or disables this control. When disabling, pass a short reason describing why —
        /// it is shown in the hover tooltip so a greyed-out control explains itself instead of
        /// looking broken. The reason is cleared automatically when the control is re-enabled.
        /// </summary>
        public virtual void SetInteractable(bool interactable, string disabledReason = null)
        {
            _disabledReason = interactable ? null : disabledReason;

            Selectable target = InteractableTarget;
            if (target != null) target.interactable = interactable;

            if (_pointerInside) BasisMainMenu.ShowTooltip(TooltipText);
        }

        /// <summary>
        /// Text shown in the hover tooltip bar. While disabled with a reason, that reason is
        /// shown so a greyed-out control explains itself. Otherwise prefers the element's
        /// description, falling back to its title — controls like toggles/sliders/dropdowns
        /// only set a title. Override to surface something more specific.
        /// </summary>
        protected virtual string TooltipText
        {
            get
            {
                if (!IsInteractable && !string.IsNullOrEmpty(_disabledReason)) return _disabledReason;
                if (!Descriptor) return null;
                if (!string.IsNullOrEmpty(Descriptor.Tooltip)) return Descriptor.Tooltip;
                return string.IsNullOrEmpty(Descriptor.Description) ? Descriptor.Title : Descriptor.Description;
            }
        }

        public virtual void OnPointerEnter(PointerEventData eventData)
        {
            _pointerInside = true;
            CurrentHovered = this;
            BasisMainMenu.ShowTooltip(TooltipText);
        }

        public virtual void OnPointerExit(PointerEventData eventData)
        {
            _pointerInside = false;
            if (CurrentHovered == this) CurrentHovered = null;
            BasisMainMenu.HideTooltip();
        }

        [UsedImplicitly]
        public virtual void OnComponentUsed()
        {
        }
    }
}
