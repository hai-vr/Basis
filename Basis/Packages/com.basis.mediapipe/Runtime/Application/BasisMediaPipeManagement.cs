using System.Collections.Generic;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices.Simulation;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

namespace Basis.MediaPipe
{
    /// <summary>
    /// Device source that turns webcam MediaPipe landmarks into fake Basis trackers,
    /// finger data, eye gaze and face blendshapes. Add this to the BasisDeviceManagement
    /// GameObject and include it in its BaseTypes list; its per-frame work is driven by
    /// BasisDeviceManagement.Simulate() (the central tick). Inert until the homuler
    /// MediaPipe Unity Plugin is installed (see README).
    /// </summary>
    public class BasisMediaPipeManagement : BasisBaseTypeManagement
    {
        public const string SubSystem = "BasisMediaPipe";

        public static BasisMediaPipeManagement Instance { get; private set; }

        public string CameraDeviceName = string.Empty;
        public BasisMediaPipeConfig Config = BasisMediaPipeConfig.Default;

        private readonly BasisMediaPipeCamera _camera = new BasisMediaPipeCamera();
        private readonly Dictionary<BasisBoneTrackedRole, BasisInputXRSimulate> _trackers = new();
        private readonly MediaPipeFaceConverter _faceConverter = new MediaPipeFaceConverter();
        private readonly MediaPipeHandConverter _handConverter = new MediaPipeHandConverter();
        private readonly MediaPipeHeadConverter _headConverter = new MediaPipeHeadConverter();
        private IBasisMediaPipeBackend _backend;
        private BasisMediaPipeResult _latest;
        private bool _hasLatest;
        public override bool IsDeviceBootable(string BootRequest) => BootRequest == SubSystem;

        /// <summary>Finds or creates the manager on the BasisDeviceManagement object and registers it for ticking.</summary>
        public static BasisMediaPipeManagement GetOrCreate()
        {
            if (Instance != null) return Instance;

            BasisDeviceManagement dm = BasisDeviceManagement.Instance;
            if (dm == null) return null;

            BasisMediaPipeManagement mgr = dm.GetComponent<BasisMediaPipeManagement>();
            if (mgr == null) mgr = dm.gameObject.AddComponent<BasisMediaPipeManagement>();
            if (dm.BaseTypes != null && !dm.BaseTypes.Contains(mgr)) dm.BaseTypes.Add(mgr);

            Instance = mgr;
            mgr.ApplySettings();
            return mgr;
        }

        /// <summary>Pulls persisted settings into Config and restarts the backend if it is already running.</summary>
        public void ApplySettings()
        {
            CameraDeviceName = BasisMediaPipeSettings.Camera.RawValue;
            Config.EnableFace = BasisMediaPipeSettings.EnableFace.RawValue;
            Config.EnableHands = BasisMediaPipeSettings.EnableHands.RawValue;
            Config.EnableHead = BasisMediaPipeSettings.EnableHead.RawValue;
            Config.EnableHandTracking = BasisMediaPipeSettings.EnableHandTracking.RawValue;
            Config.SwapHands = BasisMediaPipeSettings.SwapHands.RawValue;
            Config.MirrorHorizontally = BasisMediaPipeSettings.Mirror.RawValue;
            ApplyTuning();

            if (_backend != null)
            {
                StopSDK();
                StartSDK();
            }
        }

        /// <summary>Applies converter sign/gain tuning without restarting the backend.</summary>
        public void ApplyTuning()
        {
            _faceConverter.EyeLidIsOpenness = !BasisMediaPipeSettings.InvertBlink.RawValue;
            _headConverter.InvertYaw = BasisMediaPipeSettings.InvertHeadYaw.RawValue;
            _headConverter.InvertPitch = BasisMediaPipeSettings.InvertHeadPitch.RawValue;
            _headConverter.Smoothing = BasisMediaPipeSettings.HeadSmoothing.RawValue;
            _faceConverter.Smoothing = BasisMediaPipeSettings.FaceSmoothing.RawValue;
            _handConverter.PoseSmoothing = BasisMediaPipeSettings.HandSmoothing.RawValue;
            _handConverter.FingerSmoothing = BasisMediaPipeSettings.FingerSmoothing.RawValue;
        }

        public void SetEnabled(bool value)
        {
            if (value)
            {
                if (_backend == null) StartSDK();
            }
            else
            {
                StopSDK();
            }
        }

        public void SetCamera(string deviceName)
        {
            CameraDeviceName = deviceName;
            if (_backend != null)
            {
                _camera.Start(deviceName, 640, 480, Config.TargetFps);
            }
        }

        public override void StartSDK()
        {

            _backend = BasisMediaPipeBackendRegistry.Create();
            _backend.Initialize(Config);
            BasisDebug.Log($"BasisMediaPipe: backend = {_backend.BackendName}.");

            if (!_backend.IsAvailable)
            {
                BasisDebug.LogError("BasisMediaPipe: MediaPipe plugin not installed; tracking inert. See package README.");
                return;
            }

            if (!_camera.Start(CameraDeviceName, 640, 480, Config.TargetFps))
            {
                BasisDebug.LogError("BasisMediaPipe: failed to start webcam.");
            }
        }

