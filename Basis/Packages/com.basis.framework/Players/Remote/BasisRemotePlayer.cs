using Basis.Scripts.Addressable_Driver.Resource;
using Basis.Scripts.Avatar;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking.Receivers;
using Basis.Scripts.UI.NamePlate;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using static SerializableBasis;

namespace Basis.Scripts.BasisSdk.Players
{
    [System.Serializable]
    public class BasisRemotePlayer : BasisPlayer
    {
        #region Drivers & Receivers

        [Header("Avatar Driver")]
        [SerializeField]
        public BasisRemoteAvatarDriver RemoteAvatarDriver = new BasisRemoteAvatarDriver();

        [Header("Network Receiver")]
        [SerializeField]
        public BasisNetworkReceiver NetworkReceiver;

        [Header("Face Driver")]
        [SerializeField]
        public BasisRemoteFaceDriver RemoteFaceDriver;

        #endregion

        #region UI / Name Plate

        [Header("Name Plate")]
        [SerializeField]
        public BasisRemoteNamePlate RemoteNamePlate = null;

        public static GameObject NamePlate;

        #endregion

        #region State / Data

        public bool OutOfRangeFromLocal = false;
        public ClientAvatarChangeMessage CACM;
        public bool InAvatarRange = true;

        public byte AlwaysRequestedMode; // 0 downloading, 1 local

        [HideInInspector]
        public BasisLoadableBundle AlwaysRequestedAvatar;

        public int RemotePlayerDataIndex;
        public Transform MouthTransform;
        public short LastComputedMeshLod = -1;

        #endregion

        #region Avatar Load State Fingerprint (NEW)

        // Tracks what we have ACTUALLY loaded right now
        private string _loadedAvatarKey = null;
        private byte _loadedMode = 255; // invalid sentinel
        private bool _loadedIsFallback = true;

        // Coalesce repeated refresh calls
        private bool _pendingRefresh = false;

        private static string GetAvatarKey(BasisLoadableBundle blb)
        {
            if (blb == null) return string.Empty;

            // Use a stable identifier for the avatar. This is the minimum viable key.
            // If you have a better unique GUID/hash in the bundle, use that instead.
            return blb.BasisRemoteBundleEncrypted.RemoteBeeFileLocation ?? string.Empty;
        }

        #endregion

        #region Initialization / Addressables

        public static GameObject LoadFromHandle(string LoadableNamePlatename)
        {
            if (NamePlate == null)
            {
                UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject> op =
                    Addressables.LoadAssetAsync<GameObject>(LoadableNamePlatename);
                NamePlate = op.WaitForCompletion();
            }
            return NamePlate;
        }

        public void RemoteInitialize(
            ClientAvatarChangeMessage cACM,
            ClientMetaDataMessage PlayerMetaDataMessage,
            string LoadableNamePlatename = "Assets/UI/Prefabs/NamePlate.prefab")
        {
            CACM = cACM;
            DisplayName = PlayerMetaDataMessage.playerDisplayName;
            SetSafeDisplayname();
            this.name = DisplayName;
            UUID = PlayerMetaDataMessage.playerUUID;
            IsLocal = false;

            GameObject data = GameObject.Instantiate(LoadFromHandle(LoadableNamePlatename), transform);
            if (data.TryGetComponent(out RemoteNamePlate))
            {
                if (this == null)
                {
                    AddressableResourceProcess.ReleaseGameobject(data);
                    return;
                }
                RemoteNamePlate.Initalize(this);
            }
        }

        #endregion

        #region Avatar Loading

        public void LoadAvatarFromInitial(ClientAvatarChangeMessage CACM)
        {
            if (BasisAvatar == null)
            {
                this.CACM = CACM;
                BasisLoadableBundle BasisLoadedBundle =
                    BasisBundleConversionNetwork.ConvertNetworkBytesToBasisLoadableBundle(CACM.byteArray);

                InAvatarRange = false;

                if (BasisLoadedBundle != null)
                {
                    AlwaysRequestedAvatar = BasisLoadedBundle;
                    AlwaysRequestedMode = CACM.loadMode;

                    BasisAvatarFactory.RemoveOldAvatarAndLoadFallback(
                        this,
                        BasisAvatarFactory.LoadingAvatar.BasisLocalEncryptedBundle.DownloadedBeeFileLocation,
                        Vector3.zero,
                        Quaternion.identity
                    );

                    // We just set fallback
                    _loadedIsFallback = true;
                    _loadedAvatarKey = null;
                    _loadedMode = 255;
                }
                else
                {
                    BasisDebug.LogError("Invalid Inital Data");
                }
            }
        }

        public bool IsLoadingAnAvatar = false;

