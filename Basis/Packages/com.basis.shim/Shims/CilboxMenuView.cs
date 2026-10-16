using System.Collections.Generic;
using System.Globalization;
using Basis.BasisUI;
using UnityEngine;

namespace Cilbox
{
	internal sealed class CilboxMenuProvider : BasisMenuActionProvider<BasisMainMenu>, IBasisMenuSharePlace
	{
		internal const int MenuOrder = 95;
		internal const string NoSharePlace = ">";
		internal readonly CilboxMenuPanel Panel;
		internal CilboxMenuProvider(CilboxMenuPanel panel) => Panel = panel;
		public override bool Hidden => !CilboxMenus.AnyVisible(Panel);
		public override string Title => Panel.Title;
		public override string IconAddress => CilboxMenuView.Icon(Panel.Pages.Count > 0 ? Panel.Pages[0].Kind : CilboxMenus.KindWorld);
		public override int Order => MenuOrder;
		public override void RunAction() => CilboxMenuView.Open(Panel);
		public override void OnButtonCreated(PanelButton button) => button.Descriptor.DisableRichText();
		public override void OnReleaseEvent() => CilboxMenuView.Closed(Panel);
		public string DescribeSharePlace(PanelComponent control, out string label)
		{
			label = null;
			return NoSharePlace;
		}
		public bool OpenSharePlace(string place) => false;
		public RectTransform SharePlaceRoot(string place) => null;
	}

	internal static class CilboxMenuView
	{
		internal const string SourceKey = "menu.content.source.";

		internal static string Icon(int kind) => kind == CilboxMenus.KindAvatar ? AddressableAssets.Sprites.Avatars : kind == CilboxMenus.KindProp ? AddressableAssets.Sprites.Items : AddressableAssets.Sprites.World;

		internal static string SourceLine(CilboxMenuPage page) => BasisLocalization.Get(SourceKey + (page.Kind == CilboxMenus.KindAvatar ? "avatar" : page.Kind == CilboxMenus.KindProp ? "prop" : "world"), page.Source);

		internal static PanelTabPage BuildTab(CilboxMenuPage page, PanelTabGroup group)
		{
			PanelTabPage tab = PanelTabPage.CreateVertical(group.Descriptor.ContentParent);
			if (tab != null) Fill(page, tab);
			return tab;
		}

		internal static void Open(CilboxMenuPanel panel)
		{
			if (BasisMainMenu.ActiveMenuTitle == panel.Title)
			{
				BasisMainMenu.CloseActivePanel();
				return;
			}
			if (!BasisMainMenu.Instance || !CilboxMenus.AnyVisible(panel)) return;
			BasisMenuPanel menu = BasisMainMenu.CreateActiveMenu(BasisMenuPanel.PanelData.Standard(panel.Title), BasisMenuPanel.PanelStyles.Page, panel.Provider);
			if (menu == null) return;
			menu.Descriptor.DisableRichText();
			panel.Provider.BoundButton?.BindActiveStateToAddressablesInstance(menu);
			panel.Group = PanelTabGroup.CreateNew(menu.Descriptor.ContentParent, LayoutDirection.Vertical);
			if (panel.Group == null) return;
			for (int i = 0; i < panel.Pages.Count; i++) AddTab(panel, panel.Pages[i]);
			CilboxMenuPage first = panel.Pending != null && panel.Pending.Panel == panel && panel.Pending.Visible && !panel.Pending.Removed ? panel.Pending : FirstVisible(panel, null);
			panel.Pending = null;
			if (first != null) Select(first);
			menu.Descriptor.ForceRebuild();
		}

		internal static void Closed(CilboxMenuPanel panel)
		{
			panel.Group = null;
			for (int i = 0; i < panel.Pages.Count; i++) Forget(panel.Pages[i]);
		}

		internal static void Show(CilboxMenuPage page)
		{
			CilboxMenuPanel panel = page.Panel;
			if (Live(panel))
			{
				Select(page);
				return;
			}
			panel.Pending = page;
			BasisMainMenu.OpenWithProvider(panel.Provider);
		}

		internal static bool IsShown(CilboxMenuPage page) => Live(page.Panel) && page.Tab >= 0 && page.Panel.Group.Value == page.Tab && page.Page != null;

		internal static void TabAdded(CilboxMenuPage page, bool shown)
		{
			if (Live(page.Panel)) AddTab(page.Panel, page);
			if (!shown) RefreshHotbar();
		}

		internal static void PanelRemoved(CilboxMenuPanel panel)
		{
			if (Live(panel)) BasisMainMenu.CloseActivePanel();
			panel.Group = null;
			panel.Pending = null;
		}

		internal static void Retitle(CilboxMenuPage page)
		{
			if (page.Page != null) page.Page.Descriptor.SetTitle(page.Title);
			if (page.Panel == null || !Live(page.Panel)) return;
			PanelButton button = TabButton(page);
			if (button != null) button.Descriptor.SetTitle(page.Title);
		}

