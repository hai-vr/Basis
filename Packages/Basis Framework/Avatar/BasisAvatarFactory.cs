using Basis.Scripts.Addressable_Driver;
using Basis.Scripts.Addressable_Driver.Factory;
using Basis.Scripts.Addressable_Driver.Loading;
using Basis.Scripts.Addressable_Driver.Resource;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Basis.Scripts.Avatar
{
    public static class BasisAvatarFactory
    {
        public const string LoadingAvatar = "LoadingAvatar";

        public static async Task LoadAvatar(BasisLocalPlayer Player, string AvatarAddress,byte Mode, string hash)
        {
            if (string.IsNullOrEmpty(AvatarAddress))
            {
                Debug.LogError("Avatar Address was empty or null! Falling back to loading avatar.");
                await LoadAvatarAfterError(Player);
                return;
            }

            hash = await BasisAssetBundleHashLookup.GetHashOrFallback(hash, AvatarAddress);

            DeleteLastAvatar(Player);
            LoadLoadingAvatar(Player, LoadingAvatar);

            try
            {
                GameObject Output = null;
                switch (Mode)
                {
                    case 0://download
                        Output = await DownloadAndLoadAvatar(AvatarAddress, hash, Player);
                        break;
                    case 1://localload
                        var Para = new UnityEngine.ResourceManagement.ResourceProviders.InstantiationParameters(Player.transform.position, Quaternion.identity, null);
                        (List<GameObject> GameObjects, AddressableGenericResource resource) = await AddressableResourceProcess.LoadAsGameObjectsAsync(LoadingAvatar, Para);

                        if (GameObjects.Count > 0)
                        {
                            Output = GameObjects[0];
                        }
                        break;
                   default:
                        Output = await DownloadAndLoadAvatar(AvatarAddress, hash, Player);
                        break;
                }
                Player.AvatarUrl = AvatarAddress;
                Player.AvatarLoadMode = Mode;

                InitializePlayerAvatar(Player, Output);
                await Player.SetPlayersEyeHeight();
                Player.AvatarSwitched();
            }
            catch (Exception e)
            {
                Debug.LogError($"Loading avatar failed: {e}");
                 await LoadAvatarAfterError(Player);
            }
        }

        public static async Task LoadAvatar(BasisRemotePlayer Player, string AvatarAddress, byte Mode, string hash = "")
        {
            if (string.IsNullOrEmpty(AvatarAddress))
            {
                Debug.LogError("Avatar Address was empty or null! Falling back to loading avatar.");
                await LoadAvatarAfterError(Player);
                return;
            }

            hash = await BasisAssetBundleHashLookup.GetHashOrFallback(hash, AvatarAddress);

            DeleteLastAvatar(Player);
            LoadLoadingAvatar(Player, LoadingAvatar);

            try
            {
                GameObject Output = null;
                switch (Mode)
                {
                    case 0://download
                        Output = await DownloadAndLoadAvatar(AvatarAddress, hash, Player);
                        break;
                    case 1://localload
                        var Para = new UnityEngine.ResourceManagement.ResourceProviders.InstantiationParameters(Player.transform.position, Quaternion.identity, null);
                        (List<GameObject> GameObjects, AddressableGenericResource resource) = await AddressableResourceProcess.LoadAsGameObjectsAsync(LoadingAvatar, Para);

                        if (GameObjects.Count > 0)
                        {
                            Output = GameObjects[0];
                        }
                        break;
                    default:
                        Output = await DownloadAndLoadAvatar(AvatarAddress, hash, Player);
                        break;
                }
                Player.AvatarUrl = AvatarAddress;
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

        private static async Task<GameObject> DownloadAndLoadAvatar(string AvatarAddress, string hash, BasisPlayer Player)
        {
            string FileName = AddressableManagement.GetFileNameFromUrlWithoutExtension(AvatarAddress);
            return await BasisGameObjectAssetBundleManager.DownloadAndLoadGameObjectAsync(AvatarAddress, hash, FileName, BasisStorageManagement.AssetSubDirectory, Player.transform.position,Quaternion.identity, Player.ProgressReportAvatarLoad);
        }

        private static void InitializePlayerAvatar(BasisPlayer Player, GameObject Output)
        {
            if (Output.TryGetComponent(out BasisAvatar Avatar))
            {
                DeleteLastAvatar(Player);
                Player.Avatar = Avatar;
                if (Player is BasisLocalPlayer localPlayer)
                {
                    CreateLocal(localPlayer);
                    localPlayer.InitalizeIKCalibration(localPlayer.AvatarDriver);
                }
                else if (Player is BasisRemotePlayer remotePlayer)
                {
                    CreateRemote(remotePlayer);
                    remotePlayer.InitalizeIKCalibration(remotePlayer.RemoteAvatarDriver);
                }
            }
        }

        public static async Task LoadAvatarAfterError(BasisPlayer Player)
        {
            try
            {
                var Para = new UnityEngine.ResourceManagement.ResourceProviders.InstantiationParameters(Player.transform.position, Quaternion.identity, null);
                (List<GameObject> GameObjects, AddressableGenericResource resource) = await AddressableResourceProcess.LoadAsGameObjectsAsync(LoadingAvatar, Para);

                if (GameObjects.Count > 0)
                {
                    InitializePlayerAvatar(Player, GameObjects[0]);
                }

                Player.AvatarSwitchedFallBack();
            }
            catch (Exception e)
            {
                Debug.LogError($"Fallback avatar loading failed: {e}");
            }
        }

        public static void LoadLoadingAvatar(BasisPlayer Player, string LoadingAvatarToUse)
        {
            var op = Addressables.LoadAssetAsync<GameObject>(LoadingAvatarToUse);
            var LoadingAvatar = op.WaitForCompletion();
            var InSceneLoadingAvatar = GameObject.Instantiate(LoadingAvatar, Player.transform.position, Quaternion.identity);

            if (InSceneLoadingAvatar.TryGetComponent(out BasisAvatar Avatar))
            {
                Player.Avatar = Avatar;

                if (Player.IsLocal)
                {
                    BasisLocalPlayer BasisLocalPlayer =(BasisLocalPlayer)Player;
                    CreateLocal(BasisLocalPlayer);
                    Player.InitalizeIKCalibration(BasisLocalPlayer.AvatarDriver);
                }
                else
                {
                    BasisRemotePlayer BasisRemotePlayer =(BasisRemotePlayer)Player;
                    CreateRemote(BasisRemotePlayer);
                    Player.InitalizeIKCalibration(BasisRemotePlayer.RemoteAvatarDriver);
                }
            }
        }

        public static void DeleteLastAvatar(BasisPlayer Player)
        {
            if (Player.Avatar != null)
            {
                GameObject.Destroy(Player.Avatar.gameObject);
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