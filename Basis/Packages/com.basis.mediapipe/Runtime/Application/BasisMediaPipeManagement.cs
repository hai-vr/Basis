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
        public string CameraDeviceName = string.Empty;
        public BasisMediaPipeConfig Config = BasisMediaPipeConfig.Default;

        private readonly BasisMediaPipeCamera _camera = new BasisMediaPipeCamera();
        private readonly Dictionary<BasisBoneTrackedRole, BasisInputXRSimulate> _trackers = new();
        private readonly HashSet<BasisBoneTrackedRole> _activeTrackers = new();
        private readonly MediaPipeFaceConverter _faceConverter = new MediaPipeFaceConverter();
        private readonly MediaPipeHandConverter _handConverter = new MediaPipeHandConverter();
        private readonly MediaPipeHeadConverter _headConverter = new MediaPipeHeadConverter();
        private readonly MediaPipeBodyConverter _bodyConverter = new MediaPipeBodyConverter();
        private readonly MediaPipeArmConverter _armConverter = new MediaPipeArmConverter();
        private IBasisMediaPipeBackend _backend;
        private BasisMediaPipeResult _latest;
        private bool _hasLatest;

        private Transform _hipsBone, _headBone, _leftUpperArm, _leftLowerArm, _leftHandBone, _rightUpperArm, _rightLowerArm, _rightHandBone;
        private float _leftUpperLen, _leftForeLen, _rightUpperLen, _rightForeLen;
        private bool _armRigValid;
        private bool _armPosePathActive;
        private bool _armDiagLogged;
        public override bool IsDeviceBootable(string BootRequest) => BootRequest == SubSystem;

        /// <summary>Pulls persisted settings into Config and restarts the backend if it is already running.</summary>
        public void ApplySettings()
        {
            LoadSettingsIntoConfig();
            if (_backend != null)
            {
                StopSDK();
                StartSDK();
            }
        }

        private void LoadSettingsIntoConfig()
        {
            BasisMediaPipeSettings.LoadAll();
            CameraDeviceName = BasisMediaPipeSettings.Camera.RawValue;
            Config.EnableFace = BasisMediaPipeSettings.EnableFace.RawValue;
            Config.EnableHands = BasisMediaPipeSettings.EnableHands.RawValue;
            Config.EnableHeadPosition = BasisMediaPipeSettings.EnableHeadPosition.RawValue;
            Config.EnableHeadRotation = BasisMediaPipeSettings.EnableHeadRotation.RawValue;
            Config.EnableHandTracking = BasisMediaPipeSettings.EnableHandTracking.RawValue;
            Config.SwapHands = BasisMediaPipeSettings.SwapHands.RawValue;
            Config.MirrorHorizontally = BasisMediaPipeSettings.Mirror.RawValue;
            Config.CameraWidth = BasisMediaPipeSettings.ResolutionWidth.RawValue;
            Config.CameraHeight = BasisMediaPipeSettings.ResolutionHeight.RawValue;
            Config.TargetFps = BasisMediaPipeSettings.CameraFps.RawValue;
            Config.EnableChest = BasisMediaPipeSettings.EnableBody.RawValue;
            Config.EnableArmElbowPole = BasisMediaPipeSettings.EnableArmElbowPole.RawValue;
            Config.EnablePose = Config.EnableChest || Config.EnableHandTracking;
            ApplyTuning();
        }

        /// <summary>Applies converter sign/gain tuning without restarting the backend.</summary>
        public void ApplyTuning()
        {
            _faceConverter.EyeLidIsOpenness = !BasisMediaPipeSettings.InvertBlink.RawValue;
            _headConverter.InvertYaw = BasisMediaPipeSettings.InvertHeadYaw.RawValue;
            _headConverter.InvertPitch = BasisMediaPipeSettings.InvertHeadPitch.RawValue;
            _faceConverter.InvertEyeX = BasisMediaPipeSettings.InvertHeadYaw.RawValue;
            _faceConverter.InvertEyeY = BasisMediaPipeSettings.InvertHeadPitch.RawValue;
            _headConverter.Smoothing = BasisMediaPipeSettings.HeadSmoothing.RawValue;
            _faceConverter.Smoothing = BasisMediaPipeSettings.FaceSmoothing.RawValue;
            _faceConverter.TongueGain = BasisMediaPipeSettings.EnableTongue.RawValue
                ? BasisMediaPipeSettings.TongueStrength.RawValue
                : 0f;
            _handConverter.PoseSmoothing = BasisMediaPipeSettings.HandSmoothing.RawValue;
            _handConverter.FingerSmoothing = BasisMediaPipeSettings.FingerSmoothing.RawValue;
            _handConverter.UseRotation = BasisMediaPipeSettings.HandRotation.RawValue;
            _armConverter.Smoothing = BasisMediaPipeSettings.HandSmoothing.RawValue;
            _armConverter.SwapArms = BasisMediaPipeSettings.SwapArms.RawValue;
            _armConverter.InvertForward = BasisMediaPipeSettings.InvertArmDepth.RawValue;
            _headConverter.PositionGain = BasisMediaPipeSettings.HeadPositionStrength.RawValue;
            _headConverter.HeightOffset = BasisMediaPipeSettings.HeadHeight.RawValue;
            _headConverter.YawGain = BasisMediaPipeSettings.HeadRotationStrength.RawValue;
            _headConverter.PitchGain = BasisMediaPipeSettings.HeadRotationStrength.RawValue;
            _headConverter.RollGain = BasisMediaPipeSettings.HeadRotationStrength.RawValue;
            _headConverter.InvertRoll = BasisMediaPipeSettings.InvertHeadRoll.RawValue;
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
                _camera.Start(deviceName, Config.CameraWidth, Config.CameraHeight, Config.TargetFps);
            }
        }

        /// <summary>Applies persisted resolution/FPS and restarts only the webcam (no model reload).</summary>
        public void ReloadCamera()
        {
            Config.CameraWidth = BasisMediaPipeSettings.ResolutionWidth.RawValue;
            Config.CameraHeight = BasisMediaPipeSettings.ResolutionHeight.RawValue;
            Config.TargetFps = BasisMediaPipeSettings.CameraFps.RawValue;
            if (_backend != null)
            {
                _camera.Start(CameraDeviceName, Config.CameraWidth, Config.CameraHeight, Config.TargetFps);
            }
        }
        public static BasisMediaPipeManagement Instance;
        public override void StartSDK()
        {
            Instance = this;

            BasisDeviceManagement.OnBootModeChanged -= HandleBootModeChanged;
            BasisDeviceManagement.OnBootModeChanged += HandleBootModeChanged;

            // Webcam tracking is desktop-only; in VR the real HMD/controllers drive the trackers.
            if (BasisDeviceManagement.StaticCurrentMode != BasisConstants.Desktop)
            {
                BasisDebug.Log("BasisMediaPipe: not in Desktop mode; webcam tracking disabled.");
                return;
            }

            LoadSettingsIntoConfig();

            if (!BasisMediaPipeSettings.Enable.RawValue)
            {
                return;
            }

            _backend = BasisMediaPipeBackendRegistry.Create();
            _backend.Initialize(Config);
            BasisDebug.Log($"BasisMediaPipe: backend = {_backend.BackendName}.");

            if (!_backend.IsAvailable)
            {
                BasisDebug.LogError("BasisMediaPipe: MediaPipe plugin not installed; tracking inert. See package README.");
                return;
            }

            if (!_camera.Start(CameraDeviceName, Config.CameraWidth, Config.CameraHeight, Config.TargetFps))
            {
                BasisDebug.LogError("BasisMediaPipe: failed to start webcam.");
            }

            BasisLocalPlayer.OnLocalAvatarChanged -= HandleAvatarChanged;
            BasisLocalPlayer.OnLocalAvatarChanged += HandleAvatarChanged;
            IsDeviceBooted = true;
        }

        private void HandleAvatarChanged()
        {
            DestroyAllTrackers();
            CacheArmRig();
            _armConverter.Reset();
            CalibrateHead();
        }

        private void CacheArmRig()
        {
            _armRigValid = false;
            BasisAvatar avatar = BasisLocalPlayer.Instance != null ? BasisLocalPlayer.Instance.BasisAvatar : null;
            Animator anim = avatar != null ? avatar.Animator : null;
            if (anim == null || !anim.isHuman) return;

            _hipsBone = anim.GetBoneTransform(HumanBodyBones.Hips);
            _headBone = anim.GetBoneTransform(HumanBodyBones.Head);
            _leftUpperArm = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _leftLowerArm = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _leftHandBone = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            _rightUpperArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _rightLowerArm = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rightHandBone = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (_hipsBone == null || _leftUpperArm == null || _leftLowerArm == null || _leftHandBone == null
                || _rightUpperArm == null || _rightLowerArm == null || _rightHandBone == null)
            {
                return;
            }

            Transform root = BasisLocalPlayer.Instance.transform;
            _leftUpperLen = Vector3.Distance(root.InverseTransformPoint(_leftUpperArm.position), root.InverseTransformPoint(_leftLowerArm.position));
            _leftForeLen = Vector3.Distance(root.InverseTransformPoint(_leftLowerArm.position), root.InverseTransformPoint(_leftHandBone.position));
            _rightUpperLen = Vector3.Distance(root.InverseTransformPoint(_rightUpperArm.position), root.InverseTransformPoint(_rightLowerArm.position));
            _rightForeLen = Vector3.Distance(root.InverseTransformPoint(_rightLowerArm.position), root.InverseTransformPoint(_rightHandBone.position));
            _armRigValid = true;
        }

        private float CameraAspect()
        {
            WebCamTexture texture = _camera.Texture;
            if (texture != null && texture.width > 16 && texture.height > 16)
            {
                return (float)texture.width / texture.height;
            }
            return Config.CameraHeight > 0 ? (float)Config.CameraWidth / Config.CameraHeight : 1f;
        }

        private bool TryBuildArmRig(out MediaPipeArmConverter.AvatarArmRig rig)
        {
            rig = default;
            if (!_armRigValid || BasisLocalPlayer.Instance == null) return false;
            if (_hipsBone == null || _leftUpperArm == null || _rightUpperArm == null) return false;

            Transform root = BasisLocalPlayer.Instance.transform;
            Vector3 leftAnchor = root.InverseTransformPoint(_leftUpperArm.position);
            Vector3 rightAnchor = root.InverseTransformPoint(_rightUpperArm.position);
            Vector3 hip = root.InverseTransformPoint(_hipsBone.position);

            Vector3 up = ((leftAnchor + rightAnchor) * 0.5f) - hip;
            Vector3 right = rightAnchor - leftAnchor;
            if (up.sqrMagnitude < 1e-6f || right.sqrMagnitude < 1e-6f) return false;

            up.Normalize();
            Vector3 forward = Vector3.Cross(right.normalized, up).normalized;
            right = Vector3.Cross(up, forward).normalized;

            Vector3 shoulderCenter = (leftAnchor + rightAnchor) * 0.5f;
            Vector3 headLocal = _headBone != null ? root.InverseTransformPoint(_headBone.position) : shoulderCenter + up * 0.2f;

            rig = new MediaPipeArmConverter.AvatarArmRig
            {
                LeftAnchor = leftAnchor,
                RightAnchor = rightAnchor,
                LeftUpperLen = _leftUpperLen,
                LeftForeLen = _leftForeLen,
                RightUpperLen = _rightUpperLen,
                RightForeLen = _rightForeLen,
                Right = right,
                Up = up,
                Forward = forward,
                HeadLocal = headLocal,
                HeadMetric = Vector3.Distance(headLocal, shoulderCenter),
                Valid = true,
            };
            return true;
        }

        private void HandleBootModeChanged(string mode)
        {
            if (mode != BasisConstants.Desktop)
            {
                if (_backend != null) StopSDK();
            }
            else if (_backend == null && BasisMediaPipeSettings.Enable.RawValue)
            {
                StartSDK();
            }
        }

        public override void StopSDK()
        {
            BasisLocalPlayer.OnLocalAvatarChanged -= HandleAvatarChanged;
            _camera.Stop();
            DestroyAllTrackers();
            _backend?.Shutdown();
            _backend = null;
            _hasLatest = false;
            IsDeviceBooted = false;
        }

        public override void Simulate()
        {
            if(IsDeviceBooted == false)
            {
                return;
            }
            if (_backend == null || !_backend.IsAvailable)
            {
                return;
            }
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

            if (Config.EnableHeadPosition || Config.EnableHeadRotation)
            {
                if (result.HasFace && BasisLocalBoneDriver.EyeControl != null && _headConverter.TryGetHeadOffset(in result, out Quaternion headOffset, out Vector3 headPositionOffset))
                {
                    BasisLocalBoneControl eye = BasisLocalBoneDriver.EyeControl;
                    BasisLocalBoneControl headControl = BasisLocalBoneDriver.HeadControl;
                    float eyeToHeadY = headControl != null ? headControl.TposeLocalScaled.position.y - eye.TposeLocalScaled.position.y : 0f;

                    Vector3 headPosition = eye.OutGoingData.position;
                    headPosition.y += eyeToHeadY + headPositionOffset.y;
                    if (Config.EnableHeadPosition)
                    {
                        headPosition.x += headPositionOffset.x;
                        headPosition.z += headPositionOffset.z;
                    }
                    Quaternion headRotation = Config.EnableHeadRotation ? eye.OutGoingData.rotation * headOffset : eye.OutGoingData.rotation;
                    EnsureTracker(BasisBoneTrackedRole.Head).FollowMovement.SetLocalPositionAndRotation(headPosition, headRotation);
                }
                else
                {
                    RemoveTracker(BasisBoneTrackedRole.Head);
                }
            }
            else
            {
                RemoveTracker(BasisBoneTrackedRole.Head);
            }

            if (Config.EnableHandTracking)
            {
                bool posePath = result.HasPose && _armRigValid;
                if (!_armDiagLogged || posePath != _armPosePathActive)
                {
                    _armDiagLogged = true;
                    _armPosePathActive = posePath;
                    BasisDebug.Log($"BasisMediaPipe arms: EnablePose={Config.EnablePose} HasPose={result.HasPose} armRig={_armRigValid} pose2D={(result.PoseLandmarks != null ? result.PoseLandmarks.Length : 0)} -> {(posePath ? "pose retarget" : "hand fallback")}");
                }
                ApplyHandTracker(in result, true);
                ApplyHandTracker(in result, false);
            }
            else
            {
                RemoveTracker(BasisBoneTrackedRole.LeftHand);
                RemoveTracker(BasisBoneTrackedRole.RightHand);
                RemoveTracker(BasisBoneTrackedRole.LeftLowerArm);
                RemoveTracker(BasisBoneTrackedRole.RightLowerArm);
            }

            if (Config.EnableChest)
            {
                if (result.HasPose && BasisLocalBoneDriver.ChestControl != null && _bodyConverter.TryGetChestRotation(in result, out Quaternion chestRotation))
                {
                    EnsureTracker(BasisBoneTrackedRole.Chest).FollowMovement.SetLocalPositionAndRotation(BasisLocalBoneDriver.ChestControl.TposeLocal.position, chestRotation);
                }
                else
                {
                    RemoveTracker(BasisBoneTrackedRole.Chest);
                }
            }
            else
            {
                RemoveTracker(BasisBoneTrackedRole.Chest);
            }
        }

        private void ApplyHandTracker(in BasisMediaPipeResult result, bool left)
        {
            Vector3[] landmarks = left ? result.LeftHandLandmarks : result.RightHandLandmarks;
            bool handDetected = left ? result.HasLeftHand : result.HasRightHand;
            BasisBoneTrackedRole handRole = left ? BasisBoneTrackedRole.LeftHand : BasisBoneTrackedRole.RightHand;
            BasisBoneTrackedRole elbowRole = left ? BasisBoneTrackedRole.LeftLowerArm : BasisBoneTrackedRole.RightLowerArm;

            if (Config.EnablePose && result.HasPose && TryBuildArmRig(out MediaPipeArmConverter.AvatarArmRig rig)
                && _armConverter.TryGetArm(result.PoseLandmarks, CameraAspect(), in rig, left,
                    out Vector3 wristLocal, out Vector3 elbowLocal, out Quaternion wristRotation))
            {
                if (handDetected && landmarks != null && BasisLocalBoneDriver.HipsControl != null
                    && _handConverter.TryGetHandTarget(landmarks, left, out _, out Quaternion handRotationOffset))
                {
                    wristRotation = BasisLocalBoneDriver.HipsControl.OutGoingData.rotation * handRotationOffset;
                }

                EnsureTracker(handRole).FollowMovement.SetLocalPositionAndRotation(wristLocal, wristRotation);

                if (Config.EnableArmElbowPole)
                {
                    EnsureTracker(elbowRole).FollowMovement.SetLocalPositionAndRotation(elbowLocal, Quaternion.identity);
                }
                else
                {
                    RemoveTracker(elbowRole);
                }
                return;
            }

            RemoveTracker(elbowRole);
            if (!handDetected || landmarks == null || landmarks.Length < 21)
            {
                RemoveTracker(handRole);
                return;
            }

            // No body pose: place the wrist from the hand landmarker, anchored to the head (face) when present.
            float faceSize = result.HasFace ? result.FaceImageSize : 0f;
            if (TryBuildArmRig(out MediaPipeArmConverter.AvatarArmRig handRig)
                && _armConverter.TryGetArmFromHand(landmarks[0], result.HeadImagePosition, faceSize, CameraAspect(), in handRig, left, out Vector3 handWrist, out Quaternion handWristRotation))
            {
                if (BasisLocalBoneDriver.HipsControl != null
                    && _handConverter.TryGetHandTarget(landmarks, left, out _, out Quaternion handRotationOffset))
                {
                    handWristRotation = BasisLocalBoneDriver.HipsControl.OutGoingData.rotation * handRotationOffset;
                }
                EnsureTracker(handRole).FollowMovement.SetLocalPositionAndRotation(handWrist, handWristRotation);
                return;
            }

            BasisLocalBoneControl baseControl = BasisLocalBoneDriver.HipsControl != null ? BasisLocalBoneDriver.HipsControl : BasisLocalBoneDriver.EyeControl;
            if (baseControl == null)
            {
                return;
            }

            if (_handConverter.TryGetHandTarget(landmarks, left, out Vector3 positionOffset, out Quaternion rotationOffset))
            {
                var basePose = baseControl.OutGoingData;
                EnsureTracker(handRole).FollowMovement.SetLocalPositionAndRotation(basePose.position + basePose.rotation * positionOffset, basePose.rotation * rotationOffset);
            }
        }

        public void CalibrateHead()
        {
            if (!_hasLatest)
            {
                return;
            }

            _headConverter.Calibrate(_latest);
            _bodyConverter.Calibrate(_latest);
            _handConverter.Calibrate(_latest);
        }

        public string DiagnosticsText()
        {
            if (_backend == null)
            {
                return "Not running.";
            }

            string status = $"Backend: {_backend.BackendName}\nAvailable: {_backend.IsAvailable}\nCamera: {(_camera.IsReady ? "ready" : "not ready")}";
            if (_hasLatest)
            {
                status += $"\nFace: {_latest.HasFace}   L-Hand: {_latest.HasLeftHand}   R-Hand: {_latest.HasRightHand}   Pose: {_latest.HasPose}";
                status += $"\nArm rig: {(_armRigValid ? "ready" : "unavailable")}";
            }
            else
            {
                status += "\n(no result yet)";
            }
            return status;
        }

        private void RemoveTracker(BasisBoneTrackedRole role)
        {
            if (!_activeTrackers.Remove(role))
            {
                return;
            }
            if (_trackers.TryGetValue(role, out BasisInputXRSimulate input) && input != null)
            {
                input.UnAssignTracker();
            }
        }

        private BasisInputXRSimulate EnsureTracker(BasisBoneTrackedRole role)
        {
            if (_trackers.TryGetValue(role, out BasisInputXRSimulate existing) && existing != null)
            {
                if (_activeTrackers.Add(role))
                {
                    existing.AssignRoleAndTracker(role);
                }
                return existing;
            }

            RegisterDeviceMatch();

            string id = $"{SubSystem}:{role}";
            GameObject go = new GameObject(id)
            {
                transform =
                {
                    parent = BasisLocalPlayer.Instance.transform
                }
            };
            Transform move = new GameObject($"{id} move").transform;
            move.parent = BasisLocalPlayer.Instance.transform;

            BasisInputXRSimulate input = go.AddComponent<BasisInputXRSimulate>();
            input.IsCameraTracked = true;
            input.FollowMovement = move;
            input.InitializeTracking(id, SubSystem, SubSystem, false, role);
            input.AssignRoleAndTracker(role);

            BasisDeviceManagement.Instance.TryAdd(input);

            _trackers[role] = input;
            _activeTrackers.Add(role);
            return input;
        }

        // Declare our virtual devices to the matcher with raycast OFF, so InitializeTracking
        // resolves these settings instead of generating a raycast-enabled fallback (the default
        // for a forced non-CenterEye role). These are pose trackers, not UI pointers.
        private static bool _deviceMatchRegistered;

        private static void RegisterDeviceMatch()
        {
            if (_deviceMatchRegistered) return;

            BasisDeviceManagement dm = BasisDeviceManagement.Instance;
            if (dm == null || dm.BasisDeviceNameMatcher == null)
            {
                return;
            }

            dm.BasisDeviceNameMatcher.BasisDevice.Add(new DeviceSupportInformation
            {
                DeviceID = SubSystem,
                matchableDeviceIds = new[] { SubSystem },
                HasRayCastSupport = false,
                HasRayCastVisual = false,
                HasRayCastRadical = false,
            });
            _deviceMatchRegistered = true;
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
            _activeTrackers.Clear();
        }
    }
}
