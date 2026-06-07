using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.EventSystems;

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

        /// <summary>
        /// Text shown in the hover tooltip bar. Prefers the element's description, but falls
        /// back to its title — controls like toggles/sliders/dropdowns only set a title.
        /// Override to surface something more specific.
        /// </summary>
        protected virtual string TooltipText
        {
            get
            {
                if (!Descriptor) return null;
                if (!string.IsNullOrEmpty(Descriptor.Tooltip)) return Descriptor.Tooltip;
                return string.IsNullOrEmpty(Descriptor.Description) ? Descriptor.Title : Descriptor.Description;
            }
        }

        public virtual void OnPointerEnter(PointerEventData eventData)
        {
            BasisMainMenu.ShowTooltip(TooltipText);
        }

        public virtual void OnPointerExit(PointerEventData eventData)
        {
            BasisMainMenu.HideTooltip();
        }

        [UsedImplicitly]
        public virtual void OnComponentUsed()
        {
        }
    }
}