        public override void StopSDK()
        {
            _camera.Stop();
            DestroyAllTrackers();
            _backend?.Shutdown();
            _backend = null;
            _hasLatest = false;
        }

        public override void Simulate()
        {
            if (_backend == null || !_backend.IsAvailable)
            {
                return;
            }

            if (BasisLocalPlayer.Instance == null) return;

            if (_camera.IsReady)
            {
                _backend.SubmitFrame(_camera.Texture, Time.realtimeSinceStartupAsDouble * 1000.0);
            }

            if (_backend.TryGetLatestResult(out BasisMediaPipeResult result))
            {
                _latest = result;
                _hasLatest = true;
            }

            if (_hasLatest)
            {
                ApplyResult(in _latest);
            }
        }

        private void ApplyResult(in BasisMediaPipeResult result)
        {
            if (Config.EnableFace && result.HasFace)
            {
                _faceConverter.Apply(in result, BasisLocalPlayer.Instance.BasisAvatar);
            }
            if (Config.EnableHands)
            {
                _handConverter.Apply(in result);
            }

            if (Config.EnableHead)
            {
                if (result.HasFace && BasisLocalBoneDriver.EyeControl != null
                    && _headConverter.TryGetHeadLocalRotation(in result, out Quaternion headOffset))
                {
                    BasisLocalBoneControl eye = BasisLocalBoneDriver.EyeControl;
                    EnsureTracker(BasisBoneTrackedRole.Head).FollowMovement.SetLocalPositionAndRotation(
                        eye.OutGoingData.position, eye.OutGoingData.rotation * headOffset);
                }
            }
            else
            {
                RemoveTracker(BasisBoneTrackedRole.Head);
            }

            if (Config.EnableHandTracking)
            {
                ApplyHandTracker(in result, true);
                ApplyHandTracker(in result, false);
            }
            else
            {
                RemoveTracker(BasisBoneTrackedRole.LeftHand);
                RemoveTracker(BasisBoneTrackedRole.RightHand);
            }
        }

        private void ApplyHandTracker(in BasisMediaPipeResult result, bool left)
        {
            Vector3[] landmarks = left ? result.LeftHandLandmarks : result.RightHandLandmarks;
            bool detected = left ? result.HasLeftHand : result.HasRightHand;
            BasisBoneTrackedRole role = left ? BasisBoneTrackedRole.LeftHand : BasisBoneTrackedRole.RightHand;

            if (!detected || landmarks == null)
            {
                RemoveTracker(role);
                return;
            }

            float chestHeight = BasisLocalBoneDriver.ChestControl != null
                ? BasisLocalBoneDriver.ChestControl.TposeLocal.position.y : 1.2f;
            if (_handConverter.TryGetHandTarget(landmarks, chestHeight, left, out Vector3 position, out Quaternion rotation))
            {
                EnsureTracker(role).FollowMovement.SetLocalPositionAndRotation(position, rotation);
            }
        }

        public void CalibrateHead()
        {
            if (_hasLatest) _headConverter.Calibrate(_latest);
        }

        private void RemoveTracker(BasisBoneTrackedRole role)
        {
            if (_trackers.TryGetValue(role, out BasisInputXRSimulate input) && input != null)
            {
                BasisDeviceManagement.Instance.RemoveDevicesFrom(SubSystem, $"{SubSystem}:{role}");
            }
            _trackers.Remove(role);
        }

        private BasisInputXRSimulate EnsureTracker(BasisBoneTrackedRole role)
        {
            if (_trackers.TryGetValue(role, out BasisInputXRSimulate existing) && existing != null)
            {
                return existing;
            }

            string id = $"{SubSystem}:{role}";
            GameObject go = new GameObject(id) { transform = { parent = BasisLocalPlayer.Instance.transform } };
            Transform move = new GameObject(id + " move").transform;
            move.parent = BasisLocalPlayer.Instance.transform;

            BasisInputXRSimulate input = go.AddComponent<BasisInputXRSimulate>();
            input.FollowMovement = move;
            input.InitalizeTracking(id, SubSystem, SubSystem, true, role);
            BasisDeviceManagement.Instance.TryAdd(input);

            _trackers[role] = input;
            return input;
        }

        private void DestroyAllTrackers()
        {
            foreach (KeyValuePair<BasisBoneTrackedRole, BasisInputXRSimulate> kvp in _trackers)
            {
                if (kvp.Value != null)
                {
                    BasisDeviceManagement.Instance.RemoveDevicesFrom(SubSystem, $"{SubSystem}:{kvp.Key}");
                }
            }
            _trackers.Clear();
        }
    }
}