        /// <summary>
        /// Your original CreateAvatar remains useful when you explicitly want to load a specific bundle/mode.
        /// It also updates the loaded fingerprint so we can avoid future redundant loads.
        /// </summary>
        public async Task CreateAvatar(byte mode, BasisLoadableBundle blb)
        {
            // Normalize input immediately
            if (blb == null || string.IsNullOrEmpty(blb.BasisRemoteBundleEncrypted.RemoteBeeFileLocation))
            {
                BasisDebug.LogError("trying to create Avatar with empty Bundle", BasisDebug.LogTag.Remote);

                blb = BasisAvatarFactory.LoadingAvatar;
                mode = 0;
            }

            // If we already know this avatar cannot be shown, ensure fallback (once) and bail
            if (!InAvatarRange)
            {
                EnsureFallbackOnce();
                LastComputedMeshLod = -1;
                return;
            }

            IsLoadingAnAvatar = true;

            // Fetch per-player visibility settings only if needed
            BasisPlayerSettingsData settings = await BasisPlayerSettingsManager.RequestPlayerSettings(UUID);

            // Remember last requested avatar and mode for potential refreshes
            AlwaysRequestedAvatar = blb;
            AlwaysRequestedMode = mode;

            if (settings.AvatarVisible)
            {
                await BasisAvatarFactory.LoadAvatarRemote(this, mode, blb, Vector3.zero, Quaternion.identity);

                // Update fingerprint: we loaded a real avatar
                _loadedAvatarKey = GetAvatarKey(blb);
                _loadedMode = mode;
                _loadedIsFallback = false;
            }
            else
            {
                EnsureFallbackOnce();
            }

            LastComputedMeshLod = -1;
            IsLoadingAnAvatar = false;
        }

        /// <summary>
        /// NEW: Call this instead of ReloadAvatar(). It decides whether anything needs doing.
        /// It coalesces calls to avoid jitter thrash.
        /// </summary>
        public void RequestAvatarStateRefresh()
        {
            if (_pendingRefresh) return;
            _pendingRefresh = true;
            _ = RefreshAvatarStateInternal();
        }

        private async Task RefreshAvatarStateInternal()
        {
            // Coalesce multiple triggers in the same frame / tick
            await Task.Yield();
            _pendingRefresh = false;

            if (IsLoadingAnAvatar) return;

            // If we have no requested avatar yet, nothing to load.
            if (AlwaysRequestedAvatar == null)
            {
                // You can choose to fallback here, but typically initial setup handles it.
                return;
            }

            // 1) Range gating: out of range => fallback (but only once)
            if (!InAvatarRange)
            {
                EnsureFallbackOnce();
                LastComputedMeshLod = -1;
                return;
            }

            // 2) Visibility gating
            BasisPlayerSettingsData settings = await BasisPlayerSettingsManager.RequestPlayerSettings(UUID);
            if (!settings.AvatarVisible)
            {
                EnsureFallbackOnce();
                LastComputedMeshLod = -1;
                return;
            }

            // 3) In range + visible: load ONLY if desired != loaded
            string desiredKey = GetAvatarKey(AlwaysRequestedAvatar);
            byte desiredMode = AlwaysRequestedMode;

            bool needsLoad =
                _loadedIsFallback ||
                _loadedMode != desiredMode ||
                _loadedAvatarKey != desiredKey;

            if (!needsLoad) return;

            IsLoadingAnAvatar = true;
            try
            {
                await BasisAvatarFactory.LoadAvatarRemote(this, desiredMode, AlwaysRequestedAvatar, Vector3.zero, Quaternion.identity);

                _loadedAvatarKey = desiredKey;
                _loadedMode = desiredMode;
                _loadedIsFallback = false;
                LastComputedMeshLod = -1;
            }
            finally
            {
                IsLoadingAnAvatar = false;
            }
        }

        private void EnsureFallbackOnce()
        {
            if (_loadedIsFallback) return;

            BasisAvatarFactory.RemoveOldAvatarAndLoadFallback(
                this,
                BasisAvatarFactory.LoadingAvatar.BasisLocalEncryptedBundle.DownloadedBeeFileLocation,
                Vector3.zero,
                Quaternion.identity
            );

            _loadedIsFallback = true;
            _loadedAvatarKey = null;
            _loadedMode = 255;
        }

        #endregion

        #region Teardown

        public void OnDestroy()
        {
            if (RemoteFaceDriver != null)
            {
                RemoteFaceDriver.OnDestroy();
            }
            if (RemoteNamePlate != null)
            {
                RemoteNamePlate.DeInitalize();
                AddressableResourceProcess.ReleaseGameobject(RemoteNamePlate.gameObject);
            }
            if (RemoteAvatarDriver.InBoneDriver)
            {
                RemoteBoneJobSystem.RemoveRemotePlayer(NetworkReceiver.playerId);
                RemoteAvatarDriver.InBoneDriver = false;
            }
        }

        #endregion

        #region LOD

        public void ChangeMeshLOD(float DistanceToPlayer, float ReductionMultiplier)
        {
            if (BasisAvatar != null && BasisAvatar.Renders != null)
            {
                float normalized = DistanceToPlayer * ReductionMultiplier;
                short grid = (short)Mathf.Clamp(Mathf.FloorToInt(normalized * 4f), 0, 3);

                if (LastComputedMeshLod != grid)
                {
                    LastComputedMeshLod = grid;
                    foreach (Renderer renderer in BasisAvatar.Renders)
                    {
                        if (renderer != null)
                        {
                            renderer.forceMeshLod = grid;
                        }
                    }
                }
            }
        }

        #endregion
    }
}
