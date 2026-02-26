using Basis.BasisUI.Styling;
using Basis.Scripts.UI.UI_Panels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using Unity.Android.Gradle;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using static Basis.BasisUI.PanelButton;
using static Basis.BasisUI.PanelPasswordField;
using static Basis.BasisUI.PanelTextField;

namespace Basis.BasisUI
{
    public partial class LibraryProvider : BasisMenuActionProvider<BasisMainMenu>
    {

        // TODO: stuff up here needs to be refactored or re-worked

        public static string TitleToCase(string givenText)
        {
            TextInfo textInfo = CultureInfo.CurrentCulture.TextInfo;
            return textInfo.ToTitleCase(givenText);
        }

        public static Page ModeToPage(BundledContentHolder.Mode mode)
        {
            return mode switch
            {
                BundledContentHolder.Mode.Prop => Page.Prop,
                BundledContentHolder.Mode.World => Page.World,
                BundledContentHolder.Mode.Avatar => Page.Avatar,
                _ => throw new System.ArgumentException($"Cannot map mode {mode} to a Page")
            };
        }

        public static bool IsProp(BasisDataStoreItemKeys.ItemKey item)
        {
            return item.Mode == BundledContentHolder.Mode.Prop;
        }

        public static void RefreshPinnedProviders()
        {
            //await BasisDataStoreItemKeys.LoadKeys();

            var keys = BasisDataStoreItemKeys.DisplayKeys();

            var existing = new List<BasisMenuActionProvider<BasisMainMenu>>(
                BasisMenuBase<BasisMainMenu>.Providers);

            // Remove old pinned providers
            foreach (var provider in existing)
            {
                if (provider is PinnedItemProvider)
                {
                    BasisMenuBase<BasisMainMenu>.RemoveProvider(provider);
                }
            }

            // Add new ones
            foreach (var key in keys)
            {
                if (key.PinnedSettings.IsPinned)
                {
                    // Try get cached meta once
                    CachedMetaData.CachedContent cachedMeta;
                    if (CachedMetaData.TryGetMeta(key.Url, out cachedMeta))
                    {
                        var provider = new PinnedItemProvider(key, cachedMeta);
                        BasisMenuBase<BasisMainMenu>.AddProvider(provider);
                    }
                    else
                    {
                        BasisDebug.LogError($" Unable to build pinned provider for item = {key.Url} failed to get item from cache");
                    }
                }
            }
        }

        // TODO END

        #region Provider Setup
        [RuntimeInitializeOnLoadMethod]
        public static async void AddToMenu()
        {
            BasisMenuBase<BasisMainMenu>.AddProvider(new LibraryProvider());

            // begin meta data caching here
            // load all the keys
            await BasisDataStoreItemKeys.LoadKeys();

            // cache all items into the meta data
            // build data to be used
            var data = BasisDataStoreItemKeys.DisplayKeys()
                .ToList();

            // Preload metadata for all items
            try
            {
                await CachedMetaData.PreloadMetaForItems(data);
            }
            catch (Exception ex)
            {
                BasisDebug.LogError(ex);
            }

            // once we have the cache now invoke the task to build pinned providers
            RefreshPinnedProviders();
        }

        public override string Title => "Library";
        public override string IconAddress => AddressableAssets.Sprites.Library;
        public override int Order => 1; // after Settings
        public override bool Hidden => false;
        public static BasisMenuPanel panel;
        public static PanelTextField searchField; // reference to the search field
        private static LibraryDateSortMode _currentSort = LibraryDateSortMode.Name; // current sort mode for the library, default to name sorting
        // private static LibraryNetworkFilter _currentNetworkFilter = LibraryNetworkFilter.All;
        private static string _currentSearchQuery = string.Empty;

        public enum Page
        {
            Prop = 0,
            World = 1,
            Avatar = 2,
            Instantiated = 3
        }
        private static Page _currentPage = Page.Avatar;
        private static Dictionary<Page, PanelTabPage> tabMap;
        private static PanelTabPage _currentTab;

        private static PanelTabGroup tabGroup;

        public override async void RunAction()
        {
            if (BasisMainMenu.ActiveMenuTitle == Title) return;

            // this creates our panel
            panel = BasisMainMenu.CreateActiveMenu(
                BasisMenuPanel.PanelData.Standard(Title),
                BasisMenuPanel.PanelStyles.Page);

            // No tab cache to reset; tabs will be rebuilt on selection

            // this sets the title of our panel
            var titleLabel = panel.Descriptor.TitleLabel;
            titleLabel.text = Title;

            BoundButton?.BindActiveStateToAddressablesInstance(panel);

            // create a tab group to hold our content categories
            tabGroup = PanelTabGroup.CreateNew(panel.Descriptor.ContentParent, LayoutDirection.Horizontal);

            // create our main tabs without preloading items; items will be loaded lazily on tab selection
            var propsTab = PropsTab(tabGroup);
            var worldsTab = WorldsTab(tabGroup);
            var avatarsTab = AvatarsTab(tabGroup);
            var instantiatedTab = InstantiatedTab(tabGroup);

            // map of the pages to enums
            tabMap = new Dictionary<Page, PanelTabPage>
            {
                [Page.Avatar] = avatarsTab,
                [Page.World] = worldsTab,
                [Page.Prop] = propsTab,
                [Page.Instantiated] = instantiatedTab
            };

            // Attach per-tab refresh callbacks that only fetch and rebuild the associated tab when selected
            tabGroup.AddTab("Props", AddressableAssets.Sprites.Items, async () => await RefreshTabAsync(Page.Prop), propsTab);
            tabGroup.AddTab("Worlds", AddressableAssets.Sprites.World, async () => await RefreshTabAsync(Page.World), worldsTab);
            tabGroup.AddTab("Avatars", AddressableAssets.Sprites.Avatars, async () => await RefreshTabAsync(Page.Avatar), avatarsTab);
            tabGroup.AddTab("Instantiated", AddressableAssets.Sprites.List, async () => await RefreshTabAsync(Page.Instantiated), instantiatedTab);

            // create a search text field in the tab group extras area
            searchField = PanelTextField.CreateNew(TextFieldStyles.EntryWithNoTitle, tabGroup.ExtrasContainer);
            searchField._placeholderLabel.text = "Search...";
            searchField.Descriptor.SetSize(new Vector2(60, 80));
            searchField.OnValueChanged = async (val) =>
            {
                _currentSearchQuery = val ?? string.Empty;

                // refresh the current tab for any new changes
                await RefreshCurrentTab();
            };

            // create a sorting dropdown in the tab group extras area
            var dateSorting = PanelDropdown.CreateNew(PanelDropdown.DropdownStyles.EntryNoLabel, tabGroup.ExtrasContainer);
            string[] dateSortNames = Enum.GetNames(typeof(LibraryDateSortMode));

            dateSorting.Descriptor.SetSize(new Vector2(60, 80));
            dateSorting.AssignEntries(dateSortNames.ToList());
            dateSorting.SetValueWithoutNotify(_currentSort.ToString());

            // when sorting changes, update and refresh
            dateSorting.OnValueChanged = async (val) =>
            {
                if (Enum.TryParse<LibraryDateSortMode>(val, out var parsed))
                {
                    _currentSort = parsed;

                    // refresh the current tab for any new changes
                    await RefreshCurrentTab();
                }
            };

            // TODO this will be reused for the instantiated tab

            // // create a sorting dropdown in the tab group extras area
            // var networkSorting = PanelDropdown.CreateNew(PanelDropdown.DropdownStyles.EntryNoLabel, tabGroup.ExtrasContainer);
            // string[] networkSortNames = Enum.GetNames(typeof(LibraryNetworkFilter));

            // // modify the names of the dropdown entries to be more user-friendly
            // //var displayNames = sortNames.Select(n => $"{n}").ToList();

            // //sorting.Descriptor.SetTitle("Sort");
            // networkSorting.Descriptor.SetSize(new Vector2(60, 80));
            // networkSorting.AssignEntries(networkSortNames.ToList());
            // networkSorting.SetValueWithoutNotify(_currentNetworkFilter.ToString());

            // // when sorting changes, update and refresh
            // networkSorting.OnValueChanged = async (val) =>
            // {
            //     if (Enum.TryParse<LibraryNetworkFilter>(val, out var parsed))
            //     {
            //         _currentNetworkFilter = parsed;

            //         // refresh the current tab for any new changes
            //         await RefreshCurrentTab();
            //     }
            // };


            // add our extra menu button items, this is the buttons below the panel content
            tabGroup.AddExtraAction("Add New Content", async () => await PromptUserForNewContent(), new Vector2( 70, 80 ));

            // set the current tab to the current page
            tabGroup.SetValue((int)_currentPage); // this will trigger the tab selection and associated content loading

            await RefreshCurrentTab(); // refresh the current active tab i.e what is defined by default above _currentPage

            panel.Descriptor.ForceRebuild();
        }

        #endregion

