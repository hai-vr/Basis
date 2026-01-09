using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Basis.BasisUI
{
    public partial class SettingsProvider
    {
        public readonly struct BasisGroupBuilder
        {
            public readonly PanelElementDescriptor Group;

            public RectTransform Parent => Group.ContentParent;

            public BasisGroupBuilder(RectTransform parent, string title, string description = null, string icon = null)
            {
                Group = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, parent);
                if (!string.IsNullOrEmpty(icon)) Group.SetIcon(icon);
                Group.SetTitle(title);
                if (!string.IsNullOrEmpty(description)) Group.SetDescription(description);
            }

            public PanelToggle Toggle(string title, BasisSettingsBinding<bool> binding)
            {
                var t = PanelToggle.CreateNewEntry(Parent);
                t.Descriptor.SetTitle(title);
                t.AssignBinding(binding);
                return t;
            }

            public PanelSlider Slider(PanelSlider.SliderSettings settings, BasisSettingsBinding<float> binding)
                => PanelSlider.CreateEntryAndBind(Parent, settings, binding);

            public PanelDropdown Dropdown(string title, IList<string> entries, BasisSettingsBinding<string> binding = null)
            {
                var d = PanelDropdown.CreateNewEntry(Parent);
                d.Descriptor.SetTitle(title);
                d.AssignEntries(entries.ToList());
                if (binding != null) d.AssignBinding(binding);
                return d;
            }

            public PanelDropdown DropdownInt(string title, IList<string> entries, BasisSettingsBinding<string> binding)
            {
                var d = PanelDropdown.CreateNewEntry(Parent);
                d.Descriptor.SetTitle(title);
                d.AssignEntries(entries.ToList());
                d.AssignBinding(binding);
                return d;
            }
        }
    }
}
