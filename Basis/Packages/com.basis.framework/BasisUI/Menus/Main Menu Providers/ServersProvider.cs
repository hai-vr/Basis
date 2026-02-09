using System;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using UnityEngine;

namespace Basis.BasisUI
{
    public class ServersProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        [RuntimeInitializeOnLoadMethod]
        public static void AddToMenu()
        {
            BasisMenuBase<BasisMainMenu>.AddProvider(new ServersProvider());
        }
        public static string TitleStatic = "Servers";
        public override string Title => TitleStatic;
        public override string IconAddress => AddressableAssets.Sprites.Servers;
        public override int Order => 1;

        public override void RunAction()
        {
            if (BasisMainMenu.ActiveMenuTitle == Title)
            {
                BasisMainMenu.Instance.ActiveMenu.ReleaseInstance();
                return;
            }

            BasisMenuPanel panel = BasisMainMenu.CreateActiveMenu(
                new BasisMenuPanel.PanelData
                {
                    Title = this.Title,
                    PanelSize = new Vector2(600, 900),
                    PanelPosition = default
                },
                BasisMenuPanel.PanelStyles.Page);
            BoundButton?.BindActiveStateToAddressablesInstance(panel);

            RectTransform container = panel.Descriptor.ContentParent;

            PanelElementDescriptor layout =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.ScrollViewVertical, container);
            container = layout.ContentParent;

            usernameField = PanelTextField.CreateNewEntry(container);
            usernameField.Descriptor.SetTitle("Username");
            usernameField.SetValueWithoutNotify(BasisDataStore.LoadString(LoadFileName, string.Empty));

            ipAddressField = PanelTextField.CreateNewEntry(container);
            ipAddressField.Descriptor.SetTitle("IP Address");



            useLocalhost = PanelButton.CreateNew(container);
            useLocalhost.Descriptor.SetTitle("Use \"Localhost\"");
            useLocalhost.OnClicked += () => ipAddressField.SetValueWithoutNotify("localhost");

            portField = PanelTextField.CreateNewEntry(container);
            portField.Descriptor.SetTitle("Port");

            passwordField = PanelPasswordField.CreateNewEntry(container);
            passwordField.Descriptor.SetTitle("Password");
            passwordField.SetPassword("default_password");

            hostModeToggle = PanelToggle.CreateNewEntry(container);
            hostModeToggle.Descriptor.SetTitle("Host Mode");
            hostModeToggle.OnValueChanged += UseHostMode;

            connectButton = PanelButton.CreateNew(container);
            connectButton.Descriptor.SetTitle("Connect");
            connectButton.Descriptor.SetHeight(80);
            connectButton.OnClicked += () => _ = HasUserName();

            ShowAdvancedSettings = PanelButton.CreateNew(container);

            ShowAdvancedSettings.Descriptor.SetTitle("Show Advanced Settings");
            ShowAdvancedSettings.OnClicked += ShowAdvancedOptions;

            portField.gameObject.SetActive(false);
            useLocalhost.gameObject.SetActive(false);
            passwordField.gameObject.SetActive(false);
            hostModeToggle.gameObject.SetActive(false);
            ipAddressField.gameObject.SetActive(false);
            if (BasisNetworkManagement.Instance)
            {
                LoadCurrentSettings();
            }
            else
            {
                BasisNetworkManagement.OnIstanceCreated += LoadCurrentSettings;
            }
        }
        public void ShowAdvancedOptions()
        {

            bool State = useLocalhost.gameObject.activeSelf;
            bool Opposite = !State;

            if(Opposite)
            {
                ShowAdvancedSettings.Descriptor.SetTitle("Hide Advanced Settings");
            }
            else
            {
                ShowAdvancedSettings.Descriptor.SetTitle("Show Advanced Settings");
            }
            useLocalhost.gameObject.SetActive(Opposite);
            passwordField.gameObject.SetActive(Opposite);
            hostModeToggle.gameObject.SetActive(Opposite);
            portField.gameObject.SetActive(Opposite);
            ipAddressField.gameObject.SetActive(Opposite);
        }
        private PanelButton ShowAdvancedSettings;
        private PanelTextField usernameField;
        private PanelTextField ipAddressField;
        private PanelButton useLocalhost;
        private PanelTextField portField;
        private PanelPasswordField passwordField;
        private PanelToggle hostModeToggle;
        private PanelButton connectButton;

        public static string LoadFileName = "CachedUserName.BAS";

        public void LoadCurrentSettings()
        {
            ipAddressField.SetValueWithoutNotify(BasisNetworkManagement.Instance.Ip);
            portField.SetValueWithoutNotify(BasisNetworkManagement.Instance.Port.ToString());
            passwordField.SetPassword(BasisNetworkManagement.Instance.Password);
            hostModeToggle.SetValueWithoutNotify(BasisNetworkManagement.Instance.IsHostMode);
            UseHostMode(hostModeToggle.Value);
        }

        public async Task HasUserName()
        {
            // Set button to non-interactable immediately after clicking
            connectButton.ButtonComponent.interactable = false;

            if (!string.IsNullOrEmpty(usernameField.Value))
            {
                if (BasisNetworkConnection.LocalPlayerIsConnected)
                {
                    await BasisNetworkLifeCycle.Destroy(BasisNetworkManagement.Instance);
                    BasisNetworkLifeCycle.Initalize(BasisNetworkManagement.Instance);
                }

                BasisLocalPlayer.Instance.DisplayName = usernameField.Value;
                BasisLocalPlayer.Instance.SetSafeDisplayname();
                BasisDataStore.SaveString(BasisLocalPlayer.Instance.DisplayName, LoadFileName);
                if (BasisNetworkManagement.Instance)
                {
                    await CreateAssetBundle();
                    BasisNetworkManagement.Instance.Ip = ipAddressField.Value;
                    BasisNetworkManagement.Instance.Password = passwordField.Password;
                    BasisNetworkManagement.Instance.IsHostMode = hostModeToggle.Value;
                    ushort.TryParse(portField.Value, out BasisNetworkManagement.Instance.Port);
                    BasisNetworkManagement.Instance.Connect();
                    BasisMainMenu.Close();
                }
            }
            else
            {
                BasisDebug.LogError("Name was empty, bailing");
                // Re-enable button interaction if username is empty
                connectButton.ButtonComponent.interactable = true;
            }
        }

        public async Task CreateAssetBundle()
        {
            if (BundledContentHolder.Instance.UseSceneProvidedHere)
            {
                BasisDebug.Log("using Local Asset Bundle or Addressable", BasisDebug.LogTag.Networking);
                if (BundledContentHolder.Instance.UseAddressablesToLoadScene)
                {
                    await BasisSceneLoad.LoadSceneAddressables(
                        BundledContentHolder.Instance.DefaultScene
                        .BasisRemoteBundleEncrypted.RemoteBeeFileLocation);
                }
                else
                {
                    await BasisSceneLoad.LoadSceneAssetBundle(BundledContentHolder.Instance.DefaultScene);
                }
            }
        }

        public void UseHostMode(bool value)
        {
            connectButton.Descriptor.SetTitle(value ? "Host" : "Connect");
        }
    }
}