        #region BasisTrackedBundleWrapper BuildWrapper<BasisDataStoreItemKeys.ItemKey>

        [System.Serializable]
        public class BasisLoadableBundleWrapper
        {
            public BasisLoadableBundle BasisLoadableBundle;
            public BasisTrackedBundleWrapper basisTrackedBundleWrapper;
        }

        /// <summary>
        /// used to create a new BasisLoadableBundleWrapper for an item
        /// do not use for accessing data its only to init
        /// </summary>
        public static BasisLoadableBundleWrapper CreateNewWrapperFromItem(BasisDataStoreItemKeys.ItemKey item)
        {
            // create a new wrapper
            BasisLoadableBundleWrapper wrapper = new BasisLoadableBundleWrapper();

            // create a new bundle for the wrapper
            BasisLoadableBundle bundle = new()
            {
                BasisRemoteBundleEncrypted = new BasisRemoteEncyptedBundle()
                {
                    RemoteBeeFileLocation = item.Url
                },
                BasisLocalEncryptedBundle = new BasisStoredEncryptedBundle()
                {
                    DownloadedBeeFileLocation = item.Pass
                },
                UnlockPassword = item.Pass,
                BasisBundleConnector = new BasisBundleConnector()
                {
                    BasisBundleDescription = new BasisBundleDescription(),
                    BasisBundleGenerated = new BasisBundleGenerated[] { new() },
                    UniqueVersion = string.Empty,
                },
            };
            BasisTrackedBundleWrapper trackedWrapper = new()
            {
                LoadableBundle = bundle,
            };
            wrapper.BasisLoadableBundle = bundle;
            wrapper.basisTrackedBundleWrapper = trackedWrapper;

            return wrapper;
        }

        public static async Task<BasisLoadableBundleWrapper> LoadWrapperFromDisc(BasisDataStoreItemKeys.ItemKey item, BasisLoadableBundleWrapper wrapper = null)
        {
            if (wrapper == null) // generate a new wrapper if its null
            {
                BasisDebug.LogWarning("wrapper was not provided for LoadWrapperFromDisc, creating.");
                wrapper = CreateNewWrapperFromItem(item);
            }

            // If the metadata is missing on disk, remove the key and DO NOT attempt to create a bundle from it.
            if (BasisLoadHandler.IsMetaDataOnDisc(item.Url, out BasisBEEExtensionMeta info))
            {
                // CreateNewWrapperFromItem does not populate these fields so we update them
                wrapper.BasisLoadableBundle.BasisRemoteBundleEncrypted = info.StoredRemote;
                wrapper.BasisLoadableBundle.BasisLocalEncryptedBundle = info.StoredLocal;
                wrapper.BasisLoadableBundle.BasisBundleConnector.UniqueVersion = info.UniqueVersion;
                return wrapper;
            }
            else
            {
                BasisDebug.LogError($"Attempted to BuildWrapper({item.Url}) but IsMetaDataOnDisc returned false, removing item {item.Url}");
                await BasisDataStoreItemKeys.RemoveKey(item);
                return null;
            }
        }

        #endregion

        #region PropsTab, WorldsTab, AvatarsTab, InstantiatedTab, BuildItemsList, ClearTabContent, RefreshTabAsync, RefreshCurrentTab
        public static PanelTabPage PropsTab(PanelTabGroup tabGroup)
        {
            PanelTabPage tab = PanelTabPage.CreateGrid(tabGroup.Descriptor.ContentParent);
            tab.rectTransform.offsetMin = new Vector2(0, 0);
            var d = tab.Descriptor;
            d.SetTitle("Props");
            d.ForceRebuild();
            return tab;
        }

        public static PanelTabPage WorldsTab(PanelTabGroup tabGroup)
        {
            PanelTabPage tab = PanelTabPage.CreateGrid(tabGroup.Descriptor.ContentParent);
            tab.rectTransform.offsetMin = new Vector2(0, 0);
            var d = tab.Descriptor;
            d.SetTitle("Worlds");
            d.ForceRebuild();
            return tab;
        }

        public static PanelTabPage AvatarsTab(PanelTabGroup tabGroup)
        {
            PanelTabPage tab = PanelTabPage.CreateGrid(tabGroup.Descriptor.ContentParent);
            tab.rectTransform.offsetMin = new Vector2(0, 0);
            var d = tab.Descriptor;
            d.SetTitle("Avatars");
            d.ForceRebuild();
            return tab;
        }

        public static PanelTabPage InstantiatedTab(PanelTabGroup tabGroup)
        {
            PanelTabPage tab = PanelTabPage.CreateVerticalAlternate(tabGroup.Descriptor.ContentParent);
            tab.rectTransform.offsetMin = new Vector2(0, 0);
            var d = tab.Descriptor;
            d.SetTitle("Instantiated");
            d.ForceRebuild();
            return tab;
        }

        private static void BuildItemsList(List<BasisDataStoreItemKeys.ItemKey> items, PanelTabPage tab)
        {
            RectTransform container = tab.Descriptor.ContentParent;
            // List entries
            for (int Index = 0; Index < items.Count; Index++)
            {
                var item = items[Index];
                CreateItemCard(item, container);
            }
        }

        private static void BuildItemsListForInstantiatedObjects(IReadOnlyCollection<BasisRuntimeSpawnRegistry.SpawnInstance> loadedItems, PanelTabPage tab)
        {
            RectTransform container = tab.Descriptor.ContentParent;

            foreach (var entry in loadedItems)
            {
                string instanceId = entry.InstanceId;

                CreateListEntry(entry, container, instanceId);
            }
        }

        private static void ClearTabContent(RectTransform container)
        {
            if (container == null) return;
            // Destroy all child gameobjects under the content parent
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i);
                if (child != null && child.gameObject != null)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        public static bool TryConvert(Page page, out BundledContentHolder.Mode mode)
        {
            return Enum.TryParse(page.ToString(), out mode);
        }

        private static async Task RefreshTabAsync(Page page)
        {
            PanelTabPage tab = tabMap[page];
            BasisDebug.Log($"RefreshTabAsync() was invoked -> for page = {page}, tab = {tab} _currentTab = {_currentTab}, ");
            if (tab == null) return;

            // Ensure keys are loaded
            await BasisDataStoreItemKeys.LoadKeys();

            // If a different tab was previously active, clear its content when switching
            if (_currentTab != null && _currentTab != tab)
            {
                try
                {
                    ClearTabContent(_currentTab.Descriptor.ContentParent);
                    _currentTab.Descriptor.ForceRebuild();
                }
                catch (Exception ex)
                {
                    BasisDebug.LogError(ex);
                }
            }

            // remember currently active tab/mode
            _currentPage = page;
            _currentTab = tab;

            // try convert the mode and page we are on to match
            if (TryConvert(page, out BundledContentHolder.Mode mode))
            {
                try
                {

                    // // // Only fetch keys matching the requested mode, so if we only want props only grab props returned in data
                    // // var data = BasisDataStoreItemKeys.DisplayKeys()
                    // //     .Where(k => k.Mode == mode)
                    // //     .ToList();

                    // build data to be used
                    var data = BasisDataStoreItemKeys.DisplayKeys()
                        .Where(k => k.Mode == mode)
                        .ToList();

                    // Preload metadata for items in this tab so that filtering/sorting
                    // can use cached meta synchronously.
                    try
                    {
                        await CachedMetaData.PreloadMetaForItems(data);
                    }
                    catch (Exception ex)
                    {
                        BasisDebug.LogError(ex);
                    }

                    // Apply search filter if present
                    if (!string.IsNullOrWhiteSpace(_currentSearchQuery))
                    {
                        data = data.Where(k =>
                        {
                            var url = k.Url ?? string.Empty;
                            if (k.IsEmbedded)
                            {
                                if (!string.IsNullOrEmpty(url) && url.IndexOf(_currentSearchQuery, StringComparison.InvariantCultureIgnoreCase) >= 0)
                                {
                                    return true;
                                }
                            }
                            else
                            {
                                if (CachedMetaData.TryGetMeta(url, out var mm) && !string.IsNullOrEmpty(mm.Name) && mm.Name.IndexOf(_currentSearchQuery, StringComparison.InvariantCultureIgnoreCase) >= 0)
                                    return true;
                            }

                            return false;
                        }).ToList();
                    }

                    // Sorting must be synchronous and use cached metadata only.
                    switch (_currentSort)
                    {
                        case LibraryDateSortMode.Name:
                            data = data.OrderBy(k =>
                            {
                                var url = k.Url ?? string.Empty;
                                // if (k.IsEmbedded)
                                //     return k.Url;
                                if (CachedMetaData.TryGetMeta(url, out var mm) && !string.IsNullOrEmpty(mm.Name))
                                    return mm.Name;
                                return url;
                            }).ToList();
                            break;

                        case LibraryDateSortMode.DateOldestToNewest:
                            data = data.OrderBy(k =>
                            {
                                // Embedded items always treated as the oldest possible date
                                if (k.IsEmbedded)
                                    return DateTime.MinValue;

                                var url = k.Url ?? string.Empty;
                                if (CachedMetaData.TryGetMeta(url, out var mm) && mm.Created.HasValue)
                                    return mm.Created.Value;

                                return DateTime.MaxValue;
                            }).ToList();
                            break;

                        case LibraryDateSortMode.DateNewestToOldest:
                            data = data.OrderByDescending(k =>
                            {
                                // Embedded items always treated as the oldest possible date
                                if (k.IsEmbedded)
                                    return DateTime.MinValue;

                                var url = k.Url ?? string.Empty;
                                if (CachedMetaData.TryGetMeta(url, out var mm) && mm.Created.HasValue)
                                    return mm.Created.Value;

                                return DateTime.MinValue;
                            }).ToList();
                            break;
                    }

                    // Clear and rebuild the tab content
                    ClearTabContent(tab.Descriptor.ContentParent);
                    BuildItemsList(data, tab);
                    tab.Descriptor.ForceRebuild();
                }
                catch (Exception e)
                {
                    BasisDebug.LogError(e);
                }
            }
            else
            {
                // grab the data?
                IReadOnlyCollection<BasisRuntimeSpawnRegistry.SpawnInstance> collections = BasisRuntimeSpawnRegistry.GetAll();
                // this is most likely to be the instantiated tab so
                ClearTabContent(tab.Descriptor.ContentParent);
                // TODO build list of instantiated objects
                BuildItemsListForInstantiatedObjects(collections, tab);
                tab.Descriptor.ForceRebuild();
            }

        }

