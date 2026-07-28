using UnityEngine;

namespace Basis.BasisUI
{
    /// <summary>
    /// Single entry point for every "copy this to the clipboard" action in the
    /// runtime UI. Copying is invisible on its own — the clipboard is off-screen and
    /// in VR there is no OS-level indication anything happened — so the control that
    /// was used briefly reports back that it copied.
    /// </summary>
    public static class BasisClipboard
    {
        public const float ConfirmSeconds = 1.5f;

        public static void Copy(string value)
        {
            GUIUtility.systemCopyBuffer = value ?? string.Empty;
        }

        public static void Copy(string value, PanelElementDescriptor confirmOn)
        {
            Copy(value);
            Confirm(confirmOn);
        }

        public static void Copy(string value, PanelComponent confirmOn)
        {
            Copy(value);
            Confirm(confirmOn ? confirmOn.Descriptor : null);
        }

        public static void Confirm(PanelElementDescriptor descriptor)
        {
            if (!descriptor) return;
            descriptor.FlashTitle(BasisLocalization.Get("ui.copied"), ConfirmSeconds);
        }
    }
}
