using System;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Basis.BasisUI
{
    /// <summary>
    /// Used by the LibraryProvider.cs when a item is desired to spawn with a raycast placement
    /// </summary>
    public static class PlacementManager
    {
        public static string SpawnOutlineAddress = "Packages/com.basis.sdk/Prefabs/SpawnOutline.prefab";
        
        private static readonly int OutlineColorID = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineThickness = Shader.PropertyToID("_OutlineThickness");
        private static readonly int OutlineCornerScale = Shader.PropertyToID("_OutlineCornerScale");
        private static readonly int OutlineDotScale = Shader.PropertyToID("_CenterDotScale");

        private static void SetOutlineColor(GameObject target, Color color)
        {
            foreach (Renderer r in target.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material mat in r.materials)
                {
                    if (mat.HasProperty(OutlineColorID))
                    {
                        mat.SetColor(OutlineColorID, color);
                    }
                }
            }
        }

        private static void UpdateOutlineScale(GameObject targetOutlineGameObject)
        {
            float scale = targetOutlineGameObject.transform.localScale.magnitude/10;
            foreach (Renderer r in targetOutlineGameObject.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material mat in r.materials)
                {
                    if (mat.HasProperty(OutlineCornerScale))
                    {
                        mat.SetFloat(OutlineCornerScale, scale);
                    }

                    if(mat.HasProperty(OutlineDotScale))
                    {
                        mat.SetFloat(OutlineDotScale, scale/2);
                    }

                    if(mat.HasProperty(OutlineThickness))
                    {
                        mat.SetFloat(OutlineThickness, scale/5);
                    }
                }
            }
        }

        #region PLACEMENT
        private const float triggerDownThreshold = 0.75f;
        private const float triggerUpThreshold = 0.20f;

        public static BasisInput PlacementInput;
        private static GameObject PlacementCube;

        private static Action _triggerHandler;
        private static TaskCompletionSource<(Vector3 pos, Quaternion rot, Vector3 scale)> _tcs;
        private static bool _wasDown = false;

        // NEW: store placement metadata for finalize step
        private static Vector3 _localBoundsCenter;
        private static Vector3 _halfExtents;

        public static async Task<(Vector3 pos, Quaternion rot, Vector3 scale)> BeginPlacement(
            BasisInput input,
            Vector3 halfExtents,
            Vector3 localBoundsCenter)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            PlacementInput = input;

            // Store for finalize step
            _localBoundsCenter = localBoundsCenter;
            _halfExtents = halfExtents;

            // Enter placement mode on the raycaster (expects HALF-extents)
            PlacementInput.BasisPointRaycaster.EnterPlacementMode(halfExtents, localBoundsCenter);

            BasisLocalPlayer.AfterSimulateOnLate.AddAction(122, VisualDrive);

            _tcs = new TaskCompletionSource<(Vector3, Quaternion, Vector3)>(TaskCreationOptions.RunContinuationsAsynchronously);
            _wasDown = false;

            _triggerHandler = PlacementOnTriggerChanged;
            try { PlacementInput.CurrentInputState.OnTriggerChanged += _triggerHandler; } catch { }

            try { return await _tcs.Task; }
            finally { CancelPlacement(); }
        }

        public static void CancelPlacement()
        {
            try { if (_triggerHandler != null && PlacementInput?.CurrentInputState != null) PlacementInput.CurrentInputState.OnTriggerChanged -= _triggerHandler; } catch { }
            try { PlacementInput?.BasisPointRaycaster?.ExitPlacementMode(); } catch { }
            try { BasisLocalPlayer.AfterSimulateOnLate.RemoveAction(122, VisualDrive); } catch { }
            try { Addressables.ReleaseInstance(PlacementCube); } catch { }
            try { if (PlacementCube != null) GameObject.Destroy(PlacementCube); } catch { }

            try
            {
                if (_tcs != null && !_tcs.Task.IsCompleted)
                    _tcs.TrySetCanceled();
            }
            catch { }

            _tcs = null;
            _triggerHandler = null;
            _wasDown = false;

            // NEW: clear stored data
            _localBoundsCenter = default;
            _halfExtents = default;

            PlacementInput = null;
        }

        public static void VisualDrive()
        {
            if (BasisMainMenu.Instance != null)
            {
                CancelPlacement();
                return;
            }

            if (PlacementCube == null)
            {
                var op = Addressables.LoadAssetAsync<GameObject>(SpawnOutlineAddress);
                GameObject go = op.WaitForCompletion();
                PlacementCube = GameObject.Instantiate(go, BasisDeviceManagement.Instance.transform);
                PlacementCube.name = "Placement Outline";

                SetOutlineColor(PlacementCube, Color.purple);
            }

            if (PlacementInput != null && PlacementInput.BasisPointRaycaster.TryGetPlacement(out var placement))
            {
                if (placement.HasHit)
                {
                    PlacementCube.gameObject.SetActive(true);
                    PlacementCube.transform.SetPositionAndRotation(placement.Center, placement.Rotation);

                    // placement.Extents is HALF-size; preview cube scale expects FULL size
                    PlacementCube.transform.localScale = placement.Extents * 2f;

                    // update its selection
                    UpdateOutlineScale(PlacementCube);
                }
                else
                {
                    PlacementCube.gameObject.SetActive(false);
                }
            }
        }

        private static void PlacementOnTriggerChanged()
        {
            try
            {
                if (PlacementInput?.CurrentInputState == null || _tcs == null) return;

                float t = PlacementInput.CurrentInputState.Trigger;

                if (!_wasDown && t >= triggerDownThreshold)
                {
                    _wasDown = true;

                    if (PlacementInput.BasisPointRaycaster.TryGetPlacement(out var placement))
                    {
                        // Place the object so the BOTTOM of its bounds touches the hit point.
                        // local bottom = localCenter - up * halfHeight
                        Vector3 localBottom = _localBoundsCenter - Vector3.up * _halfExtents.y;

                        // Solve for pivot position such that pivot + rot*localBottom == hit.point
                        Vector3 spawnPos = placement.Hit.point - (placement.Rotation * localBottom);

                        _tcs.TrySetResult((spawnPos, placement.Rotation, Vector3.one));
                        CancelPlacement();
                        return;
                    }

                    // if (PlacementInput.BasisPointRaycaster.TryGetPlacement(out var placement))
                    // {
                    //     // placement.Center is already the world-space pivot position with bottom
                    //     // touching the surface, and placement.Rotation is already surface-aligned.
                    //     // ComputePlacementOBB handles all the offset math — just use it directly.
                    //     _tcs.TrySetResult((placement.Center, placement.Rotation, Vector3.one));
                    //     CancelPlacement();
                    //     return;
                    // }

                    Transform tr = PlacementInput.transform;
                    Vector3 fallbackPos = tr.position + tr.forward * 0.75f;
                    Quaternion fallbackRot = Quaternion.LookRotation(tr.forward, Vector3.up);
                    _tcs.TrySetResult((fallbackPos, fallbackRot, Vector3.one));
                    CancelPlacement();
                    return;
                }

                if (_wasDown && t <= triggerUpThreshold)
                    _wasDown = false;
            }
            catch (Exception ex)
            {
                BasisDebug.LogError(ex);
                try { if (_tcs != null && !_tcs.Task.IsCompleted) _tcs.TrySetException(ex); } catch { }
                CancelPlacement();
            }
        }
    
        #endregion

        #region SELECTION

        //private static BasisRuntimeSpawnRegistry.SpawnInstance selectedInstance;
        private static BasisRuntimeSpawnRegistry.SpawnInstance selectedInstance; 
        public static BasisRuntimeSpawnRegistry.SpawnInstance ActiveInstance { get => selectedInstance; }
        private static GameObject selectedGameObjectRef = null;
        private static GameObject selectionGameObjectRef = null;

        public static void SetActiveSelection(BasisRuntimeSpawnRegistry.SpawnInstance spawnInstance)
        {
            if(spawnInstance == null) return;

            if (BasisRuntimeSpawnRegistry.SpawnedGameobjects.TryGetValue(spawnInstance.LoadedNetID, out GameObject go) && go != null)
            {
                // lets attempt to grab the meta data first once we have the gameobject ref
                BasisBundleConnector basisBundleConnector = spawnInstance.bundleConnector;

                // null check meta data
                if(basisBundleConnector != null)
                {
                    // set the selected instance on success of finding the object
                    selectedInstance = spawnInstance;

                    // grab the game-object via ID
                    selectedGameObjectRef = go;

                    // lets create the selection cube
                    if (selectionGameObjectRef == null)
                    {
                        var op = Addressables.LoadAssetAsync<GameObject>(SpawnOutlineAddress);
                        GameObject assignedGO = op.WaitForCompletion();
                        selectionGameObjectRef = GameObject.Instantiate(assignedGO, BasisDeviceManagement.Instance.transform);

                        // change the colour
                        SetOutlineColor(selectionGameObjectRef, Color.cyan);

                        selectionGameObjectRef.name = "Selection Outline";

                        Vector3 worldCenter = go.transform.TransformPoint(basisBundleConnector.Bounds.center);
                        selectionGameObjectRef.transform.SetPositionAndRotation(worldCenter, go.transform.rotation);
                        selectionGameObjectRef.transform.localScale = basisBundleConnector.Bounds.size;
                        
                        UpdateOutlineScale(selectionGameObjectRef);
                    }
                    else
                    {
                        Vector3 worldCenter = go.transform.TransformPoint(basisBundleConnector.Bounds.center);
                        selectionGameObjectRef.transform.SetPositionAndRotation(worldCenter, go.transform.rotation);
                        selectionGameObjectRef.transform.localScale = basisBundleConnector.Bounds.size;

                        UpdateOutlineScale(selectionGameObjectRef);
                    }
                }
                else
                {
                    BasisDebug.LogWarning($"PlacementManager.cs was unable to properly SetActiveSelection(spawnInstance = {spawnInstance.Url}) basisBundleConnector is missing?");
                }

            }

        }

        public static void RemoveSelectionSpawnInstanceID(BasisRuntimeSpawnRegistry.SpawnInstance spawnInstance)
        {
            if(selectedInstance == null) return;
            if(spawnInstance == null) return;

            if(spawnInstance.LoadedNetID == selectedInstance.LoadedNetID)
            {
                RemoveActiveSelection();
            }
        }

        public static void RemoveActiveSelection()
        {
            // if the selected instance is not null meaning we have a selection
            if(selectedInstance != null)
            {
                // delete the selection game object
                if(selectionGameObjectRef != null)
                {
                    GameObject.Destroy(selectionGameObjectRef);
                }

                // set the selected instance to null
                selectedInstance = null;
            }
        }

        #endregion

    }
}