        // used to refresh the current tab
        private static async Task RefreshCurrentTab()
        {
            await RefreshTabAsync(_currentPage);

            // we should also refresh providers
            RefreshPinnedProviders();
        }
        #endregion

        #region PromptUserToDefineLegacyContent

        private static async Task<BundledContentHolder.Mode> PromptUserToDefineLegacyContent()
        {
            DialogBox<BundledContentHolder.Mode> legacyCotentDefineDialogBox = DialogBox<BundledContentHolder.Mode>.Create(panel, new Vector2(830, 430),
                "Specify Content Type",
                "Your content is marked legacy. Please specify what type of content you are adding.",
                AddressableAssets.Sprites.Add,
                true
            );

            // create the exit button for the dialog box
            var button = PanelButton.CreateNew(ButtonStyles.ExitButton, legacyCotentDefineDialogBox.Descriptor.Header);
            button.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 125);
            button.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 50);
            button.OnClicked += () => legacyCotentDefineDialogBox.Cancel(BundledContentHolder.Mode.Legacy);

            // information to give to the user
            PanelTextField createdInformationTextField = PanelTextField.CreateNew(TextFieldStyles.EntryVertical, legacyCotentDefineDialogBox.Descriptor);
            createdInformationTextField._inputField.gameObject.SetActive(false); // disable the text input field box
            createdInformationTextField.Descriptor.SetTitle("Why is this showing?");
            createdInformationTextField.Descriptor.SetIcon(AddressableAssets.Sprites.Information);
            createdInformationTextField.Descriptor.SetDescription($"BEE files now contain metadata of what's inside it, this is used to determine things about your BEE file which will be important in future features. Please to consider updating your content to include this metadata.");

            createdInformationTextField.Descriptor.SetHeight(100);
            createdInformationTextField.Descriptor.SetWidth(800);

            // the item type dropdown determines which library tab the new item will appear in.
            PanelDropdown contentTypeDropDown = PanelDropdown.CreateNew(PanelDropdown.DropdownStyles.OverlayEntry, legacyCotentDefineDialogBox.Descriptor);
            var modeNames = Enum.GetValues(typeof(BundledContentHolder.Mode))
                    .Cast<BundledContentHolder.Mode>()
                    .Where(m => m != BundledContentHolder.Mode.Legacy) // remove legacy from selection
                    .Select(m => m.ToString())
                    .ToArray();
            contentTypeDropDown.Descriptor.SetTitle("Content Type");
            contentTypeDropDown.Descriptor.SetIcon(AddressableAssets.Sprites.FileTray);
            contentTypeDropDown.Descriptor.SetDescription( "What content are you adding?" );
            contentTypeDropDown.AssignEntries(modeNames.ToList());
            
            // derive the default selected mode from the currently active tab, so if the user is browsing avatars and clicks "Add New CachedContent"
            contentTypeDropDown.SetValueWithoutNotify(modeNames[0]);
            contentTypeDropDown.Descriptor.SetHeight(50);
            contentTypeDropDown.Descriptor.SetWidth(800);

            // Add and Cancel buttons
            PanelTabGroup acceptOrDenyPanel = PanelTabGroup.CreateNew(legacyCotentDefineDialogBox.Descriptor, LayoutDirection.HorizontalNoBackground);

            acceptOrDenyPanel.Descriptor.SetHeight(50);
            acceptOrDenyPanel.Descriptor.SetWidth(800);

            PanelButton yesPanel = PanelButton.CreateNew(ButtonStyles.AcceptButton, acceptOrDenyPanel.TabButtonParent); //ButtonStyles.Cancel
            yesPanel.Descriptor.SetTitle("Add");
            yesPanel.Descriptor.SetWidth(800);
            yesPanel.Descriptor.SetHeight(60);

            // Add does the async work, then closes.
            yesPanel.OnClicked += () =>
            {
                if (legacyCotentDefineDialogBox.IsBusy) return;
                legacyCotentDefineDialogBox.IsBusy = true;

                var selected = contentTypeDropDown.Value;
                var mode = (BundledContentHolder.Mode)Enum.Parse(typeof(BundledContentHolder.Mode), selected);
                legacyCotentDefineDialogBox.CloseWithResult(mode);

            };

