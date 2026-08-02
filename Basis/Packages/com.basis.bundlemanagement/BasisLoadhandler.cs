using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk;
using UnityEngine;
using UnityEngine.SceneManagement;
using static BasisSerialization;
using static BundledContentHolder;
public static class BasisLoadHandler
{
    public static bool IsInitialized = false;
    public static ConcurrentDictionary<string, BasisTrackedBundleWrapper> LoadedBundles = new ConcurrentDictionary<string, BasisTrackedBundleWrapper>();
    public static ConcurrentDictionary<string, BasisBEEExtensionMeta> OnDiscData = new ConcurrentDictionary<string, BasisBEEExtensionMeta>();
    public static SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static async void Initialization()
    {
        BasisDebug.Log("Game has started after scene load.", BasisDebug.LogTag.Event);
        await EnsureInitializationComplete();
        SceneManager.sceneUnloaded += SceneUnloaded;
    }
    private static async void SceneUnloaded(Scene UnloadedScene)
    {
        if (LoadedBundles == null || string.IsNullOrEmpty(UnloadedScene.path))
        {
            return;
        }
        List<string> keysToRemove = new List<string>();
        foreach (KeyValuePair<string, BasisTrackedBundleWrapper> kvp in LoadedBundles)
        {
            var bundle = kvp.Value;

            if (bundle == null || string.IsNullOrEmpty(bundle.MetaLink))
                continue;

            if (bundle.MetaLink == UnloadedScene.path)
            {
                bundle.DeIncrement();

                bool state = false;
                try
                {
                    state = await bundle.UnloadIfReady();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error while unloading bundle '{kvp.Key}': {ex}");
                }

                if (state)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
        }

        foreach (string key in keysToRemove)
        {
            LoadedBundles.TryRemove(key, out var data);
        }
    }
    /// <summary>
    /// this will take 30 seconds to execute
    /// after that we wait for 30 seconds to see if we can also remove the bundle!
    /// </summary>
    /// <param name="LoadedKey"></param>
    /// <returns></returns>
    public static async Task RequestDeIncrementOfBundle(BasisLoadableBundle loadableBundle)
    {
        string CombinedURL = loadableBundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation;
        string Key = GetBundleKey(loadableBundle);
        if (LoadedBundles.TryGetValue(Key, out BasisTrackedBundleWrapper Wrapper))
        {
            Wrapper.DeIncrement();
            bool State = await Wrapper.UnloadIfReady();
            if (State)
            {
                // Only remove OUR wrapper: a lookup during the unload continuation may have
                // seen IsUnloaded, dropped the husk, and registered a fresh wrapper under the
                // same key — removing blindly here would tear that replacement out.
                string registeredKey = Wrapper.RegisteredKey ?? Key;
                if (LoadedBundles.TryGetValue(registeredKey, out BasisTrackedBundleWrapper current) && ReferenceEquals(current, Wrapper))
                {
                    LoadedBundles.Remove(registeredKey, out var data);
                }
                return;
            }
        }
        else
        {
            if (CombinedURL.ToLower() != BasisBeeConstants.DefaultAvatar.ToLower())
            {
                // The key is logged because a miss here is almost always key drift rather than a
                // genuinely absent bundle: the reservation stays held, so the wrapper never
                // unloads, and whatever DID get found under the drifted key was decremented in
                // its place.
                BasisDebug.LogError($"tried to find Loaded Key {CombinedURL} (key '{Key}') but could not find it!");
            }
        }
    }
    public static async Task<GameObject> LoadGameObjectBundle(GameObject DisabledGameobject,BasisLoadableBundle loadableBundle, bool useContentRemoval, BasisProgressReport report, CancellationToken cancellationToken, Vector3 Position, Quaternion Rotation, Vector3 Scale, bool ModifyScale, Selector Selector, Transform Parent = null, bool DestroyColliders = false,bool ChangeColidersToCorrectLayer = false, long MaxDownloadSizeInMB = 4L * 1024 * 1024 * 1024, List<BasisHeadChop.HeadChopTarget> HarvestedHeadChop = null)
    {
        await EnsureInitializationComplete();

        string Key = GetBundleKey(loadableBundle);
        if (LoadedBundles.TryGetValue(Key, out BasisTrackedBundleWrapper wrapper))
        {
            if (wrapper.IsUnloaded)
            {
                // Unload(true) already destroyed this wrapper's assets — instantiating from
                // it produces an avatar whose Animator.avatar and meshes are dead. Drop the
                // husk and load fresh.
                if (LoadedBundles.TryGetValue(Key, out BasisTrackedBundleWrapper stale) && ReferenceEquals(stale, wrapper))
                {
                    LoadedBundles.Remove(Key, out var husk);
                }
            }
            else
            {
                // Reserve BEFORE any await: the unload grace period re-checks the count, and
                // the budgeted instantiate spans frames. Incrementing only at instantiate-end
                // (the old LoadFromWrapper behavior) let the grace expire mid-load on a
                // range-boundary re-entry and Unload(true) killed the clone's assets.
                wrapper.Increment();
                try
                {
                    await wrapper.WaitForBundleLoadAsync();

                    // ensure the bundle connector is updated from the wrapper
                    loadableBundle.BasisBundleConnector = wrapper.LoadableBundle.BasisBundleConnector;

                    GameObject result = await BasisBundleLoadAsset.LoadFromWrapper(DisabledGameobject,wrapper, useContentRemoval, Position, Rotation, ModifyScale, Scale, Selector, Parent, DestroyColliders, ChangeColidersToCorrectLayer, HarvestedHeadChop);
                    if (result == null)
                    {
                        wrapper.DeIncrement();
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    wrapper.DeIncrement();
                    BasisDebug.LogError($"Failed to load content: {ex}");
                    LoadedBundles.Remove(Key, out var data);
                    return null;
                }
            }
        }

        return await HandleFirstBundleLoad(DisabledGameobject,loadableBundle, useContentRemoval, report, cancellationToken, Position, Rotation, Scale, ModifyScale, Selector, Parent, DestroyColliders, ChangeColidersToCorrectLayer, MaxDownloadSizeInMB, HarvestedHeadChop);
    }
    public static async Task<Scene> LoadSceneBundle(bool makeActiveScene, BasisLoadableBundle loadableBundle, BasisProgressReport report, CancellationToken cancellationToken, long MaxDownloadSizeInMB = 4L * 1024 * 1024 * 1024)
    {
        await EnsureInitializationComplete();

        string Key = GetBundleKey(loadableBundle);
        if (LoadedBundles.TryGetValue(Key, out BasisTrackedBundleWrapper wrapper) && wrapper.IsUnloaded)
        {
            // Dead husk from a completed unload — drop it and take the first-load path.
            if (LoadedBundles.TryGetValue(Key, out BasisTrackedBundleWrapper stale) && ReferenceEquals(stale, wrapper))
            {
                LoadedBundles.Remove(Key, out var husk);
            }
            wrapper = null;
        }
        if (wrapper != null)
        {
            BasisDebug.Log($"Bundle On Disc Loading", BasisDebug.LogTag.Networking);
            if (wrapper.AssetBundle == null)
            {
                await BasisBeeManagement.HandleBundleAndMetaLoading(wrapper, report, cancellationToken, MaxDownloadSizeInMB);
            }
            else
            {
                await wrapper.WaitForBundleLoadAsync();
            }

            if (wrapper.AssetBundle == null)
            {
                BasisDebug.LogError("Scene bundle was not available after load attempt.");
                return new Scene();
            }
            BasisDebug.Log($"Bundle Loaded, Loading Scene", BasisDebug.LogTag.Networking);
            return await BasisBundleLoadAsset.LoadSceneFromBundleAsync(wrapper, makeActiveScene, report);
        }

        return await HandleFirstSceneLoad(loadableBundle, makeActiveScene, report, cancellationToken, MaxDownloadSizeInMB);
    }

    private static async Task<Scene> HandleFirstSceneLoad(BasisLoadableBundle loadableBundle, bool makeActiveScene, BasisProgressReport report, CancellationToken cancellationToken, long MaxDownloadSizeInMB = 4L * 1024 * 1024 * 1024)
    {
        string Key = GetBundleKey(loadableBundle);
        BasisTrackedBundleWrapper wrapper = new BasisTrackedBundleWrapper { AssetBundle = null, LoadableBundle = loadableBundle, RegisteredKey = Key };

        if (!LoadedBundles.TryAdd(Key, wrapper))
        {
            BasisDebug.LogError("Unable to add bundle wrapper.");
            return new Scene();
        }

        try
        {
            await BasisBeeManagement.HandleBundleAndMetaLoading(wrapper, report, cancellationToken, MaxDownloadSizeInMB);
            return await BasisBundleLoadAsset.LoadSceneFromBundleAsync(wrapper, makeActiveScene, report);
        }
        catch
        {
            wrapper.DidErrorOccur = true;
            if (wrapper.AssetBundle != null)
            {
                wrapper.AssetBundle.Unload(true);
                wrapper.AssetBundle = null;
            }
            LoadedBundles.Remove(Key, out var data);
            throw;
        }
    }

    private static async Task<GameObject> HandleFirstBundleLoad(GameObject DisabledGameobject,BasisLoadableBundle loadableBundle, bool useContentRemoval, BasisProgressReport report, CancellationToken cancellationToken, Vector3 Position, Quaternion Rotation, Vector3 Scale, bool ModifyScale, Selector Selector, Transform Parent = null, bool DestroyColliders = false, bool ChangeColidersToCorrectLayer = false, long MaxDownloadSizeInMB = 4L * 1024 * 1024 * 1024, List<BasisHeadChop.HeadChopTarget> HarvestedHeadChop = null)
    {
        string Key = GetBundleKey(loadableBundle);
        BasisTrackedBundleWrapper wrapper = new BasisTrackedBundleWrapper
        {
            AssetBundle = null,
            LoadableBundle = loadableBundle,
            RegisteredKey = Key
        };

        if (!LoadedBundles.TryAdd(Key, wrapper))
        {
            BasisDebug.LogError("Unable to add bundle wrapper.");
            return null;
        }

        // The instantiate reservation, held from registration so the unload grace can never
        // fire between the download completing and the budgeted instantiate finishing.
        wrapper.Increment();
        try
        {
            await BasisBeeManagement.HandleBundleAndMetaLoading(wrapper, report, cancellationToken, MaxDownloadSizeInMB);
            GameObject result = await BasisBundleLoadAsset.LoadFromWrapper(DisabledGameobject, wrapper, useContentRemoval, Position, Rotation, ModifyScale, Scale, Selector, Parent, DestroyColliders, ChangeColidersToCorrectLayer, HarvestedHeadChop);
            if (result == null)
            {
                wrapper.DeIncrement();
            }
            return result;
        }
        catch (Exception ex)
        {
            BasisDebug.LogError($"{ex.Message} {ex.StackTrace}");
            wrapper.DeIncrement();
            wrapper.DidErrorOccur = true;
            if (wrapper.AssetBundle != null)
            {
                wrapper.AssetBundle.Unload(true);
                wrapper.AssetBundle = null;
            }
            wrapper.UnloadGltfTemplate();
            LoadedBundles.Remove(Key, out var data);
            CleanupFiles(loadableBundle.BasisLocalEncryptedBundle);
            return null;
        }
    }

    private static string GetBundleKey(BasisLoadableBundle loadableBundle)
    {
        string url = BasisIOManagement.CanonicalizeRemoteUrl(loadableBundle?.BasisRemoteBundleEncrypted?.RemoteBeeFileLocation);
        string key = $"{url}|{HashUnlockPassword(loadableBundle?.UnlockPassword)}";

        // Version-aware only when a version is actually declared, so unversioned content produces
        // the exact key it did before and its load/DeIncrement pairing is untouched. When a url is
        // republished, this is what stops two players wearing the same url at different versions
        // from collapsing onto one wrapper — the in-memory cache is keyed by url alone otherwise,
        // and would hand the stale bundle to whoever asked for the new one.
        string versionTag = BasisContentVersion.Normalize(loadableBundle?.BasisRemoteBundleEncrypted?.RemoteVersionTag);
        return versionTag.Length == 0 ? key : $"{key}|{versionTag}";
    }

    // Hashing the unlock password (SHA256.Create + ComputeHash) is the bulk of GetBundleKey's
    // cost and the key is recomputed several times per load for the same (usually empty) password.
    // Memoize per distinct password so it is paid once.
    private static readonly ConcurrentDictionary<string, string> _passwordHashCache = new ConcurrentDictionary<string, string>();

    private static string HashUnlockPassword(string unlockPassword)
    {
        return _passwordHashCache.GetOrAdd(unlockPassword ?? string.Empty, password =>
        {
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
            return BitConverter.ToString(hash).Replace("-", string.Empty);
        });
    }

    public static bool IsUrlLoadedInMemory(string remoteUrl)
    {
        if (string.IsNullOrEmpty(remoteUrl))
        {
            return false;
        }
        foreach (BasisTrackedBundleWrapper wrapper in LoadedBundles.Values)
        {
            if (wrapper?.LoadableBundle?.BasisRemoteBundleEncrypted?.RemoteBeeFileLocation == remoteUrl)
            {
                return true;
            }
        }
        return false;
    }

    public static void UnloadAllForUrl(string remoteUrl)
    {
        if (string.IsNullOrEmpty(remoteUrl))
        {
            return;
        }
        List<string> keysToRemove = new List<string>();
        string canonicalUrl = BasisIOManagement.CanonicalizeRemoteUrl(remoteUrl);
        foreach (KeyValuePair<string, BasisTrackedBundleWrapper> kvp in LoadedBundles)
        {
            if (BasisIOManagement.CanonicalizeRemoteUrl(kvp.Value?.LoadableBundle?.BasisRemoteBundleEncrypted?.RemoteBeeFileLocation) == canonicalUrl)
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        foreach (string key in keysToRemove)
        {
            if (LoadedBundles.TryGetValue(key, out BasisTrackedBundleWrapper inUseCheck) && inUseCheck != null && inUseCheck.IsInUse)
            {
                BasisDebug.LogError($"Skipping in-memory unload for: {remoteUrl}; bundle is still in use.");
                continue;
            }
            if (LoadedBundles.TryRemove(key, out BasisTrackedBundleWrapper removed) && removed != null)
            {
                try
                {
                    removed.IsUnloaded = true;
                    if (removed.AssetBundle != null)
                    {
                        BasisDebug.Log($"Unloading in-memory AssetBundle for: {remoteUrl}", BasisDebug.LogTag.Event);
                        removed.AssetBundle.Unload(true);
                    }
                    removed.UnloadGltfTemplate();
                }
                catch (Exception ex)
                {
                    BasisDebug.LogError($"Error unloading AssetBundle: {ex.Message}");
                }
            }
        }
    }

    public static string GetDiscInfoKey(string remoteUrl, string downloadedPlatform)
    {
        string safeUrl = BasisIOManagement.CanonicalizeRemoteUrl(remoteUrl);
        string safePlatform = string.IsNullOrWhiteSpace(downloadedPlatform) ? "legacy" : downloadedPlatform.Trim();
        return $"{safePlatform}|{safeUrl}";
    }

    private static bool TryResolveStoredBeePath(BasisBEEExtensionMeta discInfo, out string beePath)
    {
        beePath = discInfo.StoredLocal.DownloadedBeeFileLocation;
        if (!string.IsNullOrWhiteSpace(beePath) && File.Exists(beePath))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(discInfo.UniqueVersion))
        {
            string platformAwarePath = BasisIOManagement.GetBeeCacheFilePath(discInfo.UniqueVersion, discInfo.DownloadedPlatform);
            if (File.Exists(platformAwarePath))
            {
                discInfo.StoredLocal.DownloadedBeeFileLocation = platformAwarePath;
                beePath = platformAwarePath;
                return true;
            }

            string legacyPath = BasisIOManagement.GetLegacyBeeCacheFilePath(discInfo.UniqueVersion);
            if (File.Exists(legacyPath))
            {
                discInfo.StoredLocal.DownloadedBeeFileLocation = legacyPath;
                beePath = legacyPath;
                return true;
            }
        }

        beePath = null;
        return false;
    }

    private static bool TryResolveStoredConnectorPath(BasisBEEExtensionMeta discInfo, out string connectorPath)
    {
        connectorPath = discInfo.StoredLocal.DownloadedConnectorFileLocation;
        if (!string.IsNullOrWhiteSpace(connectorPath) && File.Exists(connectorPath))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(discInfo.UniqueVersion))
        {
            string platformAwarePath = BasisIOManagement.GetConnectorCacheFilePath(discInfo.UniqueVersion, discInfo.DownloadedPlatform);
            if (File.Exists(platformAwarePath))
            {
                discInfo.StoredLocal.DownloadedConnectorFileLocation = platformAwarePath;
                connectorPath = platformAwarePath;
                return true;
            }
        }

        connectorPath = null;
        return false;
    }

    private static bool HasAnyCachedPayload(BasisBEEExtensionMeta discInfo)
    {
        return TryResolveStoredBeePath(discInfo, out _) || TryResolveStoredConnectorPath(discInfo, out _);
    }

    private static bool TryLazyLoadDiscInfo(string metaUrl, string currentPlatform, out BasisBEEExtensionMeta info)
    {
        info = null;

        string path = BasisIOManagement.GenerateFolderPath(BasisBeeConstants.AssetBundlesFolder);
        if (!Directory.Exists(path))
        {
            return false;
        }

        BasisBEEExtensionMeta legacyCandidate = null;
        string canonicalUrl = BasisIOManagement.CanonicalizeRemoteUrl(metaUrl);

        foreach (string file in Directory.GetFiles(path, $"*{BasisBeeConstants.BasisMetaExtension}"))
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(file);
                BasisBEEExtensionMeta discInfo = BasisSerialization.DeserializeValue<BasisBEEExtensionMeta>(fileData);
                if (BasisIOManagement.CanonicalizeRemoteUrl(discInfo?.StoredRemote?.RemoteBeeFileLocation) != canonicalUrl)
                {
                    continue;
                }

                OnDiscData[GetDiscInfoKey(discInfo.StoredRemote.RemoteBeeFileLocation, discInfo.DownloadedPlatform)] = discInfo;

                if (!HasAnyCachedPayload(discInfo))
                {
                    continue;
                }

                if (BasisIOManagement.CachePlatformMatchesCurrent(discInfo.DownloadedPlatform))
                {
                    info = discInfo;
                    return true;
                }

                if (string.IsNullOrWhiteSpace(discInfo.DownloadedPlatform) && legacyCandidate == null)
                {
                    legacyCandidate = discInfo;
                }
            }
            catch (Exception ex)
            {
                BasisDebug.LogWarning($"Failed lazy-loading disc info from {file}: {ex.Message}", BasisDebug.LogTag.Event);
            }
        }

