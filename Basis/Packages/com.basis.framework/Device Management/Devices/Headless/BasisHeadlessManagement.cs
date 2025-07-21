using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices.Headless;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using System.Threading.Tasks;
using UnityEngine;
public class BasisHeadlessManagement : BasisBaseTypeManagement
{
    public BasisHeadlessInput BasisHeadlessInput;
    public override void BeginLoadSDK()
    {
        if (BasisHeadlessInput == null)
        {
            BasisDeviceManagement.Instance.SetCameraRenderState(false);
            BasisDeviceManagement.CurrentMode = "Headless";
            GameObject gameObject = new GameObject("Headless Eye");
            if (BasisLocalPlayer.Instance != null)
            {
                gameObject.transform.parent = BasisLocalPlayer.Instance.transform;
            }
            BasisHeadlessInput = gameObject.AddComponent<BasisHeadlessInput>();
            BasisHeadlessInput.Initialize("Desktop Eye", nameof(Basis.Scripts.Device_Management.Devices.Headless.BasisHeadlessInput));
            BasisDeviceManagement.Instance.TryAdd(BasisHeadlessInput);
        }
        BasisDebug.Log(nameof(BeginLoadSDK), BasisDebug.LogTag.Device);
        BasisLocalPlayer.Instance.DisplayName = "test client";
        BasisLocalPlayer.Instance.SetSafeDisplayname();
        if (BasisNetworkManagement.Instance != null)
        {
            ConnectToNetwork();
        }
        else
        {
            BasisNetworkManagement.OnEnableInstanceCreate += ConnectToNetwork;
        }
    }
    public async void ConnectToNetwork()
    {
        await CreateAssetBundle();
        // BasisNetworkManagement.Instance.Ip = IPaddress.text;
        // BasisNetworkManagement.Instance.Password = Password.text;
        BasisNetworkManagement.Instance.IsHostMode = false;
        //   ushort.TryParse(Port.text, out BasisNetworkManagement.Instance.Port);
        BasisNetworkManagement.Instance.Connect();
        BasisDebug.Log("connecting to default");
    }
    public async Task CreateAssetBundle()
    {
        if (BundledContentHolder.Instance.UseSceneProvidedHere)
        {
            BasisDebug.Log("using Local Asset Bundle or Addressable", BasisDebug.LogTag.Networking);
            if (BundledContentHolder.Instance.UseAddressablesToLoadScene)
            {
                await BasisSceneLoad.LoadSceneAddressables(BundledContentHolder.Instance.DefaultScene.BasisRemoteBundleEncrypted.RemoteBeeFileLocation);
            }
            else
            {
                await BasisSceneLoad.LoadSceneAssetBundle(BundledContentHolder.Instance.DefaultScene);
            }
        }
    }
    public override void StartSDK()
    {
        BasisDebug.Log(nameof(StartSDK), BasisDebug.LogTag.Device);
    }
    public override void StopSDK()
    {
        BasisDebug.Log(nameof(StopSDK), BasisDebug.LogTag.Device);
    }
    public override string Type()
    {
        return "Headless";
    }
}