            return await legacyCotentDefineDialogBox.WaitAsync();

        }

        #endregion

        #region PromptUserForNewContent, AddNewNewItemKey, ChangeInputFieldStyle

        private static void ChangeInputFieldStyle(GameObject inputFieldObject, bool isError)
        {
            if (inputFieldObject == null) return;

            if (!inputFieldObject.TryGetComponent(out UiStyleImage styleImage))
                return;

            string newStyle = isError ? "Button Caution" : "Button Standard";

            if (styleImage.ColorStyle == newStyle)
                return;

            styleImage.SetStyle(newStyle);
        }

        // not super clean but will do for now, used to update interactable input fields
        private static void UpdateInputFieldInteractability( PanelTextField URLTextField, PanelPasswordField PasswordTextField, DialogBox<BasisDataStoreItemKeys.ItemKey> activeDialog )
        {
            URLTextField._inputField.interactable = !activeDialog.IsBusy;
            PasswordTextField._inputField.interactable = !activeDialog.IsBusy;
        }

        /// <summary>
        /// Invoked on the add new content is pressed in the library provider menu, to prompt the user to enter new content with a dialog box
        /// </summary>
        public static async Task<BasisDataStoreItemKeys.ItemKey> PromptUserForNewContent()
        {
            // Build overlay using DialogBox helper
            DialogBox<BasisDataStoreItemKeys.ItemKey> newItemDialogBox = DialogBox<BasisDataStoreItemKeys.ItemKey>.Create(panel, new Vector2(930, 600),
                "Add New Content",
                "Please provide the URL and password for your BEE file. Ensure your url and pass are correct or the item wont be included in your library.",
                AddressableAssets.Sprites.Add);

            // create the exit button for the dialog box
            var button = PanelButton.CreateNew(ButtonStyles.ExitButton, newItemDialogBox.Descriptor.Header);
            button.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 125);
            button.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 50);
            button.OnClicked += () => newItemDialogBox.Cancel(null);

            // panel group for the fields
            PanelTabGroup panelGroup = PanelTabGroup.CreateNew(PanelTabGroup.TabGroupStyles.VerticalStackedNoBackground, newItemDialogBox.Descriptor.ContentParent);
            panelGroup.Descriptor.SetHeight(400);
            panelGroup.Descriptor.SetWidth(900);

            // BEE file URL field
            PanelTextField URL = PanelTextField.CreateNew(TextFieldStyles.EntryVertical, panelGroup.TabButtonParent);
            URL._placeholderLabel.text = "URL";
            URL._inputField.contentType = TMP_InputField.ContentType.Standard;
            URL.Descriptor.SetHeight(115);
            URL.Descriptor.SetWidth(700);
            URL.Descriptor.SetTitle("BEE File URL:");
            URL.Descriptor.SetIcon(AddressableAssets.Sprites.Network);
            URL.Descriptor.SetDescription("This should be a direct link to your BEE file.");

            PanelPasswordField Password = PanelPasswordField.CreateNew(PasswordFieldStyles.EntryVertical, panelGroup.TabButtonParent);
            Password._placeholderField.text = "Enter password";
            Password.Descriptor.SetHeight(115);
            Password.Descriptor.SetWidth(700);

            Password.Descriptor.SetTitle("BEE File Password:");
            Password.Descriptor.SetIcon(AddressableAssets.Sprites.Unlocked);
            Password.Descriptor.SetDescription("This is the password that was generated with you BEE file.");

            // create a text field to show validation error messages, initially empty
            PanelTextField validationMessageField = PanelTextField.CreateNew(TextFieldStyles.EntryWarning, panelGroup.TabButtonParent);
            validationMessageField.Descriptor.gameObject.SetActive(false);
            validationMessageField._inputField.gameObject.SetActive(false); // disable the text input field box
            validationMessageField.Descriptor.SetTitle("AWAITING_INPUT");
            validationMessageField.Descriptor.SetDescription("AWAITING_INPUT");
            validationMessageField.Descriptor.TitleLabel.color = Color.yellow;
            validationMessageField.Descriptor.DescriptionLabel.color = Color.yellow;

            validationMessageField.Descriptor.SetHeight(50);
            validationMessageField.Descriptor.SetWidth(700);

            // Add and Cancel buttons
            PanelTabGroup acceptOrDenyPanel = PanelTabGroup.CreateNew(newItemDialogBox.Descriptor, LayoutDirection.HorizontalNoBackground);

            acceptOrDenyPanel.Descriptor.SetHeight(50);
            acceptOrDenyPanel.Descriptor.SetWidth(900);

            PanelButton yesPanel = PanelButton.CreateNew(ButtonStyles.AcceptButton, acceptOrDenyPanel.TabButtonParent); //ButtonStyles.Cancel
            yesPanel.Descriptor.SetTitle("Add");
            yesPanel.Descriptor.SetWidth(900);
            yesPanel.Descriptor.SetHeight(60);

            // Add does the async work, then closes.
            yesPanel.OnClicked += async () =>
            {
                if (newItemDialogBox.IsBusy) return;
                newItemDialogBox.IsBusy = true;

                // update interactability for fields based on dialog busy
                UpdateInputFieldInteractability(URL, Password, newItemDialogBox);

                try
                {

                    // perform input validation, pass our current url and password along with the existing library entries to check for duplicates
                    InputValidation.EntryValidationResponse validationResponse = InputValidation.ValidateEntry(URL.Value, Password.Password, BasisDataStoreItemKeys.DisplayKeys());

                    BasisDebug.Log($"given url {URL.Value}, given password {Password.Password}");
                    BasisDebug.Log($"processed url {validationResponse.ProcessedUrl} processed password {validationResponse.Password}");

                    // get the result of the validationResponse
                    InputValidation.EntryValidationResult validationResult = validationResponse.Result;

                    // we now use the validation result to determine whether to proceed with adding the item or show an error message
                    switch (validationResult)
                    {
                        case InputValidation.EntryValidationResult.Success:
                            // if validation succeeded, proceed with adding the item

                            if (validationMessageField.enabled)
                            {
                                validationMessageField.enabled = false; // hide any previous error message
                            }

                            // reset the fields
                            ChangeInputFieldStyle(URL._inputField.gameObject, false);
                            ChangeInputFieldStyle(Password._inputField.gameObject, false);

                            // perform a meta-only validation of the provided BEE file before adding the key
                            try
                            {
                                if (!validationMessageField.Descriptor.gameObject.activeSelf)
                                    validationMessageField.Descriptor.gameObject.SetActive(true);

                                validationMessageField.Descriptor.SetTitle("Validating BEE file");
                                validationMessageField.Descriptor.SetDescription("Checking BEE metadata...");

                                // temp item do not use to add new item with!
                                BasisDataStoreItemKeys.ItemKey tempItem = new BasisDataStoreItemKeys.ItemKey
                                {
                                    Pass = validationResponse.Password,
                                    Url = validationResponse.ProcessedUrl,
                                    Mode = 0, // we are going to infer from the type of data the item is
                                };

                                var tempWrapper = CreateNewWrapperFromItem(tempItem);

                                BasisProgressReport Report = new BasisProgressReport();
                                CancellationTokenSource CancellationSource = new CancellationTokenSource();

                                // Attempt a meta-only load (this will download or read connector info and cache meta on disk)
                                bool isValid = await BasisBeeManagement.HandleMetaOnlyLoad(tempWrapper.basisTrackedBundleWrapper, Report, CancellationSource.Token);

                                if (isValid)
                                {
                                    // Attempt to read the metadata back from disk into the wrapper
                                    BasisLoadableBundleWrapper loaded = await LoadWrapperFromDisc(tempItem, tempWrapper);

                                    // infered item type
                                    BundledContentHolder.Mode itemType = BundledContentHolder.Mode.Legacy;

                                    // grab the meta data
                                    if(loaded.BasisLoadableBundle?.BasisBundleConnector?.MetaData != null)
                                    {
                                        if (loaded.BasisLoadableBundle.BasisBundleConnector.MetaData.ComponentNames != null)
                                        {
                                            BasisDebug.Log($"BasisComponentNames = {loaded.BasisLoadableBundle.BasisBundleConnector.MetaData.ComponentNames}");
                                            BasisDebug.Log($"BasisComponentNamesLength = {loaded.BasisLoadableBundle.BasisBundleConnector.MetaData.ComponentNames.Length}");

                                            // lets attempt to find out what type of item it is?

                                            // grab components
                                            foreach (BasisBundleConnector.BasisComponentName comp in loaded.BasisLoadableBundle.BasisBundleConnector.MetaData.ComponentNames)
                                            {
                                                BasisDebug.Log($"BasisComponentName = {comp.Name} count = {comp.count}");
                                                switch (comp.Name.ToLower())
                                                {
                                                    case "basisprop":
                                                        itemType = BundledContentHolder.Mode.Prop;
                                                        break;
                                                    case "basisavatar":
                                                        itemType = BundledContentHolder.Mode.Avatar;
                                                        break;
                                                    case "basisccene":
                                                        itemType = BundledContentHolder.Mode.World;
                                                        break;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            BasisDebug.LogWarning($"Warning BEE file from url = {tempItem.Url} does not contain metadata ComponentNames, consider updating it!");
                                        }
                                    }
                                    else
                                    {
                                        BasisDebug.LogWarning($"Warning BEE file from url = {tempItem.Url} does not contain metadata, consider updating it!");
                                    }

                                    // if the provided content did not change the item type assume its legacy or old BEE file with no metadata
                                    if(itemType == BundledContentHolder.Mode.Legacy)
                                    {
                                        // prompt them for what content
                                        itemType = await PromptUserToDefineLegacyContent();
                                        
                                        // if for whatever reason they did not enter anything else other than legacy?
                                        if(itemType == BundledContentHolder.Mode.Legacy)
                                        {
                                            // Still legacy? Yea no goodbye
                                            throw new Exception("Request Denied. Please specify content type for your legacy content.");
                                        }
                                    }

                                    // add the item to the basis key store
                                    await AddNewNewItemKey(itemType, validationResponse.ProcessedUrl, validationResponse.Password);
                                    // just close the overlay
                                    newItemDialogBox.CloseWithResult(null);

                                    // change the focus of the UI to goto where the users newly added content is
                                    _currentPage = ModeToPage(itemType);
                                    
                                    tabGroup.SetValue((int)_currentPage); // this will trigger the tab selection and associated content loading

                                    // switch to the page
                                    await RefreshTabAsync(_currentPage);

                                }
                                else
                                {
                                    throw new Exception("The provided BEE file url could not provide the bundle array.");
                                }
                            }
                            catch (Exception ex)
                            {
                                BasisDebug.LogError(ex);
                                ChangeInputFieldStyle(URL._inputField.gameObject, true);
                                ChangeInputFieldStyle(Password._inputField.gameObject, true);

                                if (!validationMessageField.Descriptor.gameObject.activeSelf)
                                    validationMessageField.Descriptor.gameObject.SetActive(true);

                                validationMessageField.Descriptor.SetTitle("BEE Validation Error");
                                validationMessageField.Descriptor.SetDescription($"Failed to validate BEE file: {ex.Message}");

                                newItemDialogBox.IsBusy = false;

                                // update interactability for fields based on dialog busy
                                UpdateInputFieldInteractability(URL, Password, newItemDialogBox);

                                return;
                            }

                            return;
                        case InputValidation.EntryValidationResult.EmptyUrl:
                            ChangeInputFieldStyle(URL._inputField.gameObject, true);
                            break;
                        case InputValidation.EntryValidationResult.InvalidUrlFormat:
                            ChangeInputFieldStyle(URL._inputField.gameObject, true);
                            break;
                        case InputValidation.EntryValidationResult.InvalidUrlScheme:
                            ChangeInputFieldStyle(URL._inputField.gameObject, true);
                            break;
                        case InputValidation.EntryValidationResult.EmptyPassword:
                            ChangeInputFieldStyle(URL._inputField.gameObject, false);
                            ChangeInputFieldStyle(Password._inputField.gameObject, true);
                            break;
                        case InputValidation.EntryValidationResult.DuplicateEntry:
                            ChangeInputFieldStyle(URL._inputField.gameObject, true);
                            ChangeInputFieldStyle(Password._inputField.gameObject, true);
                            break;
                        default:
                            BasisDebug.LogWarning("validation result returned unknown result unable to handle visual representation on UI.");
                            break;
                    }

                    // re-enable input
                    URL._inputField.interactable = true;
                    Password._inputField.interactable = true;

                    // if validation failed, show an error message and do not proceed
                    string errorMessage = validationResult switch
                    {
                        InputValidation.EntryValidationResult.EmptyUrl => "URL cannot be empty.",
                        InputValidation.EntryValidationResult.InvalidUrlFormat => "URL format is invalid.",
                        InputValidation.EntryValidationResult.InvalidUrlScheme => "URL must start with http:// or https://",
                        InputValidation.EntryValidationResult.EmptyPassword => "Password cannot be empty.",
                        InputValidation.EntryValidationResult.DuplicateEntry => "An entry with this URL already exists in your library.",
                        _ => "Unknown validation error."
                    };

                    if (!validationMessageField.Descriptor.gameObject.activeSelf)
                        validationMessageField.Descriptor.gameObject.SetActive(true);

                    // setting the title and desc auto enables the game object anyway
                    validationMessageField.Descriptor.SetTitle(validationResult.ToString());
                    validationMessageField.Descriptor.SetDescription(errorMessage);

                    // For simplicity, using Debug.LogWarning. In a real implementation, you would want to show this in the UI.
                    BasisDebug.LogWarning(errorMessage);
                    newItemDialogBox.IsBusy = false;

                    // update interactability for fields based on dialog busy
                    UpdateInputFieldInteractability(URL, Password, newItemDialogBox);
                }
                catch (Exception ex)
                {
                    BasisDebug.LogError(ex);
                    newItemDialogBox.IsBusy = false;
                    // update interactability for fields based on dialog busy
                    UpdateInputFieldInteractability(URL, Password, newItemDialogBox);
                }
            };

            // await until user closes or accepts
            return await newItemDialogBox.WaitAsync();
        }

        /// <summary>
        /// Used with the add new item button to add a new item to the basis key store for items
        /// </summary>
        public static async Task AddNewNewItemKey(BundledContentHolder.Mode mode, string URL, string Password)
        {
            if (mode == BundledContentHolder.Mode.Legacy)
            {
                BasisDebug.LogWarning($"AddNewNewItemKey() -> was invoked with mode = {mode}, for item {URL}. Please consider updating your BEE file to include metadata. (Use Advance settings to override auto import type your content will be marked legacy)");
            }

            var key = new BasisDataStoreItemKeys.ItemKey
            {
                Pass = Password,
                Url = URL,
                Mode = mode,
            };

            await BasisDataStoreItemKeys.AddNewKey(key);

            // this is misleading in user experience expectations
            // while it saves 2 clicks, it will force a user out of an avatar straight up, when the action is not desired
            // ideally we should prompt the user if they want to directly load into their most recently loaded asset
            // TODO: revise
            // if(mode == BundledContentHolder.Mode.Avatar)
            // {
            //     BasisLoadableBundle loadableBundle = new()
            //     {
            //         UnlockPassword = Password,
            //         BasisRemoteBundleEncrypted = new BasisRemoteEncyptedBundle { RemoteBeeFileLocation = URL },
            //         BasisBundleConnector = new BasisBundleConnector(),
            //         BasisLocalEncryptedBundle = new BasisStoredEncryptedBundle()
            //     };

            //     await BasisLocalPlayer.Instance.CreateAvatar(BasisLocalPlayer.LoadModeNetworkDownloadable, loadableBundle);
            // }
        }

        #endregion

        #region CreateItemCard, ShowItemOverlay, ApplyMetaDataToButton

        private static void LockedIcon()
        {
            
        }

        /// <summary>
        /// The item card displayed all around the library menu
        /// </summary>
        private static void CreateItemCard(BasisDataStoreItemKeys.ItemKey item, RectTransform container)
        {
            PanelButton buttonPanel = PanelButton.CreateNew(ButtonStyles.Prop, container);
            var urlKey = item.Url ?? string.Empty;
            var desc = buttonPanel.Descriptor;

            // Try get cached meta once
            CachedMetaData.CachedContent cachedMeta;
            CachedMetaData.TryGetMeta(urlKey, out cachedMeta);

            if (item.PinnedSettings.IsPinned)
            {
                // create an image for this card in top right with an offset of -35, -35
                PanelImage pinnedIcon = PanelImage.CreateNew(buttonPanel.Descriptor);
                pinnedIcon.SetIcon(AddressableAssets.GetSprite(AddressableAssets.Sprites.Pin), true);
                pinnedIcon.rectTransform.anchorMin = new Vector2(1, 1);
                pinnedIcon.rectTransform.anchorMax = new Vector2(1, 1);
                pinnedIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                pinnedIcon.rectTransform.anchoredPosition = new Vector2(-35, -35);
                pinnedIcon.rectTransform.sizeDelta = new Vector2(40, 40);
            }
            else
            {
                if(item.IsEmbedded)
                {    
                    PanelImage embeddedIcon = PanelImage.CreateNew(buttonPanel.Descriptor);
                    embeddedIcon.SetIcon(AddressableAssets.GetSprite(AddressableAssets.Sprites.Locked), true);
                    embeddedIcon.rectTransform.anchorMin = new Vector2(1, 1);
                    embeddedIcon.rectTransform.anchorMax = new Vector2(1, 1);
                    embeddedIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    embeddedIcon.rectTransform.anchoredPosition = new Vector2(-35, -35);
                    embeddedIcon.rectTransform.sizeDelta = new Vector2(40, 40);
                }
            }

            if (item.IsEmbedded && IsProp(item))
            {
                // TODO fix representation for stacked icons
                // // create an image for this card in top right with an offset of -35, -35
                // PanelImage networkIcon = PanelImage.CreateNew(buttonPanel.Descriptor);
                // networkIcon.SetIcon(AddressableAssets.GetSprite(AddressableAssets.Sprites.Locked), true);
                // networkIcon.rectTransform.anchorMin = new Vector2(1, 1);
                // networkIcon.rectTransform.anchorMax = new Vector2(1, 1);
                // networkIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                // networkIcon.rectTransform.anchoredPosition = new Vector2(-35, -35);
                // networkIcon.rectTransform.sizeDelta = new Vector2(40, 40);

                desc.SetTitle(urlKey);
                desc.SetDescription(urlKey);
                desc.ForceRebuild();

                // yeah I know dw about temporary
                if (desc.ContentParent.TryGetComponent<Image>(out Image image))
                {
                    image.gameObject.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
                    image.sprite = EmbeddedItems.GetSpriteForEmbeddedItem(item);
                }
            }
            else
            {

                if (cachedMeta != null)
                {
                    ApplyMetaDataToButton(buttonPanel, cachedMeta, urlKey);
                }
                else
                {
                    desc.SetTitle("Loading...");
                    desc.SetDescription(urlKey);
                    desc.ForceRebuild();

                    _ = CachedMetaData.PreloadMetaDataForItem(item);
                }
            }

            buttonPanel.OnClicked += () =>
            {
                try
                {
                    ShowItemOverlay(item);
                }
                catch (Exception ex)
                {
                    BasisDebug.LogError(ex);
                }
            };
        }

        private static BasisDataStoreItemKeys.ItemKey _activeItem;

        private static string ConvertItemKeyToAddressableSprite(BasisDataStoreItemKeys.ItemKey item)
        {
            switch (item.Mode)
            {
                case BundledContentHolder.Mode.Avatar:
                    return AddressableAssets.Sprites.Avatars;
                case BundledContentHolder.Mode.Prop:
                    return AddressableAssets.Sprites.Items;
                case BundledContentHolder.Mode.World:
                    return AddressableAssets.Sprites.World;
                default:
                    BasisDebug.LogWarning($"ConvertItemKeyToAddressableSprite was given an item with an unknown mode of {item.Mode}, cannot determine icon defaulting to items icon!");
                    return AddressableAssets.Sprites.Items;
            }
        }

        public static string PinnedText(BasisDataStoreItemKeys.ItemKey item)
        {
            return item.PinnedSettings.IsPinned ? "Pinned" : "Pin";
        }

        public static void ShowItemOverlay(BasisDataStoreItemKeys.ItemKey item)
        {
            #region ITEM OVERLAY SETUP

            Vector2 overlaySize = new Vector2(1200, 960);

            // grab the content from the cache 
            CachedMetaData.CachedContent metadata;
            CachedMetaData.TryGetMeta(item.Url, out metadata);

            // the network type of the item
            BundledContentHolder.NetworkType desiredNetworkType = BundledContentHolder.NetworkType.Local;
            bool ephemeral = true;  // the persistence behavior of the item 
            BasisBundleConnector.BasisMetaData basisMetaData; // grab the meta data
            BasisBundleDescription description; // grab the description data
            Sprite targetSprite = null;   // target sprite

            // default string text for embedded item
            string embedItem = "Emebbed item";

            if (item.IsEmbedded && IsProp(item))
            {
                description = new BasisBundleDescription()
                {
                    AssetBundleName = item.Url,
                    AssetBundleDescription = embedItem,
                };

                targetSprite = EmbeddedItems.GetSpriteForEmbeddedItem(item);

            }
            else
            {
                // grab BEE file information
                basisMetaData = metadata.BasisBundleConnector.MetaData;
                description = metadata.BasisBundleConnector.BasisBundleDescription;
                targetSprite = CachedMetaData.CreateSpriteFromMetaData(metadata);
            }

            // Not sure why we need this so lets to remove.
            _activeItem = item;

            // Build overlay using DialogBox helper
            DialogBox<BasisDataStoreItemKeys.ItemKey> existingItemDialog = DialogBox<BasisDataStoreItemKeys.ItemKey>.Create(panel, overlaySize,
                $"{TitleToCase(description.AssetBundleName)}",
                $"{(description.AssetBundleDescription.Length > 0 ? description.AssetBundleDescription : "No description was provided.")}",
                ConvertItemKeyToAddressableSprite(item));

            // only items can be pinned as props
            // this has to be here to ensure correct placement
            if (item.Mode == BundledContentHolder.Mode.Prop)
            {
                // create the exit button for the dialog box
                var pinButton = PanelButton.CreateNew(ButtonStyles.ExitButton, existingItemDialog.Descriptor.Header);
                pinButton.Descriptor.SetTitle(PinnedText(item));
                pinButton.Descriptor.SetIcon(AddressableAssets.Sprites.Pin);
                pinButton.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 125);
                pinButton.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 50);
                pinButton.OnClicked += async () =>
                {
                    // grab the state of the item if its pinned
                    bool isPinned = item.PinnedSettings.IsPinned;

                    // create new pinned settings
                    BasisDataStoreItemKeys.PinnedSettings newPinnedSettings = new BasisDataStoreItemKeys.PinnedSettings
                    {
                        IsPinned = !isPinned,
                        NetworkType = desiredNetworkType,
                        IsEphemeral = ephemeral
                    };

                    // toggle the item is pinned in the key files store
                    bool success = await BasisDataStoreItemKeys.UpdatePinnedSettings(item, newPinnedSettings);

                    // update it in the cache
                    item.PinnedSettings.IsPinned = !isPinned;

                    await RefreshCurrentTab();
                    //await RefreshPinnedProviders();
                    pinButton.Descriptor.SetTitle(PinnedText(item));

                    BasisDebug.Log($"Pinned button was pressed on item = {item.Url}, success = {success}, item.IsPinned = {item.PinnedSettings.IsPinned}");
                };
            }

            // create the exit button for the dialog box
            var button = PanelButton.CreateNew(ButtonStyles.ExitButton, existingItemDialog.Descriptor.Header);
            button.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 125);
            button.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 50);
            button.OnClicked += () => existingItemDialog.Cancel(null);

            // icon for the selected item
            var itemIcon = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.GroupLargeIconVertical, existingItemDialog.Descriptor.ContentParent);

            switch (item.Mode)
            {
                case BundledContentHolder.Mode.Avatar:
                    itemIcon.SetHeight(750); // make the display panel bigger because 
                    break;
                default:
                    itemIcon.SetHeight(500);
                    break;
            }

            itemIcon.SetIcon(targetSprite);

            #endregion

            // create a scrollable page for the information of the selected content item
            PanelTabPage scrollablePage = PanelTabPage.CreateNew(itemIcon.ContentParent);
            PanelElementDescriptor descriptor = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.ScrollViewVerticalLibraryParentContentSize, scrollablePage.Descriptor.ContentParent);
            scrollablePage.Descriptor.ContentParent = descriptor.ContentParent;

            #region CREATION DATE

            string creationDate = string.Empty; // get the creation date of the basis bundle

            if (item.IsEmbedded && IsProp(item))
            {
                creationDate = embedItem;
            }
            else
            {
                creationDate = metadata.BasisBundleConnector.DateOfCreation;
                // determine what the creation date text is gonna say
                if (string.IsNullOrEmpty(creationDate))
                {
                    creationDate = "N/A";
                }
                else
                {
                    creationDate = DateTime
                        .Parse(creationDate, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
                        .ToString(CultureInfo.InvariantCulture);

                    creationDate += " UTC";
                }
            }


            // creation date and time
            PanelTextField createdInformationTextField = PanelTextField.CreateNew(TextFieldStyles.EntryVertical, scrollablePage.Descriptor.ContentParent);
            createdInformationTextField._inputField.gameObject.SetActive(false); // disable the text input field box
            createdInformationTextField.Descriptor.SetTitle("Creation Date");
            createdInformationTextField.Descriptor.SetIcon(AddressableAssets.Sprites.Clock);
            createdInformationTextField.Descriptor.SetDescription($"{creationDate}");

            createdInformationTextField.Descriptor.SetHeight(50);
            createdInformationTextField.Descriptor.SetWidth(400);

            #endregion

            #region PLATFORM ICONS

            // creation date and time
            PanelTextField platformIconsTextField = PanelTextField.CreateNew(TextFieldStyles.EntryVerticalHorizontalContent, scrollablePage.Descriptor.ContentParent);
            platformIconsTextField._inputField.gameObject.SetActive(false); // disable the text input field box
            platformIconsTextField.Descriptor.SetTitle("Available Platforms");
            platformIconsTextField.Descriptor.SetIcon(AddressableAssets.Sprites.Computer);
            platformIconsTextField.Descriptor.SetHeight(130);
            platformIconsTextField.Descriptor.SetWidth(400);

            if (item.IsEmbedded && IsProp(item))
            {
                platformIconsTextField.Descriptor.SetDescription($"All - Embedded Item");
            }
            else
            {
                string[] platforms = metadata.BasisBundleConnector.BasisBundleGenerated.Select(pair => pair.Platform).ToArray();
                string supported_platforms = string.Join(" | ", platforms);
                platformIconsTextField.Descriptor.SetDescription($"{supported_platforms}");

                foreach (string platform in platforms)
                {
                    PanelImage panelImage = PanelImage.CreateNew(PanelImage.ImageStyles.SimpleSquare, platformIconsTextField.Descriptor.ContentParent);
                    panelImage.SetSize(new Vector2(80, 80));

                    switch (platform)
                    {
                        case "StandaloneWindows64":

                            panelImage.SetIcon(AddressableAssets.Sprites.PlatformStandaloneWindows64);
                            break;

                        case "StandaloneOSX":
                            panelImage.SetIcon(AddressableAssets.Sprites.PlatformStandaloneOSX);
                            break;

                        case "StandaloneLinux64":
                            panelImage.SetIcon(AddressableAssets.Sprites.PlatformStandaloneLinux64);
                            break;

                        case "Android":
                            panelImage.SetIcon(AddressableAssets.Sprites.PlatformMobileAndroid);
                            break;

                        case "iOS":
                            panelImage.SetIcon(AddressableAssets.Sprites.PlatformMobileiOS);
                            break;
                    }
                }
            }


            #endregion

            // lets create a grid to put the items below in

            PanelTabPage grid = PanelTabPage.CreateNew(scrollablePage.Descriptor.ContentParent);
            PanelElementDescriptor scrollViewGridDescriptor = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.ScrollViewGridLibrary, grid.Descriptor.ContentParent);
            grid.Descriptor.ContentParent = scrollViewGridDescriptor.ContentParent;
            grid.Descriptor.SetHeight(150);

            #region POLYGON COUNT

            long polygonCount = 0;

            if (item.IsEmbedded  && IsProp(item))
            {
                polygonCount = 0;
            }
            else
            {
                polygonCount = metadata.BasisBundleConnector.MetaData.TrianglesCount;
            }

            // creation date and time
            PanelTextField polygonTextField = PanelTextField.CreateNew(TextFieldStyles.EntryVertical, grid.Descriptor.ContentParent);//scrollablePage.Descriptor.ContentParent);
            polygonTextField._inputField.gameObject.SetActive(false); // disable the text input field box
            polygonTextField.Descriptor.SetTitle("Triangle Count");
            polygonTextField.Descriptor.SetIcon(AddressableAssets.Sprites.Polygons);
            polygonTextField.Descriptor.SetDescription($"{polygonCount}");

            polygonTextField.Descriptor.SetHeight(50);
            polygonTextField.Descriptor.SetWidth(400);

            #endregion

            #region MATERIAL COUNT

            long materialCount = 0;

            if (item.IsEmbedded  && IsProp(item))
            {
                materialCount = 0;
            }
            else
            {
                materialCount = metadata.BasisBundleConnector.MetaData.MaterialCount;
            }

            // creation date and time
            PanelTextField materialTextField = PanelTextField.CreateNew(TextFieldStyles.EntryVertical, grid.Descriptor.ContentParent);//scrollablePage.Descriptor.ContentParent);
            materialTextField._inputField.gameObject.SetActive(false); // disable the text input field box
            materialTextField.Descriptor.SetTitle("Material Count");
            materialTextField.Descriptor.SetIcon(AddressableAssets.Sprites.Materials);
            materialTextField.Descriptor.SetDescription($"{materialCount}");

            materialTextField.Descriptor.SetHeight(50);
            materialTextField.Descriptor.SetWidth(400);

            #endregion

            #region BONES COUNT

            long boneCount = 0;

            if (item.IsEmbedded && IsProp(item))
            {
                boneCount = 0;
            }
            else
            {
                boneCount = metadata.BasisBundleConnector.MetaData.BonesCount;
            }

            // creation date and time
            PanelTextField bonesTextField = PanelTextField.CreateNew(TextFieldStyles.EntryVertical, grid.Descriptor.ContentParent);//scrollablePage.Descriptor.ContentParent);
            bonesTextField._inputField.gameObject.SetActive(false); // disable the text input field box
            bonesTextField.Descriptor.SetTitle("Bones Count");
            bonesTextField.Descriptor.SetIcon(AddressableAssets.Sprites.Bones);
            bonesTextField.Descriptor.SetDescription($"{boneCount}");

            bonesTextField.Descriptor.SetHeight(50);
            bonesTextField.Descriptor.SetWidth(400);

            #endregion

            #region ITEM FIELDS

            string itemID = string.Empty; // item id

            if (item.IsEmbedded && IsProp(item))
            {
                itemID = embedItem;
            }
            else
            {
                itemID = metadata.BasisBundleConnector.UniqueVersion;
            }

            PanelPasswordField IDField = PanelPasswordField.CreateNew(PasswordFieldStyles.EntryVertical, scrollablePage.Descriptor.ContentParent);//accessibleItemDataTextField.Descriptor.ContentParent);
            IDField._placeholderField.text = "";
            IDField._inputField.interactable = false;
            IDField.Descriptor.SetTitle("Unique Version:");
            IDField.Descriptor.SetDescription("The unique version number of the basis bundle.");
            IDField.Descriptor.SetIcon(AddressableAssets.Sprites.Information);
            IDField.SetPassword(itemID);
            //IDField.LayoutElement.minWidth = 500;

            PanelPasswordField urlField = PanelPasswordField.CreateNew(PasswordFieldStyles.EntryVertical, scrollablePage.Descriptor.ContentParent);//accessibleItemDataTextField.Descriptor.ContentParent);
            urlField._placeholderField.text = "";
            urlField._inputField.interactable = false;
            urlField.Descriptor.SetTitle("BEE File Url:");
            urlField.Descriptor.SetDescription("The direct link to the BEE file.");
            urlField.Descriptor.SetIcon(AddressableAssets.Sprites.Network);
            urlField.SetPassword(item.Url);
            //urlField.LayoutElement.minWidth = 500;

            PanelPasswordField passField = PanelPasswordField.CreateNew(PasswordFieldStyles.EntryVertical, scrollablePage.Descriptor.ContentParent);//accessibleItemDataTextField.Descriptor.ContentParent);
            passField._placeholderField.text = "";
            passField._inputField.interactable = false;
            passField.Descriptor.SetTitle("BEE File Password:");
            passField.Descriptor.SetDescription("This is the password that was generated with you BEE file.");
            passField.Descriptor.SetIcon(AddressableAssets.Sprites.Unlocked);
            passField.SetPassword(item.Pass); // if supported
            //passField.LayoutElement.minWidth = 500;

            #endregion

            #region ITEM NETWORK MODE & EPHEMERAL TOGGLE

            // // advanced setting button parent
            // PanelTabGroup advanceSettingsPanelGroup = PanelTabGroup.CreateNew(existingItemDialog.Descriptor.ContentParent, LayoutDirection.HorizontalNoBackground);
            // advanceSettingsPanelGroup.Descriptor.SetHeight(60);
            // advanceSettingsPanelGroup.Descriptor.SetWidth(900);

            // reference to the PanelTabGroup advancedActionsPanel
            // PanelTabGroup advancedActionsPanel = null;

            // PanelButton advancedSettingsButton = PanelButton.CreateNew(ButtonStyles.StandardButton,  advanceSettingsPanelGroup.TabButtonParent ); //actionsPanel.TabButtonParent existingItemDialog.Descripto
            // advancedSettingsButton.Descriptor.SetTitle("Advanced Settings");
            // advancedSettingsButton.Descriptor.SetWidth(900);
            // advancedSettingsButton.Descriptor.SetHeight(60);
            // // on load of a item we do these actions
            // advancedSettingsButton.OnClicked += async () =>
            // {
            //     if(advancedActionsPanel != null)
            //     {
            //         // toggle the advance panel
            //         advancedActionsPanel.Descriptor.gameObject.SetActive(!advancedActionsPanel.Descriptor.gameObject.activeSelf);
            //     }
            //     // if (existingItemDialog.IsBusy) return;
            //     // existingItemDialog.IsBusy = true;

            //     // try
            //     // {
            //     //     BasisDebug.Log($"Load Button Clicked for item: {item.Url}");
            //     //     await LoadSelectedItem(item, desiredNetworkType, !ephemeral);
            //     // }
            //     // catch (Exception ex)
            //     // {
            //     //     BasisDebug.LogError(ex);
            //     // }
            //     // finally
            //     // {
            //     //     // just close the overlay instead.
            //     //     await existingItemDialog.CloseAsync();
            //     // }

            // };

            // only do this menu for props
            if (item.Mode == BundledContentHolder.Mode.Prop)
            {
                // Advanced Settings
                PanelTabGroup advancedActionsPanel = PanelTabGroup.CreateNew(PanelTabGroup.TabGroupStyles.VerticalStackedNoBackground, existingItemDialog.Descriptor.ContentParent);
                advancedActionsPanel.Descriptor.SetHeight(160);

                // content sync mode dropdown determines whether the new item is flagged as networked or local, which affects filtering and how the item is loaded later
                PanelDropdown contentSyncModeDropDown = PanelDropdown.CreateNew(PanelDropdown.DropdownStyles.Entry, advancedActionsPanel.TabButtonParent);
                string[] contentSyncModes = Enum.GetNames(typeof(BundledContentHolder.NetworkType));
                contentSyncModeDropDown.Descriptor.SetTitle("Network Type");
                contentSyncModeDropDown.Descriptor.SetDescription("If the item is set to local, it will only be visible and interactive for you.");
                contentSyncModeDropDown.Descriptor.SetIcon(AddressableAssets.Sprites.Network);
                contentSyncModeDropDown.AssignEntries(contentSyncModes.ToList());
                contentSyncModeDropDown.Descriptor.SetSize(new Vector2(700, 80));

                // DISABLE THIS DROPDOWN IF EMBEDED ITEM
                if (contentSyncModeDropDown.Descriptor.gameObject.TryGetComponent<PanelDropdown>(out PanelDropdown dropdown))
                {
                    if (dropdown.DropdownComponent != null)
                    {
                        // if the item is embedded dont interact
                        dropdown.DropdownComponent.interactable = !item.IsEmbedded;
                    }
                }

                // set the default network type
                contentSyncModeDropDown.SetValueWithoutNotify(desiredNetworkType.ToString());
                contentSyncModeDropDown.OnValueChanged = (val) =>
                {
                    if (Enum.TryParse(contentSyncModeDropDown.SelectedString, out BundledContentHolder.NetworkType selectedNetType))
                    {
                        desiredNetworkType = selectedNetType;
                        BasisDebug.Log($"Selected Network Type: {desiredNetworkType}");
                    }
                    else
                    {
                        BasisDebug.LogError("Coudnt Parse BundledContentHolder.NetworkType!");
                    }
                };

                //content persistence toggle determines weather
                PanelToggle contentPersistenceToggle = PanelToggle.CreateNew(advancedActionsPanel.TabButtonParent, PanelToggle.Styles.Entry);
                contentPersistenceToggle.SetValueWithoutNotify(ephemeral);
                contentPersistenceToggle.Descriptor.SetTitle("Ephemeral Mode");
                contentPersistenceToggle.Descriptor.SetIcon(AddressableAssets.Sprites.HourGlass);
                contentPersistenceToggle.Descriptor.SetDescription("This item will only be visible to people currently in the instance. Late joiners wont be able to see this.");
                contentPersistenceToggle.Descriptor.SetSize(new Vector2(700, 80));
                contentPersistenceToggle.OnValueChanged = (val) =>
                {
                    ephemeral = val;
                };

                // DISABLE THIS TOGGLE IF THE ITEM IS EMBEDDED
                if (contentPersistenceToggle.Descriptor.gameObject.TryGetComponent<Toggle>(out Toggle toggle))
                {
                    // if the item is embedded dont interact
                    toggle.interactable = !item.IsEmbedded;
                }
            }

            #endregion

            // Delete & Load Buttons
            PanelTabGroup actionsPanel = PanelTabGroup.CreateNew(existingItemDialog.Descriptor.ContentParent, LayoutDirection.HorizontalNoBackground);

            actionsPanel.Descriptor.SetHeight(60);
            //actionsPanel.Descriptor.SetWidth(900);

            PanelButton deletePanelButton = PanelButton.CreateNew(ButtonStyles.CancelButton, actionsPanel.TabButtonParent); //ButtonStyles.Cancel
            deletePanelButton.Descriptor.SetTitle("Delete");
            deletePanelButton.Descriptor.SetWidth(220);
            deletePanelButton.Descriptor.SetHeight(60);

            // DISABLE THIS BUTTON IF ITEM IS EMBEDDED
            if (deletePanelButton.Descriptor.gameObject.TryGetComponent<Button>(out Button deleteButtonComponent))
            {
                // if the item is embedded dont interact
                deleteButtonComponent.interactable = !item.IsEmbedded;
            }

            // upon delete we do these actions
            deletePanelButton.OnClicked += async () =>
            {
                if(item.IsEmbedded) return; // prevent delete button working on embedded items
                if (existingItemDialog.IsBusy) return;
                existingItemDialog.IsBusy = true;

                // remove the item
                await BasisDataStoreItemKeys.RemoveKey(item);
                // just close the overlay instead.
                existingItemDialog.CloseWithResult(null);
                // refresh current tab
                await RefreshCurrentTab();
            };


            PanelButton loadPanelButton = PanelButton.CreateNew(ButtonStyles.AcceptButton, actionsPanel.TabButtonParent);
            loadPanelButton.Descriptor.SetTitle("Load");
            loadPanelButton.Descriptor.SetWidth(620);
            loadPanelButton.Descriptor.SetHeight(60);
            // on load of a item we do these actions
            loadPanelButton.OnClicked += async () =>
            {
                if (existingItemDialog.IsBusy) return;
                existingItemDialog.IsBusy = true;

                try
                {
                    BasisDebug.Log($"Load Button Clicked for item: {item.Url}");
                    await LoadSelectedItem(item, desiredNetworkType, !ephemeral);
                }
                catch (Exception ex)
                {
                    BasisDebug.LogError(ex);
                }
                finally
                {
                    // just close the overlay instead.
                    existingItemDialog.CloseWithResult(null);
                }
            };
        }

        private static void ApplyMetaDataToButton(PanelButton buttonPanel, CachedMetaData.CachedContent cachedMeta, string urlKey)
        {
            Sprite iconSprite = CachedMetaData.CreateSpriteFromMetaData(cachedMeta);

            buttonPanel.SetIcon(iconSprite, false);

            var desc = buttonPanel.Descriptor;
            desc.SetTitle(TitleToCase(!string.IsNullOrEmpty(cachedMeta.Name) ? cachedMeta.Name : urlKey));
            desc.SetDescription(urlKey);
            desc.ForceRebuild();
        }

        #endregion

        #region LoadSelectedItem

        /// <summary>
        /// used to load a target item from a BasisDataStoreItemKeys.ItemKey
        /// items are branched with a switch statement depending on item mode
        /// </summary>
        /// <param name="item">The ItemKey desired to be loaded</param>
        /// <param name="networkType">default local unless specified</param>
        /// <returns></returns>
        public static async Task LoadSelectedItem(BasisDataStoreItemKeys.ItemKey item, BundledContentHolder.NetworkType networkType = BundledContentHolder.NetworkType.Local, bool persistence = false, bool modifyScale = false)
        {
            // At this point the item should be fully loaded and ready to use. What happens next is up to you and your application needs.
            // For example, you could raise an event that other parts of your app listen for, or directly instantiate the loaded content if it's a prefab.
            BasisDebug.Log($"Attempting to load selected item: {item.Url} item type {item.Mode} with network type {networkType} persistent = {persistence} modifyScale = {modifyScale}");

            try
            {
                switch (item.Mode)
                {
                    case BundledContentHolder.Mode.Avatar:
                        // For avatars we might want to apply them directly to the player instead of spawning in the world as a separate object
                        await ContentLoader.LoadAvatar(item);
                        break;
                    case BundledContentHolder.Mode.Prop:
                        await ContentLoader.LoadProp(item, networkType, persistence, modifyScale);
                        break;
                    case BundledContentHolder.Mode.World:
                        await ContentLoader.LoadWorld(item);
                        break;
                    default:
                        BasisDebug.LogError($"LoadSelectedItem was given an item with an unknown mode of {item.Mode}, cannot determine how to load!");
                        break;
                }
            }
            catch (Exception ex)
            {
                BasisDebug.LogError(ex);
            }
        }

        #endregion

        #region InstiatedListElement

        // TODO use items key
        // 
        private static void CreateListEntry(BasisRuntimeSpawnRegistry.SpawnInstance itemKey, RectTransform parentTabGroup, string instanceID)
        {
            // // icon for the selected item
            // var itemIcon = PanelElementDescriptor.CreateNew(
            //     PanelElementDescriptor.ElementStyles.GroupLargeIconVertical, parentTabGroup);

            // createdInformationTextField.Descriptor.SetTitle(item.Url);
            // createdInformationTextField.Descriptor.SetDescription($"Embedded item");
            // itemIcon.SetIcon(EmbeddedItems.GetSpriteForEmbeddedItem(item));

            BasisDebug.Log($"creating list entry for item url = {itemKey.Url} with instanceID = {instanceID}");

            PanelTabGroup itemListPanel = PanelTabGroup.CreateNew(PanelTabGroup.TabGroupStyles.HorizontalStackedNoBackground, parentTabGroup);
            itemListPanel.Descriptor.SetWidth(1400);
            itemListPanel.Descriptor.SetHeight(80);

            // simple info
            PanelTextField itemTextInfo = PanelTextField.CreateNew(TextFieldStyles.Entry, itemListPanel.TabButtonParent);
            itemTextInfo._inputField.gameObject.SetActive(false); // disable the text input field box
            if (itemKey.SpawnMethod == 0)
            {
                itemTextInfo.Descriptor.SetTitle(TitleToCase(itemKey.Url));
            }
            else
            {
                // Try get cached meta once
                CachedMetaData.CachedContent cachedMeta;
                CachedMetaData.TryGetMeta(itemKey.Url, out cachedMeta);
                itemTextInfo.Descriptor.SetTitle(TitleToCase(cachedMeta.BasisBundleConnector.BasisBundleDescription.AssetBundleName));
            }
            //createdInformationTextField.Descriptor.SetIcon(EmbeddedItems.GetSpriteForEmbeddedItem(item));
            itemTextInfo.Descriptor.SetDescription($"Embedded item");

            itemTextInfo.Descriptor.SetHeight(50);
            itemTextInfo.Descriptor.SetWidth(400);

            PanelButton removeItem = PanelButton.CreateNew(ButtonStyles.CancelButton, itemListPanel.TabButtonParent);
            removeItem.Descriptor.SetTitle("Remove");
            removeItem.SetSize(new Vector2(200, 60));
            removeItem.OnClicked += async () =>
            {
                BasisDebug.Log($"CreateListEntry() -> requested removal of item = {itemKey.Url} of instanceID = {instanceID} of SpawnMethod = {itemKey.SpawnMethod} and SpawnMode = {itemKey.SpawnMode}");
                
                switch(itemKey.SpawnMethod)
                {
                    case BasisRuntimeSpawnRegistry.SpawnMethod.Local:
                    case BasisRuntimeSpawnRegistry.SpawnMethod.Embedded:

                        // if the item is local and embedded lets actually try get the gameobject first
                        if(BasisRuntimeSpawnRegistry.SpawnedGameobjects.TryGetValue(itemKey.LoadedNetID, out GameObject go) && go != null)
                        {
                            // if the gameobject is not null then lets remove its registery
                            bool success = await BasisRuntimeSpawnRegistry.RemoveByLoadedNetId( itemKey.LoadedNetID );
                            if(success)
                            {
                                // we should delete the embedded item
                                GameObject.Destroy(go);
                            }
                            else
                            {
                                BasisDebug.LogError($"failed to remove item = {instanceID} that has itemKey.SpawnMethod = {itemKey.SpawnMethod} from basis BasisRuntimeSpawnRegistry");
                            }

                        }
                        break;
                    case BasisRuntimeSpawnRegistry.SpawnMethod.Network:
                    switch (itemKey.SpawnMode)
                    {
                        case BasisRuntimeSpawnRegistry.SpawnMode.GameObject:
                            BasisNetworkSpawnItem.RequestGameObjectUnLoad(instanceID);
                            break;
                        case BasisRuntimeSpawnRegistry.SpawnMode.Scene:
                            BasisNetworkSpawnItem.RequestSceneUnLoad(instanceID);
                            break;
                        default:
                            BasisDebug.LogWarning($"Missing Spawn Method! {itemKey.SpawnMode}");
                            break;
                    }
                    break;
                }
                await RefreshCurrentTab();
            };
        }

        #endregion

    }
}
