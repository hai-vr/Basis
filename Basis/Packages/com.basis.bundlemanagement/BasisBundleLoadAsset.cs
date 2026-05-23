using System.Collections.Generic;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk;
using UnityEngine;
using UnityEngine.SceneManagement;
using static BundledContentHolder;
public static class BasisBundleLoadAsset
{
    public static async Task<GameObject> LoadFromWrapper(GameObject DisabledGameobject,BasisTrackedBundleWrapper BasisLoadableBundle, bool UseContentRemoval, Vector3 Position, Quaternion Rotation, bool ModifyScale, Vector3 Scale, Selector Selector, Transform Parent = null, bool DestroyColliders = false,bool ChangeColidersToCorrectLayer = false, List<BasisHeadChop.HeadChopTarget> HarvestedHeadChop = null)
    {
        bool Incremented = false;
        if (BasisLoadableBundle.AssetBundle != null)
        {
            BasisLoadableBundle output = BasisLoadableBundle.LoadableBundle;
            if (output.BasisBundleConnector.GetPlatform(out BasisBundleGenerated Generated))
            {
                switch (Generated.AssetMode)
                {
                    case "GameObject":
                        {
                            string ReplacedName = Generated.AssetToLoadName.Replace(".bundle", ".prefab");

                            AssetBundleRequest Request = BasisLoadableBundle.AssetBundle.LoadAssetAsync<GameObject>(ReplacedName);
                            await Request;
                            GameObject loadedObject = Request.asset as GameObject;
                            if (loadedObject == null)
                            {
                                BasisDebug.LogError("Unable to proceed, null Gameobject for request " + Generated.AssetToLoadName);

                                string[] assetNames = BasisLoadableBundle.AssetBundle.GetAllAssetNames();
                                BasisDebug.LogError("All assets in bundle: \n" + string.Join("\n", assetNames));

                                BasisLoadableBundle.DidErrorOccur = true;
                                await BasisLoadableBundle.AssetBundle.UnloadAsync(true);
                                return null;
                            }
                            ChecksRequired ChecksRequired = new ChecksRequired();
                            if (loadedObject.TryGetComponent<BasisAvatar>(out BasisAvatar BasisAvatar))
                            {
                                ChecksRequired.DisableAnimatorEvents = true;
                            }
                            ChecksRequired.UseContentRemoval = UseContentRemoval;
                            ChecksRequired.RemoveColliders = DestroyColliders;
                            ChecksRequired.ChangeCollidersToCorrectLayer = ChangeColidersToCorrectLayer;
                            ChecksRequired.ScrubPersistentUnityEvents = true;
                            BasisContentHarvest harvest = BasisAvatar != null ? new BasisContentHarvest() : null;
                            GameObject CreatedCopy = ContentPoliceControl.ContentControl(DisabledGameobject,loadedObject, ChecksRequired, Position, Rotation, ModifyScale, Scale, Selector, Parent, LayerMask.NameToLayer("IgnoredByInteractable"), HarvestedHeadChop, harvest);
                            if (harvest != null && CreatedCopy != null && CreatedCopy.TryGetComponent(out Basis.Scripts.BasisSdk.BasisAvatar createdAvatar))
                            {
                                createdAvatar.Harvest = harvest;
                            }
                            Incremented = BasisLoadableBundle.Increment();
                            string InstanceID = BasisGenerateUniqueID.GenerateUniqueID();
                            CreatedCopy.name = InstanceID + Incremented;
                            return CreatedCopy;
                        }
                    default:
                        BasisDebug.LogError("Requested type " + Generated.AssetMode + " has no handler");
                        return null;
                }
            }
            else
            {
                BasisDebug.LogError("Missing Platform Bundle! can't find : " + Application.platform);
            }
        }
        else
        {
            BasisDebug.LogError("Missing Bundle!");
        }
        BasisDebug.LogError("Returning unable to load gameobject!");
        return null;
    }
    public static async Task<Scene> LoadSceneFromBundleAsync(BasisTrackedBundleWrapper bundle, bool MakeActiveScene, BasisProgressReport progressCallback)
    {
        string UniqueID = BasisGenerateUniqueID.GenerateUniqueID();
        bool AssignedIncrement = false;
        string[] scenePaths = bundle.AssetBundle.GetAllScenePaths();
        if (scenePaths.Length == 0)
        {
            BasisDebug.LogError("No scenes found in AssetBundle.");
            return new Scene();
        }
        if (scenePaths.Length > 1)
        {
            BasisDebug.LogError("More then one scene was found in The Asset Bundle, Please Correct!");
            return new Scene();
        }

        if (!string.IsNullOrEmpty(scenePaths[0]))
        {
            string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePaths[0]);
            // Load the scene asynchronously
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(scenePaths[0], LoadSceneMode.Additive);
            asyncLoad.allowSceneActivation = true;
            while (!asyncLoad.isDone)
            {
                progressCallback.ReportProgress(UniqueID, 50 + asyncLoad.progress * 50, $"Activating scene {sceneName}");
                await Task.Yield();
            }

            BasisDebug.Log("Scene loaded successfully from AssetBundle.");
            Scene loadedScene = SceneManager.GetSceneByPath(scenePaths[0]);
            bundle.MetaLink = loadedScene.path;
            // Set the loaded scene as the active scene
            if (loadedScene.IsValid())
            {
                ChecksRequired ChecksRequired = new ChecksRequired();
                ChecksRequired.UseContentRemoval = true;
                ChecksRequired.ScrubPersistentUnityEvents = true;
                ContentPoliceControl.ContentControl(ChecksRequired, Selector.World, loadedScene, true);
                AssignedIncrement = bundle.Increment();
                if (MakeActiveScene)
                {
                    SceneManager.SetActiveScene(loadedScene);
                    BasisDebug.Log("Scene set as active: " + loadedScene.name);
                }
                BasisDebug.Log("Scene loaded: " + loadedScene.name + " (MakeActive=" + MakeActiveScene + ", Incremented=" + AssignedIncrement + ")");
#if UNITY_BUNDLEUNLOAD
                bundle.ReleaseBundleBackingStore();
#endif
                progressCallback.ReportProgress(UniqueID, 100, $"Loaded scene {sceneName}");
                return loadedScene;
            }
            else
            {
                BasisDebug.LogError("Failed to get loaded scene.");
            }
        }
        else
        {
            BasisDebug.LogError("Path was null or empty! this should not be happening!");
        }
        return new Scene();
    }
}
