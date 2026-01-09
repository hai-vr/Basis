using UnityEngine;

namespace Basis.BasisUI
{
    public partial class SettingsProvider
    {
        public readonly struct BasisTabBuilder
        {
            public readonly PanelTabPage Tab;
            public readonly PanelElementDescriptor Descriptor;

            public RectTransform Content => Descriptor.ContentParent;

            public BasisTabBuilder(PanelTabGroup tabGroup, string title, string icon = null)
            {
                Tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
                Descriptor = Tab.Descriptor;
                if (!string.IsNullOrEmpty(icon))
                {
                    Descriptor.SetIcon(icon);
                }

                Descriptor.SetTitle(title);
            }

            public BasisGroupBuilder Group(string title, string description = null, string icon = null) => new BasisGroupBuilder(Content, title, description, icon);
        }
    }
}
