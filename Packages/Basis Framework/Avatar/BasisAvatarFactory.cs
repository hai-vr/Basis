using Basis.Scripts.Addressable_Driver;
using Basis.Scripts.Addressable_Driver.Factory;
using Basis.Scripts.Addressable_Driver.Resource;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace Basis.Scripts.Avatar
{
    public static class BasisAvatarFactory
    {
      public static BasisLoadableBundle LoadingAvatar = new BasisLoadableBundle()
        {
            BasisBundleInformation = new BasisBundleInformation()
            {
                BasisBundleDescription = new BasisBundleDescription()
                {
                    AssetBundleDescription = "LoadingAvatar",
                    AssetBundleName = "LoadingAvatar"
                },
                HasError = false,
                BasisBundleGenerated = new BasisBundleGenerated()
                {
                    AssetBundleCRC = 0,
                    AssetBundleHash = "N/A",
                    AssetMode = "Gameobject"
                },
            },
            UnlockPassword = "N/A",
            BasisRemoteBundleEncrypted = new BasisRemoteEncyptedBundle()
            {
                BundleURL = "LoadingAvatar",
                IsLocal = true,
                MetaURL = "LoadingAvatar",
            },
            BasisLocalEncryptedBundle = new BasisStoredEncyptedBundle()
            {
                LocalBundleFile = "LoadingAvatar",
                LocalMetaFile = "LoadingAvatar",
            },
        };
        public static async Task LoadAvatarLocal(BasisLocalPlayer Player,byte Mode, BasisLoadableBundle BasisLoadableBundle)
        {
            if (string.IsNullOrEmpty(BasisLoadableBundle.BasisRemoteBundleEncrypted.BundleURL))
            {
                Debug.LogError("Avatar Address was empty or null! Falling back to loading avatar.");
                await LoadAvatarAfterError(Player);
                return;
            }

            DeleteLastAvatar(Player, false);
            LoadLoadingAvatar(Player, LoadingAvatar.BasisLocalEncryptedBundle.LocalBundleFile);

            try
            {
                GameObject Output = null;
                switch (Mode)
                {
                    case 0://download
                        Debug.Log("Requested Avatar was a AssetBundle Avatar " + BasisLoadableBundle.BasisRemoteBundleEncrypted.BundleURL);
                        Output = await DownloadAndLoadAvatar(BasisLoadableBundle, Player);
                        break;
                    case 1://localload
                        Debug.Log("Requested Avatar was a Addressable Avatar " + BasisLoadableBundle.BasisRemoteBundleEncrypted.BundleURL);
                        var Para = new UnityEngine.ResourceManagement.ResourceProviders.InstantiationParameters(Player.transform.position, Quaternion.identity, null);
                        ChecksRequired Required = new ChecksRequired
                        {
                            UseContentRemoval = true,
                            DisableAnimatorEvents = false
                        };
                        (List<GameObject> GameObjects, AddressableGenericResource resource) = await AddressableResourceProcess.LoadAsGameObjectsAsync(BasisLoadableBundle.BasisRemoteBundleEncrypted.BundleURL, Para, Required);

                        if (GameObjects.Count > 0)
                        {
                            Debug.Log("Found Avatar for " + BasisLoadableBundle.BasisRemoteBundleEncrypted.BundleURL);
                            Output = GameObjects[0];
                        }
                        else
                        {
                            Debug.LogError("Cant Find Local Avatar for " + BasisLoadableBundle.BasisRemoteBundleEncrypted.BundleURL);
                        }
                        break;
                    default:
                        Debug.Log("Using Default, this means index was out of acceptable range! " + BasisLoadableBundle.BasisRemoteBundleEncrypted.BundleURL);
                        Output = await DownloadAndLoadAvatar(BasisLoadableBundle, Player);
                        break;
                }
                Player.AvatarMetaData =  BasisBundleConversionNetwork.ConvertFromNetwork(new AvatarNetworkLoadInformation() { AvatarBundleUrl = BasisLoadableBundle.BasisRemoteBundleEncrypted.BundleURL, AvatarMetaUrl = BasisLoadableBundle.BasisRemoteBundleEncrypted.MetaURL, UnlockPassword = BasisLoadableBundle.UnlockPassword });
                Player.AvatarLoadMode = Mode;

                InitializePlayerAvatar(Player, Output);
                BasisHeightDriver.SetPlayersEyeHeight(Player);
                Player.AvatarSwitched();
            }
            catch (Exception e)
            {
                Debug.LogError($"Loading avatar failed: {e}");
                await LoadAvatarAfterError(Player);
            }
        }

        public static async Task LoadAvatarRemote(BasisRemotePlayer Player,byte Mode, BasisLoadableBundle BasisLoadableBundle)
        {
            if (string.IsNullOrEmpty(BasisLoadableBundle.BasisRemoteBundleEncrypted.BundleURL))
            {
                Debug.LogError("Avatar Address was empty or null! Falling back to loading avatar.");
                await LoadAvatarAfterError(Player);
                return;
            }

            DeleteLastAvatar(Player,false);
            LoadLoadingAvatar(Player, LoadingAvatar.BasisLocalEncryptedBundle.LocalBundleFile);

            try
            {
                GameObject Output = null;
                switch (Mode)
                {
                    case 0://download
                        Output = await DownloadAndLoadAvatar(BasisLoadableBundle, Player);
                        break;
                    case 1://localload
                        Debug.Log("Requested Avatar was a Addressable Avatar " + BasisLoadableBundle.BasisRemoteBundleEncrypted.BundleURL);
                        ChecksRequired Required = new ChecksRequired
                        {
                            UseContentRemoval = false,
                            DisableAnimatorEvents = false
                        };
                        var Para = new UnityEngine.ResourceManagement.ResourceProviders.InstantiationParameters(Player.transform.position, Quaternion.identity, null);
                        (List<GameObject> GameObjects, AddressableGenericResource resource) = await AddressableResourceProcess.LoadAsGameObjectsAsync(BasisLoadableBundle.BasisRemoteBundleEncrypted.BundleURL, Para, Required);

                        if (GameObjects.Count > 0)
                        {
                            Debug.Log("Found Avatar for " + BasisLoadableBundle.BasisRemoteBundleEncrypted.BundleURL);
                            Output = GameObjects[0];
                        }
                        else
                        {
                            Debug.LogError("Cant Find Local Avatar for " + BasisLoadableBundle.BasisRemoteBundleEncrypted.BundleURL);
                        }
                        break;
                    default:
                        Output = await DownloadAndLoadAvatar(BasisLoadableBundle, Player);
                        break;
                }
                Player.AvatarMetaData = BasisBundleConversionNetwork.ConvertFromNetwork(new AvatarNetworkLoadInformation() { AvatarBundleUrl = BasisLoadableBundle.BasisRemoteBundleEncrypted.BundleURL, AvatarMetaUrl = BasisLoadableBundle.BasisRemoteBundleEncrypted.MetaURL, UnlockPassword = BasisLoadableBundle.UnlockPassword });
                Player.AvatarLoadMode = Mode;

                InitializePlayerAvatar(Player, Output);
                Player.AvatarSwitched();
            }
            catch (Exception e)
            {
                Debug.LogError($"Loading avatar failed: {e}");
                await LoadAvatarAfterError(Player);
            }
        }
        public static async Task<GameObject> DownloadAndLoadAvatar(BasisLoadableBundle BasisLoadableBundle, BasisPlayer BasisPlayer)
        {
            GameObject Output = await BasisLoadHandler.LoadGameObjectBundle(BasisLoadableBundle, true, BasisPlayer.ProgressReportAvatarLoad, new CancellationToken(), BasisPlayer.transform.position, Quaternion.identity);
            BasisPlayer.ProgressReportAvatarLoad.ReportProgress(100, "Setting Position");
            Output.transform.SetPositionAndRotation(BasisPlayer.transform.position, Quaternion.identity);
            return Output;
        }
        private static void InitializePlayerAvatar(BasisPlayer Player, GameObject Output)
        {
            bool isLocal = Player is BasisLocalPlayer;
            List<BasisConditionalLoad> conditionalLoadComps = new List<BasisConditionalLoad>();
            Output.GetComponentsInChildren(true, conditionalLoadComps);
            foreach (var conditionalLoadComp in conditionalLoadComps)
            {
                // null-Destroy check: Someone might have put multiple BasisConditionalLoad in the same object hierarchy
                if (conditionalLoadComp)
                {
                    bool preserve = conditionalLoadComp.useNetworkCondition && (
                        isLocal && (conditionalLoadComp.networkAllow & BasisConditionalLoadNetwork.Local) != 0
                        || !isLocal && (conditionalLoadComp.networkAllow & BasisConditionalLoadNetwork.Remote) != 0
                    );
                    if (!preserve)
                    {
                        Object.Destroy(conditionalLoadComp.gameObject);
                        // Note: Even if an object is local-only, content policing must be executed for the safety of the local user.
                    }
                }
            }
            
            if (Output.TryGetComponent(out BasisAvatar Avatar))
            {
                DeleteLastAvatar(Player, true);
                Player.Avatar = Avatar;
                Player.Avatar.Renders = Player.Avatar.GetComponentsInChildren<Renderer>(true);
                if (Player is BasisLocalPlayer localPlayer)
                {
                    Player.Avatar.IsOwnedLocally = true;
                    CreateLocal(localPlayer);
                    localPlayer.InitalizeIKCalibration(localPlayer.AvatarDriver);
                    Avatar.OnAvatarReady?.Invoke(true);
                    for (int Index = 0; Index < Avatar.Renders.Length; Index++)
                    {
                        Avatar.Renders[Index].gameObject.layer = 6;
                    }
                }
                else if (Player is BasisRemotePlayer remotePlayer)
                {
                    Player.Avatar.IsOwnedLocally = false;
                    CreateRemote(remotePlayer);
                    remotePlayer.InitalizeIKCalibration(remotePlayer.RemoteAvatarDriver);
                    Avatar.OnAvatarReady?.Invoke(false);
                    for (int Index = 0; Index < Avatar.Renders.Length; Index++)
                    {
                        Avatar.Renders[Index].gameObject.layer = 7;
                    }
                }
            }
        }

        public static async Task LoadAvatarAfterError(BasisPlayer Player)
        {
            try
            {
                ChecksRequired Required = new ChecksRequired
                {
                    UseContentRemoval = false,
                    DisableAnimatorEvents = false
                };
                var Para = new UnityEngine.ResourceManagement.ResourceProviders.InstantiationParameters(Player.transform.position, Quaternion.identity, null);
                (List<GameObject> GameObjects, AddressableGenericResource resource) = await AddressableResourceProcess.LoadAsGameObjectsAsync(LoadingAvatar.BasisLocalEncryptedBundle.LocalBundleFile, Para, Required);

                if (GameObjects.Count != 0)
                {
                    InitializePlayerAvatar(Player, GameObjects[0]);
                }
                Player.AvatarMetaData = BasisAvatarFactory.LoadingAvatar;
                Player.AvatarLoadMode = 1;
                Player.AvatarSwitched();

                //we want to use Avatar Switched instead of the fallback version to let the server know this is what we actually want to use.
            }
            catch (Exception e)
            {
                Debug.LogError($"Fallback avatar loading failed: {e}");
            }
        }
        /// <summary>
        /// no content searching is done here since its local content.
        /// </summary>
        /// <param name="Player"></param>
        /// <param name="LoadingAvatarToUse"></param>
        public static void LoadLoadingAvatar(BasisPlayer Player, string LoadingAvatarToUse)
        {
            var op = Addressables.LoadAssetAsync<GameObject>(LoadingAvatarToUse);
            var LoadingAvatar = op.WaitForCompletion();

            var InSceneLoadingAvatar = GameObject.Instantiate(LoadingAvatar, Player.transform.position, Quaternion.identity);


            if (InSceneLoadingAvatar.TryGetComponent(out BasisAvatar Avatar))
            {
                Player.Avatar = Avatar;
                Player.Avatar.Renders = Player.Avatar.GetComponentsInChildren<Renderer>(true);
                int RenderCount = Player.Avatar.Renders.Length;
                if (Player.IsLocal)
                {
                    BasisLocalPlayer BasisLocalPlayer = (BasisLocalPlayer)Player;
                    Player.Avatar.IsOwnedLocally = true;
                    CreateLocal(BasisLocalPlayer);
                    Player.InitalizeIKCalibration(BasisLocalPlayer.AvatarDriver);
                    for (int Index = 0; Index < RenderCount; Index++)
                    {
                        Avatar.Renders[Index].gameObject.layer = 6;
                    }
                }
                else
                {
                    BasisRemotePlayer BasisRemotePlayer = (BasisRemotePlayer)Player;
                    Player.Avatar.IsOwnedLocally = false;
                    CreateRemote(BasisRemotePlayer);
                    Player.InitalizeIKCalibration(BasisRemotePlayer.RemoteAvatarDriver);
                    for (int Index = 0; Index < RenderCount; Index++)
                    {
                        Avatar.Renders[Index].gameObject.layer = 7;
                    }
                }
            }
        }
        public static async void DeleteLastAvatar(BasisPlayer Player,bool IsRemovingFallback)
        {
            if (Player.Avatar != null)
            {
                if (IsRemovingFallback)
                {
                    GameObject.Destroy(Player.Avatar.gameObject);
                }
                else
                {
                  await  BasisLoadHandler.DestroyGameobject(Player.Avatar.gameObject, Player.AvatarMetaData.BasisRemoteBundleEncrypted.MetaURL, false);
                }
            }

            if (Player.AvatarAddressableGenericResource != null)
            {
                AddressableLoadFactory.ReleaseResource(Player.AvatarAddressableGenericResource);
            }
        }

        public static void CreateRemote(BasisRemotePlayer Player)
        {
            if (Player == null || Player.Avatar == null)
            {
                Debug.LogError("Missing RemotePlayer or Avatar");
                return;
            }

            Player.RemoteAvatarDriver.RemoteCalibration(Player);
        }

        public static void CreateLocal(BasisLocalPlayer Player)
        {
            if (Player == null || Player.Avatar == null)
            {
                Debug.LogError("Missing LocalPlayer or Avatar");
                return;
            }

            Player.AvatarDriver.InitialLocalCalibration(Player);
        }
    }
}