		internal static void Rebuild(CilboxMenuPage page, double now)
		{
			page.Dirty = false;
			page.NextBuild = now + CilboxMenus.RebuildSeconds;
			PanelTabPage tab = page.Page;
			if (tab == null || tab.IsReleased)
			{
				Forget(page);
				return;
			}
			for (int i = 0; i < page.Built.Count; i++)
			{
				if (page.Built[i] != null) page.Built[i].ReleaseInstance();
			}
			Forget(page);
			page.Page = tab;
			Build(page);
		}

		internal static void Forget(CilboxMenuPage page)
		{
			page.Page = null;
			page.Built.Clear();
			for (int i = 0; i < page.Items.Count; i++)
			{
				page.Items[i].Element = null;
				page.Items[i].Control = null;
			}
		}

		internal static void ShowText(CilboxMenuItem item)
		{
			if (item.Element == null) return;
			if (item.Type == CilboxMenus.Label) item.Element.SetDescription(item.Text);
			else item.Element.SetTitle(item.Text);
			Relayout(item.Page);
		}

		internal static void ShowTooltip(CilboxMenuItem item)
		{
			if (item.Element != null) item.Element.SetTooltip(item.Tooltip);
		}

		internal static void ShowValue(CilboxMenuItem item)
		{
			if (item.Control is PanelToggle toggle && toggle != null) toggle.SetValueWithoutNotify(item.Value != 0f);
			else if (item.Control is PanelSlider slider && slider != null) slider.SetValueWithoutNotify(item.Value);
			else if (item.Control is PanelDropdown dropdown && dropdown != null) dropdown.SetValueWithoutNotify(Id((int)item.Value));
		}

		internal static void ShowOptions(CilboxMenuItem item)
		{
			if (item.Control is PanelDropdown dropdown && dropdown != null) Entries(dropdown, item);
		}

		internal static void ShowVisible(CilboxMenuItem item)
		{
			if (item.Element == null) return;
			item.Element.gameObject.SetActive(item.Visible);
			Relayout(item.Page);
		}

		internal static void ShowInteractable(CilboxMenuItem item)
		{
			if (item.Control != null) item.Control.SetInteractable(item.Interactable);
		}

		static void Fill(CilboxMenuPage page, PanelTabPage tab)
		{
			Forget(page);
			page.Page = tab;
			page.Dirty = false;
			PanelElementDescriptor descriptor = tab.Descriptor;
			descriptor.DisableRichText();
			descriptor.SetIcon(Icon(page.Kind));
			descriptor.SetTitle(page.Title);
			Build(page);
		}

		static void Build(CilboxMenuPage page)
		{
			RectTransform content = page.Page.Descriptor.ContentParent;
			PanelElementDescriptor source = Card(page, content, string.Empty);
			if (source != null)
			{
				source.SetBackgroundVisible(false);
				source.SetDescription(SourceLine(page));
			}
			PanelElementDescriptor card = null;
			for (int i = 0; i < page.Items.Count; i++)
			{
				CilboxMenuItem item = page.Items[i];
				if (item.Type == CilboxMenus.Header)
				{
					card = Card(page, content, item.Text);
					if (card == null) continue;
					item.Element = card;
					card.SetTooltip(item.Tooltip);
					card.gameObject.SetActive(item.Visible);
					continue;
				}
				if (card == null) card = Card(page, content, string.Empty);
				if (card != null) BuildItem(item, card.ContentParent);
			}
			page.Page.Descriptor.ForceRebuild();
		}

		static PanelElementDescriptor Card(CilboxMenuPage page, RectTransform parent, string title)
		{
			PanelElementDescriptor card = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, parent);
			if (card == null) return null;
			card.DisableRichText();
			card.SetTitle(title);
			card.SetDescription(string.Empty);
			page.Built.Add(card);
			return card;
		}

