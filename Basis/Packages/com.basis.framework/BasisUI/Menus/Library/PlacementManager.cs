using System;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Basis.BasisUI
{
    public static class PlacementManager
    {
        // Wait until trigger crosses a threshold (debounced).
        private const float triggerDownThreshold = 0.75f;
        private const float triggerUpThreshold = 0.20f;

        public static BasisInput PlacementInput;
        public static GameObject PlacementCube;
        public static string PlacementAddress = "Packages/com.basis.sdk/Prefabs/SpawnOutline.prefab";

        private static Action _triggerHandler;
        private static TaskCompletionSource<(Vector3 pos, Quaternion rot, Vector3 scale)> _tcs;
        private static bool _wasDown = false;

        public static async Task<(Vector3 pos, Quaternion rot, Vector3 scale)> BeginPlacement(BasisInput input, Vector3 extents)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            PlacementInput = input;

            // Enter placement mode on the raycaster
            PlacementInput.BasisPointRaycaster.EnterPlacementMode(extents);

            // Add update loop
            BasisLocalPlayer.AfterSimulateOnLate.AddAction(122, VisualDrive);

            _tcs = new TaskCompletionSource<(Vector3, Quaternion, Vector3)>(TaskCreationOptions.RunContinuationsAsynchronously);
            _wasDown = false;

            _triggerHandler = PlacementOnTriggerChanged;
            try { PlacementInput.CurrentInputState.OnTriggerChanged += _triggerHandler; } catch { }

            try
            {
                return await _tcs.Task;
            }
            finally
            {
                CancelPlacement();
            }
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
                {
                    _tcs.TrySetCanceled();
                }
            }
            catch { }

            _tcs = null;
            _triggerHandler = null;
            _wasDown = false;
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
                UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject> op = Addressables.LoadAssetAsync<GameObject>(PlacementAddress);
                GameObject go = op.WaitForCompletion();
                PlacementCube = GameObject.Instantiate(go, BasisDeviceManagement.Instance.transform);
                PlacementCube.name = "Placement";
            }

            if (PlacementInput != null && PlacementInput.BasisPointRaycaster.TryGetPlacement(out var placement))
            {
                if (placement.HasHit)
                {
                    PlacementCube.gameObject.SetActive(true);
                    PlacementCube.transform.SetPositionAndRotation(placement.Center, placement.Rotation);
                    PlacementCube.transform.localScale = placement.Extents;
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
                        _tcs.TrySetResult((placement.Center, placement.Rotation, Vector3.one));
                        CancelPlacement();
                        return;
                    }

                    Transform tr = PlacementInput.transform;
                    Vector3 fallbackPos = tr.position + tr.forward * 0.75f;
                    Quaternion fallbackRot = Quaternion.LookRotation(tr.forward, Vector3.up);
                    _tcs.TrySetResult((fallbackPos, fallbackRot, Vector3.one));
                    CancelPlacement();
                    return;
                }

                if (_wasDown && t <= triggerUpThreshold)
                {
                    _wasDown = false;
                }
            }
            catch (Exception ex)
            {
                BasisDebug.LogError(ex);
                try { if (_tcs != null && !_tcs.Task.IsCompleted) _tcs.TrySetException(ex); } catch { }
                CancelPlacement();
            }
        }
    }
}