        if (legacyCandidate != null)
        {
            info = legacyCandidate;
            return true;
        }

        return false;
    }

    private static bool TryGetInMemoryDiscInfo(string MetaURL, out BasisBEEExtensionMeta info)
    {
        BasisBEEExtensionMeta legacyCandidate = null;
        string canonicalUrl = BasisIOManagement.CanonicalizeRemoteUrl(MetaURL);

        foreach (var discInfo in OnDiscData.Values)
        {
            if (BasisIOManagement.CanonicalizeRemoteUrl(discInfo.StoredRemote.RemoteBeeFileLocation) == canonicalUrl)
            {
                if (!string.IsNullOrWhiteSpace(discInfo.DownloadedPlatform) &&
                    !BasisIOManagement.CachePlatformMatchesCurrent(discInfo.DownloadedPlatform))
                {
                    continue;
                }

                if (HasAnyCachedPayload(discInfo))
                {
                    if (BasisIOManagement.CachePlatformMatchesCurrent(discInfo.DownloadedPlatform))
                    {
                        info = discInfo;
                        return true;
                    }

                    legacyCandidate = discInfo;
                }
            }
        }

        if (legacyCandidate != null)
        {
            info = legacyCandidate;
            return true;
        }

        info = null;
        return false;
    }

    public static bool IsMetaDataOnDisc(string MetaURL, out BasisBEEExtensionMeta info)
    {
        if (TryGetInMemoryDiscInfo(MetaURL, out info))
        {
            return true;
        }

        string currentPlatform = BasisIOManagement.GetCurrentCachePlatform();
        if (TryLazyLoadDiscInfo(MetaURL, currentPlatform, out BasisBEEExtensionMeta lazyLoadedInfo))
        {
            info = lazyLoadedInfo;
            return true;
        }

        info = new BasisBEEExtensionMeta();
        return false;
    }

    public static Task<(bool found, BasisBEEExtensionMeta info)> IsMetaDataOnDiscAsync(string MetaURL)
    {
        if (TryGetInMemoryDiscInfo(MetaURL, out BasisBEEExtensionMeta inMemory))
        {
            return Task.FromResult((true, inMemory));
        }

        return Task.Run<(bool, BasisBEEExtensionMeta)>(() =>
        {
            string currentPlatform = BasisIOManagement.GetCurrentCachePlatform();
            if (TryLazyLoadDiscInfo(MetaURL, currentPlatform, out BasisBEEExtensionMeta lazyInfo))
            {
                return (true, lazyInfo);
            }
            return (false, new BasisBEEExtensionMeta());
        });
    }

    public static async Task AddDiscInfo(BasisBEEExtensionMeta discInfo)
    {
        string discKey = GetDiscInfoKey(discInfo.StoredRemote.RemoteBeeFileLocation, discInfo.DownloadedPlatform);
        OnDiscData[discKey] = discInfo;
        string filePath = BasisIOManagement.GetMetaCacheFilePath(discInfo.UniqueVersion, discInfo.DownloadedPlatform);
        byte[] serializedData = BasisSerialization.SerializeValue(discInfo);

        try
        {
            if (!string.IsNullOrWhiteSpace(discInfo.UniqueVersion))
            {
                string legacyMetaPath = BasisIOManagement.GetLegacyMetaCacheFilePath(discInfo.UniqueVersion);
                if (File.Exists(legacyMetaPath))
                {
                    File.Delete(legacyMetaPath);
                }
            }
            string tempPath = filePath + ".tmp";
            await File.WriteAllBytesAsync(tempPath, serializedData);
            if (File.Exists(filePath))
            {
                File.Replace(tempPath, filePath, null);
            }
            else
            {
                File.Move(tempPath, filePath);
            }
            BasisDebug.Log($"Disc info saved to {filePath}", BasisDebug.LogTag.Event);
        }
        catch (Exception ex)
        {
            BasisDebug.LogError($"Failed to save disc info: {ex.Message}", BasisDebug.LogTag.Event);
        }
    }

    public static void RemoveDiscInfo(string metaUrl)
    {
        BasisStorageManagement.DeleteStoredFile(metaUrl);
    }

    public static async Task EnsureInitializationComplete()
    {
        if (!IsInitialized)
        {
            await _initSemaphore.WaitAsync();
            try
            {
                if (!IsInitialized)
                {
                    await LoadAllDiscData();
                    IsInitialized = true;
                }
            }
            finally
            {
                _initSemaphore.Release();
            }
        }
    }

    private static async Task LoadAllDiscData()
    {
        BasisDebug.Log("Loading all disc data...", BasisDebug.LogTag.Event);
        string path = BasisIOManagement.GenerateFolderPath(BasisBeeConstants.AssetBundlesFolder);
        string[] files = Directory.GetFiles(path, $"*{BasisBeeConstants.BasisMetaExtension}");

        List<Task> loadTasks = new List<Task>();

        foreach (string file in files)
        {
            loadTasks.Add(Task.Run(async () =>
            {
               // BasisDebug.Log($"Loading file: {file}");
                try
                {
                    byte[] fileData = await File.ReadAllBytesAsync(file);
                    BasisBEEExtensionMeta discInfo = BasisSerialization.DeserializeValue<BasisBEEExtensionMeta>(fileData);
                    OnDiscData[GetDiscInfoKey(discInfo.StoredRemote.RemoteBeeFileLocation, discInfo.DownloadedPlatform)] = discInfo;
                }
                catch (Exception ex)
                {
                    BasisDebug.LogError($"Failed to load disc info from {file}: {ex.Message}", BasisDebug.LogTag.Event);
                    File.Delete(file);
                }
            }));
        }

        await Task.WhenAll(loadTasks);

        BasisDebug.Log("Completed loading all disc data.");
    }

    private static void CleanupFiles(BasisStoredEncryptedBundle bundle)
    {
        if (!string.IsNullOrWhiteSpace(bundle?.DownloadedBeeFileLocation) && File.Exists(bundle.DownloadedBeeFileLocation))
        {
            File.Delete(bundle.DownloadedBeeFileLocation);
        }
    }
}