		static void BuildItem(CilboxMenuItem item, RectTransform parent)
		{
			switch (item.Type)
			{
				case CilboxMenus.Label:
				{
					PanelElementDescriptor row = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, parent);
					if (row == null) return;
					row.DisableRichText();
					row.SetBackgroundVisible(false);
					row.SetTitle(string.Empty);
					row.SetDescription(item.Text);
					ReleaseControlSlot(row);
					item.Element = row;
					break;
				}
				case CilboxMenus.Button:
				{
					PanelButton button = PanelButton.CreateNew(parent);
					if (button == null) return;
					button.Descriptor.SetTitle(item.Text);
					button.OnClicked += () => CilboxMenus.Clicked(item);
					item.Control = button;
					break;
				}
				case CilboxMenus.Toggle:
				{
					PanelToggle toggle = PanelToggle.CreateNewEntry(parent);
					if (toggle == null) return;
					toggle.Descriptor.SetTitle(item.Text);
					toggle.SetValueWithoutNotify(item.Value != 0f);
					toggle.SetResetDefault(item.Initial != 0f);
					toggle.OnValueChanged += on => CilboxMenus.Changed(item, on ? 1f : 0f);
					item.Control = toggle;
					break;
				}
				case CilboxMenus.Slider:
				{
					PanelSlider slider = PanelSlider.CreateNew(PanelSlider.SliderStyles.Entry, parent);
					if (slider == null) return;
					slider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(item.Text, item.Min, item.Max, item.Whole, item.Whole ? 0 : 2, ValueDisplayMode.Raw));
					slider.SetValueWithoutNotify(item.Value);
					slider.SetResetDefault(item.Initial);
					slider.OnValueChanged += value => CilboxMenus.Changed(item, value);
					item.Control = slider;
					break;
				}
				case CilboxMenus.Dropdown:
				{
					PanelDropdown dropdown = PanelDropdown.CreateNewEntry(parent);
					if (dropdown == null) return;
					dropdown.Descriptor.SetTitle(item.Text);
					Entries(dropdown, item);
					dropdown.SetResetDefault(Id((int)item.Initial));
					dropdown.OnValueChanged += id => CilboxMenus.Changed(item, Parse(id));
					item.Control = dropdown;
					break;
				}
				default:
					return;
			}
			if (item.Control != null)
			{
				item.Element = item.Control.Descriptor;
				item.Control.SetInteractable(item.Interactable);
			}
			if (item.Element == null) return;
			item.Element.DisableRichText();
			item.Element.SetTooltip(item.Tooltip);
			item.Element.gameObject.SetActive(item.Visible);
		}

		static void Entries(PanelDropdown dropdown, CilboxMenuItem item)
		{
			List<string> ids = new List<string>(item.Options.Length);
			List<string> labels = new List<string>(item.Options.Length);
			for (int i = 0; i < item.Options.Length; i++)
			{
				ids.Add(Id(i));
				labels.Add(item.Options[i]);
			}
			dropdown.AssignEntries(ids, labels);
			dropdown.SetValueWithoutNotify(Id((int)item.Value));
		}

		static void AddTab(CilboxMenuPanel panel, CilboxMenuPage page)
		{
			PanelTabGroup group = panel.Group;
			PanelTabPage tab = PanelTabPage.CreateVertical(group.Descriptor.ContentParent);
			if (tab == null) return;
			page.Tab = group.Pages.Count;
			group.AddTab(page.Title, () => Selected(page, tab), tab);
			PanelButton button = TabButton(page);
			if (button == null) return;
			button.Descriptor.DisableRichText();
			button.gameObject.SetActive(page.Visible);
		}

		static void Selected(CilboxMenuPage page, PanelTabPage tab)
		{
			if (!page.Removed && tab != null && page.Page != tab) Fill(page, tab);
		}

		internal static void Present(CilboxMenuPage page, bool shown)
		{
			CilboxMenuPanel panel = page.Panel;
			bool visible = CilboxMenus.AnyVisible(panel);
			if (Live(panel))
			{
				if (!visible) BasisMainMenu.CloseActivePanel();
				else
				{
					PanelButton button = TabButton(page);
					if (button != null) button.gameObject.SetActive(page.Visible && !page.Removed);
					CilboxMenuPage next = FirstVisible(panel, null);
					if ((page.Removed || !page.Visible) && panel.Group.Value == page.Tab && next != null) Select(next);
				}
			}
			if (visible != shown) RefreshHotbar();
		}

		static bool Live(CilboxMenuPanel panel) => panel != null && panel.Group != null && BasisMainMenu.ActivePanelProvider == panel.Provider;

		static PanelButton TabButton(CilboxMenuPage page)
		{
			PanelTabGroup group = page.Panel != null ? page.Panel.Group : null;
			if (group == null || page.Tab < 0 || page.Tab >= group.SelectionButtons.Count) return null;
			PanelButton button = group.SelectionButtons[page.Tab];
			return button != null ? button : null;
		}

		static void Select(CilboxMenuPage page)
		{
			PanelButton button = TabButton(page);
			if (button != null && button.gameObject.activeSelf) button.OnClicked?.Invoke();
		}

		static CilboxMenuPage FirstVisible(CilboxMenuPanel panel, CilboxMenuPage except)
		{
			for (int i = 0; i < panel.Pages.Count; i++)
			{
				CilboxMenuPage page = panel.Pages[i];
				if (page != except && page.Visible && !page.Removed) return page;
			}
			return null;
		}

		static void RefreshHotbar()
		{
			if (BasisMenuBase<BasisMainMenu>.Instance) BasisMenuBase<BasisMainMenu>.Instance.BindProvidersToButtons();
		}

		static void Relayout(CilboxMenuPage page)
		{
			if (page.Page != null) page.Page.Descriptor.ForceRebuild();
		}

		static void ReleaseControlSlot(PanelElementDescriptor row)
		{
			if (row.Header == null) return;
			Transform slot = row.Header.Find("Title/Element");
			if (slot != null) slot.gameObject.SetActive(false);
		}

		static string Id(int index) => index.ToString(CultureInfo.InvariantCulture);

		static float Parse(string id) => int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ? index : 0f;
	}
}
