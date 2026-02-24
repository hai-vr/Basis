using Basis.BasisUI.Styling;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.UI.UI_Panels;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using Unity.Android.Gradle.Manifest;
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

        #region Provider Setup
        [RuntimeInitializeOnLoadMethod]
        public static void AddToMenu()
        {
            BasisMenuBase<BasisMainMenu>.AddProvider(new LibraryProvider());
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
            PanelTabGroup tabGroup = PanelTabGroup.CreateNew(panel.Descriptor.ContentParent, LayoutDirection.Horizontal);

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
            tabGroup.AddTab("Avatars",AddressableAssets.Sprites.Avatars, async () => await RefreshTabAsync(Page.Avatar), avatarsTab);
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
            tabGroup.AddExtraAction("Add New Content", PromptUserForNewContent, new Vector2( 70, 80 ));

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
        public static async Task<BasisLoadableBundleWrapper> CreateNewWrapperFromItem( BasisDataStoreItemKeys.ItemKey item )
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
            if(wrapper == null) // generate a new wrapper if its null
            {
                BasisDebug.LogWarning( "wrapper was not provided for LoadWrapperFromDisc, creating." );
                wrapper = await CreateNewWrapperFromItem( item );
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

        private static async Task BuildItemsListForInstantiatedObjects(ContentLoaderStore.LoadedItem[] loadedItems, PanelTabPage tab)
        {
            RectTransform container = tab.Descriptor.ContentParent;
        
            foreach (var entry in loadedItems)
            {
                int instanceId = entry.InstanceId;
                BasisDataStoreItemKeys.ItemKey itemKey = entry.ItemKey;
                GameObject go = entry.GameObject;

                if (go == null)
                    continue;

                CreateListEntry(itemKey, container, instanceId, go);
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
            if(TryConvert(page, out BundledContentHolder.Mode mode))
            {
                try
                {

                    // // // Only fetch keys matching the requested mode, so if we only want props only grab props returned in data
                    // // var data = BasisDataStoreItemKeys.DisplayKeys()
                    // //     .Where(k => k.Mode == mode)
                    // //     .ToList();
                    
                    // build data to be used
                    var data = BasisDataStoreItemKeys.DisplayKeys()
                        .Concat(EmbeddedItems.HardcodedKeys)
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
                            if (CachedMetaData.TryGetMeta(url, out var mm) && !string.IsNullOrEmpty(mm.Name) && mm.Name.IndexOf(_currentSearchQuery, StringComparison.InvariantCultureIgnoreCase) >= 0)
                                return true;

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
                                if (CachedMetaData.TryGetMeta(url, out var mm) && !string.IsNullOrEmpty(mm.Name))
                                    return mm.Name;
                                return url;
                            }).ToList();
                            break;
                        case LibraryDateSortMode.DateOldestToNewest:
                            data = data.OrderBy(k =>
                            {
                                var url = k.Url ?? string.Empty;
                                if (CachedMetaData.TryGetMeta(url, out var mm) && mm.Created.HasValue)
                                    return mm.Created.Value;
                                return DateTime.MaxValue;
                            }).ToList();
                            break;
                        case LibraryDateSortMode.DateNewestToOldest:
                            data = data.OrderByDescending(k =>
                            {
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

                BasisDebug.LogWarning("Normal Tab Stuff");
            }
            else
            {
                BasisDebug.LogWarning("Doing Instantiated Tab Stuff");

                // grab the data?
                ContentLoaderStore.LoadedItem[] loadedItems = await ContentLoaderStore.GetAll();

                // this is most likely to be the instantiated tab so
                ClearTabContent(tab.Descriptor.ContentParent);
                // TODO build list of instantiated objects
                await BuildItemsListForInstantiatedObjects(loadedItems, tab);
                tab.Descriptor.ForceRebuild();
            }

        }
        
        // used to refresh the current tab
        private static async Task RefreshCurrentTab()
        {
            await RefreshTabAsync(_currentPage);
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
        private static void UpdateInputFieldInteractability( PanelTextField URLTextField, PanelPasswordField PasswordTextField, DialogBox activeDialog )
        {
            URLTextField._inputField.interactable = !activeDialog.IsBusy;
            PasswordTextField._inputField.interactable = !activeDialog.IsBusy;
        }

        /// <summary>
        /// Invoked on the add new content is pressed in the library provider menu, to prompt the user to enter new content with a dialog box
        /// </summary>
        public static void PromptUserForNewContent()
        {
            // Build overlay using DialogBox helper
            DialogBox newItemDialogBox = DialogBox.Create(panel, new Vector2(930, 722),
                "Add New Content",
                "Please specify the type of content you are adding and then provide the URL and password for your BEE file. Once everything is set, confirm your choices to include the item in your library.",
                AddressableAssets.Sprites.Add);

            // the item type dropdown determines which library tab the new item will appear in.
            PanelDropdown contentTypeDropDown = PanelDropdown.CreateNew(PanelDropdown.DropdownStyles.OverlayEntry, newItemDialogBox.Descriptor);
            string[] modeNames = Enum.GetNames(typeof(BundledContentHolder.Mode));
            contentTypeDropDown.Descriptor.SetTitle("Content Type");
            contentTypeDropDown.Descriptor.SetIcon(AddressableAssets.Sprites.FileTray);
            contentTypeDropDown.Descriptor.SetDescription( "What content are you adding?" );
            contentTypeDropDown.AssignEntries(modeNames.ToList());
            
            // derive the default selected mode from the currently active tab, so if the user is browsing avatars and clicks "Add New CachedContent"
            contentTypeDropDown.SetValueWithoutNotify(_currentPage.ToString());
            contentTypeDropDown.Descriptor.SetHeight(50);
            contentTypeDropDown.Descriptor.SetWidth(900);

            // BEE file URL field
            PanelTextField URL = PanelTextField.CreateNew(TextFieldStyles.EntryVertical, newItemDialogBox.Descriptor);
            URL._placeholderLabel.text = "URL";
            URL._inputField.contentType = TMP_InputField.ContentType.Standard;
            URL.Descriptor.SetHeight(115);
            URL.Descriptor.SetWidth(900);
            URL.Descriptor.SetTitle("BEE File URL:");
            URL.Descriptor.SetIcon(AddressableAssets.Sprites.Network);
            URL.Descriptor.SetDescription("This should be a direct link to your BEE file.");

            PanelPasswordField Password = PanelPasswordField.CreateNew(PasswordFieldStyles.EntryVertical, newItemDialogBox.Descriptor);
            Password._placeholderField.text = "Enter password";
            Password.Descriptor.SetHeight(115);
            Password.Descriptor.SetWidth(900);

            Password.Descriptor.SetTitle("BEE File Password:");
            Password.Descriptor.SetIcon(AddressableAssets.Sprites.Unlocked);
            Password.Descriptor.SetDescription("This is the password that was generated with you BEE file.");

            // create a text field to show validation error messages, initially empty
            PanelTextField validationMessageField = PanelTextField.CreateNew(TextFieldStyles.EntryWarning, newItemDialogBox.Descriptor);
            validationMessageField.Descriptor.gameObject.SetActive(false);
            validationMessageField._inputField.gameObject.SetActive(false); // disable the text input field box
            validationMessageField.Descriptor.SetTitle("AWAITING_INPUT");
            validationMessageField.Descriptor.SetDescription("AWAITING_INPUT");
            validationMessageField.Descriptor.TitleLabel.color = Color.yellow;
            validationMessageField.Descriptor.DescriptionLabel.color = Color.yellow;

            validationMessageField.Descriptor.SetHeight(50);
            validationMessageField.Descriptor.SetWidth(900);

            // Add and Cancel buttons
            PanelTabGroup acceptOrDenyPanel = PanelTabGroup.CreateNew(newItemDialogBox.Descriptor, LayoutDirection.HorizontalNoBackground);

            acceptOrDenyPanel.Descriptor.SetHeight(50);
            acceptOrDenyPanel.Descriptor.SetWidth(900);

            PanelButton yesPanel = PanelButton.CreateNew(ButtonStyles.AcceptButton, acceptOrDenyPanel.TabButtonParent); //ButtonStyles.Cancel
            PanelButton noPanel = PanelButton.CreateNew(ButtonStyles.StandardButton, acceptOrDenyPanel.TabButtonParent);

            noPanel.Descriptor.SetTitle("Cancel");
            yesPanel.Descriptor.SetTitle("Add");

            noPanel.Descriptor.SetWidth(420);
            noPanel.Descriptor.SetHeight(60);
            yesPanel.Descriptor.SetWidth(420);
            yesPanel.Descriptor.SetHeight(60);

            // Cancel just closes.
            noPanel.OnClicked += async () =>
            {
                // just close the overlay instead.
                await newItemDialogBox.CloseAsync();
            };

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

                    BasisDebug.Log( $"given url {URL.Value}, given password {Password.Password}" );
                    BasisDebug.Log( $"processed url {validationResponse.ProcessedUrl} processed password {validationResponse.Password}" );

                    // get the result of the validationResponse
                    InputValidation.EntryValidationResult validationResult = validationResponse.Result;

                    // we now use the validation result to determine whether to proceed with adding the item or show an error message
                    switch(validationResult)
                    {
                        case InputValidation.EntryValidationResult.Success:
                            // if validation succeeded, proceed with adding the item
                            
                            if(validationMessageField.enabled)
                            {
                                validationMessageField.enabled = false; // hide any previous error message
                            }

                            // reset the fields
                            ChangeInputFieldStyle(URL._inputField.gameObject, false);
                            ChangeInputFieldStyle(Password._inputField.gameObject, false);

                            // perform a meta-only validation of the provided BEE file before adding the key
                            try
                            {
                                if(!validationMessageField.Descriptor.gameObject.activeSelf)
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

                                var tempWrapper = await CreateNewWrapperFromItem(tempItem);

                                BasisProgressReport Report = new BasisProgressReport();
                                CancellationTokenSource CancellationSource = new CancellationTokenSource();

                                // Attempt a meta-only load (this will download or read connector info and cache meta on disk)
                                bool isValid = await BasisBeeManagement.HandleMetaOnlyLoad(tempWrapper.basisTrackedBundleWrapper, Report, CancellationSource.Token);

                                if(isValid)
                                {
                                    // Attempt to read the metadata back from disk into the wrapper
                                    BasisLoadableBundleWrapper loaded = await LoadWrapperFromDisc(tempItem, tempWrapper);

                                    if(loaded.BasisLoadableBundle?.BasisBundleConnector?.MetaData != null)
                                    {
                                        if(loaded.BasisLoadableBundle.BasisBundleConnector.MetaData.ComponentNames != null)
                                        {
                                            BasisDebug.Log($"BasisComponentNames = {loaded.BasisLoadableBundle.BasisBundleConnector.MetaData.ComponentNames}");
                                            BasisDebug.Log($"BasisComponentNamesLength = {loaded.BasisLoadableBundle.BasisBundleConnector.MetaData.ComponentNames.Length}");

                                            // grab components
                                            foreach(BasisBundleConnector.BasisComponentName comp in loaded.BasisLoadableBundle.BasisBundleConnector.MetaData.ComponentNames)
                                            {
                                                BasisDebug.Log($"BasisComponentName = {comp.Name} count = {comp.count}");
                                            }
                                        }
                                        else
                                        {
                                            BasisDebug.LogError($"Warning BEE file from url = {tempItem.Url} does not contain metadata ComponentNames, consider updating it!");
                                        }
                                    }
                                    else
                                    {
                                        BasisDebug.LogError($"Warning BEE file from url = {tempItem.Url} does not contain metadata, consider updating it!");
                                    }

                                    // add the item to the basis key store
                                    await AddNewNewItemKey(contentTypeDropDown.SelectedString, validationResponse.ProcessedUrl, validationResponse.Password);
                                    // just close the overlay
                                    await newItemDialogBox.CloseAsync();
                                    // refresh the current tab
                                    await RefreshCurrentTab();
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

                                if(!validationMessageField.Descriptor.gameObject.activeSelf)
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

                    if(!validationMessageField.Descriptor.gameObject.activeSelf)
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
                catch(Exception ex)
                {
                    BasisDebug.LogError(ex);
                    newItemDialogBox.IsBusy = false;
                    // update interactability for fields based on dialog busy
                    UpdateInputFieldInteractability(URL, Password, newItemDialogBox);
                }
            };
        }

        /// <summary>
        /// Used with the add new item button to add a new item to the basis key store for items
        /// </summary>
        public static async Task AddNewNewItemKey(string Mode, string URL, string Password)
        {
            if (Enum.TryParse<BundledContentHolder.Mode>(Mode, out var mode))
            {
                var key = new BasisDataStoreItemKeys.ItemKey
                {
                    Pass = Password,
                    Url = URL,
                    Mode = mode,
                };

                await BasisDataStoreItemKeys.AddNewKey(key);
                if(mode == BundledContentHolder.Mode.Avatar)
                {
                    BasisLoadableBundle loadableBundle = new()
                    {
                        UnlockPassword = Password,
                        BasisRemoteBundleEncrypted = new BasisRemoteEncyptedBundle { RemoteBeeFileLocation = URL },
                        BasisBundleConnector = new BasisBundleConnector(),
                        BasisLocalEncryptedBundle = new BasisStoredEncryptedBundle()
                    };

                  await BasisLocalPlayer.Instance.CreateAvatar(BasisLocalPlayer.LoadModeNetworkDownloadable, loadableBundle);
                }
            }
            else
            {
                BasisDebug.LogError($"Failed to parse mode to BundledContentHolder.Mode Enum with string {Mode}, unable to add new item to the BasisDataStoreItemsKey.");
            }
        }

        #endregion

        #region CreateItemCard, ShowItemOverlay, ApplyMetaDataToButton

        /// <summary>
        /// The item card displayed all around the library menu
        /// </summary>
        private static async void CreateItemCard(BasisDataStoreItemKeys.ItemKey item, RectTransform container)
        {
            PanelButton buttonPanel = PanelButton.CreateNew(ButtonStyles.Prop, container);
            var urlKey = item.Url ?? string.Empty;
            var desc = buttonPanel.Descriptor;

            // Try get cached meta once
            CachedMetaData.CachedContent cachedMeta;
            CachedMetaData.TryGetMeta(urlKey, out cachedMeta);

            if(item.IsEmbedded)
            {

                // create an image for this card in top right with an offset of -35, -35
                PanelImage networkIcon = PanelImage.CreateNew(buttonPanel.Descriptor);
                networkIcon.SetIcon(AddressableAssets.GetSprite(AddressableAssets.Sprites.Locked), true);
                networkIcon.rectTransform.anchorMin = new Vector2(1, 1);
                networkIcon.rectTransform.anchorMax = new Vector2(1, 1);
                networkIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                networkIcon.rectTransform.anchoredPosition = new Vector2(-35, -35);
                networkIcon.rectTransform.sizeDelta = new Vector2(40, 40);

                desc.SetTitle(urlKey);
                desc.SetDescription(urlKey);
                desc.ForceRebuild();

                if(desc.IconBackground.TryGetComponent<Image>(out Image image))
                {
                    image.sprite = EmbeddedItems.GetSpriteForEmbeddedItem(item);
                }
            }
            else
            {

                if (cachedMeta != null)
                {
                    BasisDebug.Log($"ApplyMetaDataToButton -> for item {urlKey}");
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

            // TODO: reuse for platform icons?
            // if(item.NetworkType == BundledContentHolder.NetworkType.Networked)
            // {
            //     // create an image for the button network icon in the top right with an offset of -35, -35
            //     PanelImage networkIcon = PanelImage.CreateNew(buttonPanel.Descriptor);
            //     networkIcon.SetIcon(AddressableAssets.GetSprite(AddressableAssets.Sprites.Network), true);
            //     networkIcon.rectTransform.anchorMin = new Vector2(1, 1);
            //     networkIcon.rectTransform.anchorMax = new Vector2(1, 1);
            //     networkIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            //     networkIcon.rectTransform.anchoredPosition = new Vector2(-35, -35);
            //     networkIcon.rectTransform.sizeDelta = new Vector2(40, 40);
            // }

            buttonPanel.OnClicked += async () =>
            {
                try
                {
                    await ShowItemOverlay(item);
                }
                catch (Exception ex)
                {
                    BasisDebug.LogError(ex);
                }
            };
        }

        private static BasisDataStoreItemKeys.ItemKey _activeItem;

        private static string ConvertItemKeyToAddressableSprite( BasisDataStoreItemKeys.ItemKey item )
        {
            switch(item.Mode)
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

        public static async Task ShowItemOverlay(BasisDataStoreItemKeys.ItemKey item)
        {
            // grab the content from the cache 
            CachedMetaData.CachedContent metadata;
            CachedMetaData.TryGetMeta(item.Url, out metadata);

            // the network type of the item
            BundledContentHolder.NetworkType desiredNetworkType = BundledContentHolder.NetworkType.Local;

            // the persistence behavior of the item 
            bool ephemeral = true; 

            // grab the meta data
            BasisBundleConnector.BasisMetaData basisMetaData;

            // grab the description data
            BasisBundleDescription description;

            // get the creation date of the basis bundle
            string creationDate = string.Empty;

            // target sprite
            Sprite targetSprite = null;

            // platforms

            string[] platforms = new string[0];

            if(item.IsEmbedded)
            {
                description = new BasisBundleDescription(){
                    AssetBundleName = item.Url,
                    AssetBundleDescription = "Emebbed item",
                };

                targetSprite = EmbeddedItems.GetSpriteForEmbeddedItem(item);
            }
            else
            {
                // grab BEE file information
                basisMetaData = metadata.BasisBundleConnector.MetaData;
                description = metadata.BasisBundleConnector.BasisBundleDescription;
                creationDate = metadata.BasisBundleConnector.DateOfCreation;
                targetSprite = CachedMetaData.CreateSpriteFromMetaData(metadata);

                platforms = metadata.BasisBundleConnector.BasisBundleGenerated.Select(pair => pair.Platform).ToArray();
            }

            // // TODO: actually validate the BEE file upon adding it rather than checking description for it to actually exists?

            // if (description == null)
            // {
            //     BasisDebug.LogError($"Bundle Description on AvatarMenuItem {item} not found, auto removing.");
                
            //     // TODO: Remove this once input validation is in place to prevent invalid entries from being added. This is to ensure a clean user experience in the meantime.
            //     // temp will remove invalid entries that failed to get meta data.
            //     await BasisDataStoreItemKeys.RemoveKey(item);

            //     // refresh the current tab for any new changes
            //     await RefreshCurrentTab();
            //     return;
            // }

            // Not sure why we need this so lets to remove.
            _activeItem = item;

            // Build overlay using DialogBox helper
            DialogBox existingItemDialog = DialogBox.Create(panel, new Vector2(930, 722),
                $"{description.AssetBundleName}",
                $"{(description.AssetBundleDescription.Length > 0 ? description.AssetBundleDescription : "N/A")}",
                ConvertItemKeyToAddressableSprite(item));

            // create the exit button for the dialog box
            var button = PanelButton.CreateNew(ButtonStyles.ExitButtonOverlay, existingItemDialog.Descriptor.Header);
            button.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 125);
            button.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 50);
            button.OnClicked += async () => await existingItemDialog.CloseAsync();

            // icon for the selected item
            var itemIcon = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.GroupLargeIconVertical, existingItemDialog.Descriptor);

            itemIcon.SetIcon(targetSprite);

            // info about the item
            PanelTabGroup itemMetaDataPanel = PanelTabGroup.CreateNew(PanelTabGroup.TabGroupStyles.VerticalStackedNoBackground, itemIcon.ContentParent);
            // advancedActionsPanel.Descriptor.SetHeight(160);
            // advancedActionsPanel.Descriptor.SetWidth(900);

            #region CREATION DATE

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

            // creation date and time
            PanelTextField createdInformationTextField = PanelTextField.CreateNew(TextFieldStyles.Entry, itemMetaDataPanel.TabButtonParent);
            createdInformationTextField._inputField.gameObject.SetActive(false); // disable the text input field box
            createdInformationTextField.Descriptor.SetTitle("Creation Date");
            createdInformationTextField.Descriptor.SetIcon(AddressableAssets.Sprites.Clock);
            createdInformationTextField.Descriptor.SetDescription($"{creationDate}");

            createdInformationTextField.Descriptor.SetHeight(50);
            createdInformationTextField.Descriptor.SetWidth(400);

            #endregion

            #region PLATFORM ICONS

            // create a text field to show validation error messages, initially empty
            PanelTabGroup platformIconsPanel = PanelTabGroup.CreateNew(PanelTabGroup.TabGroupStyles.HorizontalStackedNoBackground, itemMetaDataPanel.TabButtonParent);
            //validationMessageField.Descriptor.gameObject.SetActive(false);
            //platformIconsPanel._inputField.gameObject.SetActive(false); // disable the text input field box
            //platformIconsPanel.Descriptor.SetTitle("PLATFORM ICONS");
            //platformIconsPanel.Descriptor.SetDescription("PLATFORM ICONS DESC");
            // validationMessageField.Descriptor.TitleLabel.color = Color.yellow;
            // validationMessageField.Descriptor.DescriptionLabel.color = Color.yellow;

            platformIconsPanel.Descriptor.SetHeight(50);
            platformIconsPanel.Descriptor.SetWidth(400);
            
            BasisDebug.Log($"item {item.Url} has platforms supported {platforms} {platforms.Length}");

            foreach (string platform in platforms)
            {
                string address = null;

                switch (platform)
                {
                    case "StandaloneWindows64":
                        address = "Packages/com.basis.sdk/Prefabs/Panel Elements/Platform Panel - Windows.prefab";
                        break;

                    case "StandaloneOSX":
                        address = "Packages/com.basis.sdk/Prefabs/Panel Elements/Platform Panel - Mac.prefab";
                        break;

                    case "StandaloneLinux64":
                        address = "Packages/com.basis.sdk/Prefabs/Panel Elements/Platform Panel - Linux.prefab";
                        break;

                    case "Android":
                        address = "Packages/com.basis.sdk/Prefabs/Panel Elements/Platform Panel - Android.prefab";
                        break;

                    case "iOS":
                        address = "Packages/com.basis.sdk/Prefabs/Panel Elements/Platform Panel - iOS.prefab";
                        break;
                }

                if (string.IsNullOrEmpty(address))
                {
                    continue;
                }

                var handle = Addressables.LoadAssetAsync<GameObject>(address);
                var prefab = await handle.Task;

                GameObject.Instantiate(prefab, platformIconsPanel.TabButtonParent.transform);
            }

            #endregion

            #region ITEM DISPLAY LOGIC SPECIFIC TO BundledContentHolder.Mode

            switch(item.Mode)
            {
                case BundledContentHolder.Mode.Avatar:
                    break;
                case BundledContentHolder.Mode.Prop:

                    // Advanced Settings
                    PanelTabGroup advancedActionsPanel = PanelTabGroup.CreateNew(PanelTabGroup.TabGroupStyles.VerticalStackedNoBackground, existingItemDialog.Descriptor);
                    advancedActionsPanel.Descriptor.SetHeight(160);
                    advancedActionsPanel.Descriptor.SetWidth(900);

                    // PanelTabPage tab = PanelTabPage.CreateGrid(advancedActionsPanel.Descriptor.ContentParent);
                    // tab.rectTransform.offsetMin = new Vector2(0, 0);

                    // content sync mode dropdown determines whether the new item is flagged as networked or local, which affects filtering and how the item is loaded later
                    PanelDropdown contentSyncModeDropDown = PanelDropdown.CreateNew(PanelDropdown.DropdownStyles.OverlayEntry, advancedActionsPanel.TabButtonParent);
                    string[] contentSyncModes = Enum.GetNames(typeof(BundledContentHolder.NetworkType));
                    contentSyncModeDropDown.Descriptor.SetTitle("Network Type");
                    contentSyncModeDropDown.Descriptor.SetDescription("Determines visibility.");
                    contentSyncModeDropDown.Descriptor.SetIcon(AddressableAssets.Sprites.Network);
                    contentSyncModeDropDown.AssignEntries(contentSyncModes.ToList());
                    contentSyncModeDropDown.Descriptor.SetSize(new Vector2(700, 80));

                    // DISABLE THIS DROPDOWN IF EMBEDED ITEM
                    if(contentSyncModeDropDown.Descriptor.gameObject.TryGetComponent<PanelDropdown>(out PanelDropdown dropdown))
                    {
                        if(dropdown.DropdownComponent != null)
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
                    contentPersistenceToggle.Descriptor.SetDescription("If enabled, this item will only be visible to people currently in the instance. Late joiners wont be able to see this.");
                    contentPersistenceToggle.Descriptor.SetSize(new Vector2(700, 80));
                    contentPersistenceToggle.OnValueChanged = (val) =>
                    {
                        ephemeral = val;
                    };

                    // DISABLE THIS TOGGLE IF THE ITEM IS EMBEDDED
                    if(contentPersistenceToggle.Descriptor.gameObject.TryGetComponent<Toggle>(out Toggle toggle))
                    {
                        // if the item is embedded dont interact
                        toggle.interactable = !item.IsEmbedded;
                    }

                    break;
                case BundledContentHolder.Mode.World:
                    break;
                default:
                    BasisDebug.Log( $"Unknown item.Mode {item.Mode} for item {item.Url}, unable to determine ShowItemOverlay layout" );
                    break;

            }

            #endregion


            // Delete & Load Buttons
            PanelTabGroup actionsPanel = PanelTabGroup.CreateNew(existingItemDialog.Descriptor, LayoutDirection.HorizontalNoBackground);

            actionsPanel.Descriptor.SetHeight(50);
            actionsPanel.Descriptor.SetWidth(900);

            PanelButton deletePanelButton = PanelButton.CreateNew(ButtonStyles.CancelButton, actionsPanel.TabButtonParent); //ButtonStyles.Cancel
            deletePanelButton.Descriptor.SetTitle("Delete");
            deletePanelButton.Descriptor.SetWidth(220);
            deletePanelButton.Descriptor.SetHeight(60);

            // upon delete we do these actions
            deletePanelButton.OnClicked += async () =>
            {
                if (existingItemDialog.IsBusy) return;
                existingItemDialog.IsBusy = true;

                // remove the item
                await BasisDataStoreItemKeys.RemoveKey(item);
                // just close the overlay instead.
                await existingItemDialog.CloseAsync();
                // refresh current tab
                await RefreshCurrentTab();
            };

            // DISABLE THIS BUTTON IF ITEM IS EMBEDDED
            if(deletePanelButton.Descriptor.gameObject.TryGetComponent<Button>(out Button deleteButtonComponent))
            {
                // if the item is embedded dont interact
                deleteButtonComponent.interactable = !item.IsEmbedded;
            }

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
                    await existingItemDialog.CloseAsync();
                }

            };

            // string creationDate = bundle.BasisBundleConnector.DateOfCreation;
            // if (string.IsNullOrEmpty(creationDate))
            // {
            //     creationDate = string.Empty;
            // }
            // else
            // {
            //     creationDate = DateTime
            //         .Parse(creationDate, CultureInfo.InvariantCulture,
            //                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            //         .ToString(CultureInfo.InvariantCulture);

            //     creationDate += " UTC";
            // }

            // // Wrapper
            // var Descriptor = PanelElementDescriptor.CreateNew(
            //     PanelElementDescriptor.ElementStyles.GroupLargeIcon, _descriptor);

            // Descriptor.SetIcon(Sprite);
            // Descriptor.SetTitle(description.AssetBundleDescription);

            // PanelTabGroup actionsSupportedPlatforms =  PanelTabGroup.CreateNew(_descriptor, LayoutDirection.HorizontalNoBackground);
            // if (actionsSupportedPlatforms.TryGetComponent<LayoutElement>(out LayoutElement LayoutElement))
            // {
            //     LayoutElement.minHeight = 50;
            // }

            // Descriptor.SetDescription($"\nCreated: {creationDate}");

            // var IDField = PanelPasswordField.CreateNew(PasswordFieldStyles.Entry, _descriptor);
            // IDField._placeholderField.text = "";//Wrapper
            // IDField.SetPassword(bundle.BasisBundleConnector.UniqueVersion);
            // IDField._inputField.interactable = false;
            // IDField.Descriptor.SetTitle("URL:");
            // IDField.LayoutElement.minWidth = 500;

            // var urlField = PanelPasswordField.CreateNew(PasswordFieldStyles.Entry, _descriptor);
            // urlField._placeholderField.text = "";
            // urlField.SetPassword(item.Url);
            // urlField._inputField.interactable = false;
            // urlField.Descriptor.SetTitle("URL:");
            // urlField.LayoutElement.minWidth = 500;

            // var passField = PanelPasswordField.CreateNew(PasswordFieldStyles.Entry, _descriptor);
            // passField._placeholderField.text = "";
            // passField.SetPassword(item.Pass); // if supported
            // passField._inputField.interactable = false;
            // passField.Descriptor.SetTitle("Password:");
            // passField.LayoutElement.minWidth = 500;

            // // Buttons row
            // PanelTabGroup actions = PanelTabGroup.CreateNew(_descriptor, LayoutDirection.HorizontalNoBackground);

            // PanelButton DeleteBtn = PanelButton.CreateNew(ButtonStyles.CancelButton, actions.TabButtonParent);
            // PanelButton loadBtn = PanelButton.CreateNew(ButtonStyles.AcceptButton, actions.TabButtonParent);

            // DeleteBtn.Descriptor.SetTitle("Delete");
            // loadBtn.Descriptor.SetTitle("Load");

            // DeleteBtn.SetSize(new Vector2(200, 60));
            // loadBtn.SetSize(new Vector2(530, 60));

            // DeleteBtn.OnClicked += async () =>
            // {
            //     await BasisDataStoreItemKeys.RemoveKey(item);
            //     await CloseOverlay();
            // };

            // loadBtn.OnClicked += async () =>
            // {
            //     if (_isSubmitting) return;
            //     _isSubmitting = true;

            //     try
            //     {
            //         BasisDebug.Log($"Load Button Clicked for item: {item.Url}");
            //         await LoadSelectedItem(item);
            //     }
            //     catch (Exception ex)
            //     {
            //         BasisDebug.LogError(ex);
            //     }
            //     finally
            //     {
            //         _isSubmitting = false;
            //         await CloseOverlay();
            //     }
            // };
        }

        private static void ApplyMetaDataToButton(PanelButton buttonPanel, CachedMetaData.CachedContent cachedMeta, string urlKey)
        {
            Sprite iconSprite = CachedMetaData.CreateSpriteFromMetaData(cachedMeta);

            buttonPanel.SetIcon(iconSprite, false);

            var desc = buttonPanel.Descriptor;
            desc.SetTitle(!string.IsNullOrEmpty(cachedMeta.Name) ? cachedMeta.Name : urlKey);
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
        private static async Task LoadSelectedItem(BasisDataStoreItemKeys.ItemKey item, BundledContentHolder.NetworkType networkType = BundledContentHolder.NetworkType.Local, bool persistence = false, bool modifyScale = false)
        {
            // At this point the item should be fully loaded and ready to use. What happens next is up to you and your application needs.
            // For example, you could raise an event that other parts of your app listen for, or directly instantiate the loaded content if it's a prefab.
            BasisDebug.Log($"Attempting to load selected item: {item.Url} item type {item.Mode} with network type {networkType} persistent = {persistence} modifyScale = {modifyScale}");

            try
            {
                switch(item.Mode)
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
        private static async void CreateListEntry(BasisDataStoreItemKeys.ItemKey itemKey, RectTransform parentTabGroup, int instanceID, GameObject gameObject)
        {
            // // icon for the selected item
            // var itemIcon = PanelElementDescriptor.CreateNew(
            //     PanelElementDescriptor.ElementStyles.GroupLargeIconVertical, parentTabGroup);

            // createdInformationTextField.Descriptor.SetTitle(item.Url);
            // createdInformationTextField.Descriptor.SetDescription($"Embedded item");
            // itemIcon.SetIcon(EmbeddedItems.GetSpriteForEmbeddedItem(item));

            BasisDebug.Log($"creating list entry for item url = {itemKey.Url} with instanceID = {instanceID}");

            PanelTabGroup itemListPanel = PanelTabGroup.CreateNew(PanelTabGroup.TabGroupStyles.HorizontalStackedNoBackground, parentTabGroup);
            itemListPanel.Descriptor.SetWidth( 1400 );
            itemListPanel.Descriptor.SetHeight( 80 );

            // simple info
            PanelTextField itemTextInfo = PanelTextField.CreateNew(TextFieldStyles.Entry, itemListPanel.TabButtonParent);
            itemTextInfo._inputField.gameObject.SetActive(false); // disable the text input field box
            itemTextInfo.Descriptor.SetTitle(itemKey.Url);
            //createdInformationTextField.Descriptor.SetIcon(EmbeddedItems.GetSpriteForEmbeddedItem(item));
            itemTextInfo.Descriptor.SetDescription($"Embedded item");

            itemTextInfo.Descriptor.SetHeight(50);
            itemTextInfo.Descriptor.SetWidth(400);


            PanelButton removeItem = PanelButton.CreateNew(ButtonStyles.CancelButton, itemListPanel.TabButtonParent);
            removeItem.Descriptor.SetTitle("Remove");
            removeItem.SetSize(new Vector2(200, 60));
            removeItem.OnClicked += async () =>
            {
                if(gameObject != null)
                {
                    GameObject.Destroy(gameObject);
                }

                // remove the item from the list
                await ContentLoaderStore.Remove(instanceID);

                await RefreshCurrentTab();
            };
            

            
        }

        #endregion

    }
}
