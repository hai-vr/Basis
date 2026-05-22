using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking.Receivers;
using HVR.Basis.Comms.HVRUtility;
using System;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.Transmitters;
using Unity.Mathematics;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [DefaultExecutionOrder(15010)] // Run after BasisEyeFollowBase
    [AddComponentMenu("HVR.Basis/Comms/Eye Tracking Bone Actuation")]
    public class EyeTrackingBoneActuation : MonoBehaviour, IHVRInitializable
    {
        private const string EyeLeftX = "FT/v2/EyeLeftX";
        private const string EyeRightX = "FT/v2/EyeRightX";
        private const string EyeY = "FT/v2/EyeY";
        private const string EyeTrackingActive = "HVR/Internal/EyeTrackingActive";
        private const float EyeParameterInactivityTimeoutSeconds = 0.5f;

        private readonly int _eyeLeftXAddress;
        private readonly int _eyeRightXAddress;
        private readonly int _eyeYAddress;
        private readonly int _eyeTrackingActiveAddress;
        private readonly int[] _sourceEyeAddresses;

        [HideInInspector] [SerializeField] private BasisAvatar avatar;
        [SerializeField] internal float multiplyX = 1f;
        [SerializeField] internal float multiplyY = 1f;

        public float _fEyeLeftX;
        public float _fEyeRightX;
        public float _fEyeY;

        private HVRAvatarComms comms;
        public BasisNetworkReceiver Receiver = null;

        private bool _trackingActive;
        public bool IsTrackingActive => _trackingActive;
        public bool IsEyeTrackingParametersActive => _eyeTracking?.IsEyeTrackingParametersActive ?? false;
        private FaceTrackingActivityRelay _activityRelay;

        private IEyeTracking _eyeTracking;

        public EyeTrackingBoneActuation()
        {
            _eyeLeftXAddress = HVRAddress.AddressToId(EyeLeftX);
            _eyeRightXAddress = HVRAddress.AddressToId(EyeRightX);
            _eyeYAddress = HVRAddress.AddressToId(EyeY);
            _eyeTrackingActiveAddress = HVRAddress.AddressToId(EyeTrackingActive);
            _sourceEyeAddresses = new[] { _eyeLeftXAddress, _eyeRightXAddress, _eyeYAddress, _eyeTrackingActiveAddress };
        }

        private void Awake()
        {
            if (avatar == null) avatar = HVRCommsUtil.GetAvatar(this);
            comms = HVRCommsUtil.GetComms(this);
            _activityRelay = FaceTrackingActivityRelay.GetOrCreate(avatar);
        }


        public void OnHVRAvatarReady(bool isWearer)
        {
            _eyeTracking = isWearer ? new EyeTracking_Wearer(this) : new EyeTracking_Remote(this);

            if (_activityRelay != null)
            {
                _eyeTracking.OnEyeTrackingActiveAddressUpdated(_activityRelay != null && _activityRelay.IsTrackingActive);
                _activityRelay.OnTrackingActivityChanged -= OnTrackingActivityUpdated;
                _activityRelay.OnTrackingActivityChanged += OnTrackingActivityUpdated;
            }

            comms.VariableStore.RegisterAddresses(_sourceEyeAddresses, OnAddressUpdated);
        }

        public void OnHVRReadyBothAvatarAndNetwork(bool isWearer)
        {
            HVRLogging.ProtocolDebug("OnReadyBothAvatarAndNetwork called on BlendshapeActuation.");

            var addresses = new[] { _eyeLeftXAddress, _eyeRightXAddress, _eyeYAddress };
            foreach (var address in addresses)
            {
                comms.RequireVariable(new HVRVariable
                {
                    addressId = address,
                    initialValue = 0f,
                    variableTypeCode = HVRVariableTypeCode.Float,
                    needsInterpolation = true,
                    min = -1f,
                    max = 1f,
                });
            }
            comms.RequireVariable(new HVRVariable
            {
                addressId = _eyeTrackingActiveAddress,
                initialValue = 0f,
                variableTypeCode = HVRVariableTypeCode.Float,
                needsInterpolation = false,
                min = 0f,
                max = 1f,
            });

            if (!isWearer)
            {
                if (BasisNetworkPlayers.AvatarToPlayer(avatar, out _, out var networkPlayer))
                {
                    Receiver = networkPlayer as BasisNetworkReceiver;
                }
                else
                {
                    BasisDebug.LogError($"Could not find BasisNetworkReceiver for avatar {avatar.name}");
                }
            }
        }

        private void OnEnable()
        {
            BasisNetworkTransmitter.AfterAvatarChanges += UpdateAfterAvatarChangesApplied;
        }

        private void OnDisable()
        {
            BasisNetworkTransmitter.AfterAvatarChanges -= UpdateAfterAvatarChangesApplied;
        }

        private void OnDestroy()
        {
            if (_activityRelay != null)
            {
                _activityRelay.OnTrackingActivityChanged -= OnTrackingActivityUpdated;
            }

            comms.VariableStore.UnregisterAddresses(_sourceEyeAddresses, OnAddressUpdated);
            _eyeTracking?.OnDestroy();
        }

        private void Update() => _eyeTracking?.Update();
        private void UpdateAfterAvatarChangesApplied() => _eyeTracking?.UpdateAfterAvatarChangesApplied();

        private class EyeTracking_Wearer : IEyeTracking
        {
            private readonly EyeTrackingBoneActuation our;
            private bool _trackingActive;
            private float _lastEyeParameterSampleTime = float.NegativeInfinity;
            private bool _eyeTrackingParametersActive;
            private bool _eyeTrackingActive;
            private float _xLeft;
            private float _xRight;
            private float _y;

            public EyeTracking_Wearer(EyeTrackingBoneActuation our)
            {
                this.our = our;
            }

            public void Update()
            {
                if (_eyeTrackingParametersActive && Time.unscaledTime - _lastEyeParameterSampleTime > EyeParameterInactivityTimeoutSeconds)
                {
                    TurnOffEyeTracking();
                }
            }

            public void OnTrackingActivityUpdated(bool isTrackingActive)
            {
                _trackingActive = isTrackingActive;

                if (!_trackingActive)
                {
                    TurnOffEyeTracking();
                }
            }

            public void OnEyeAngleAddressUpdated(float xLeft, float xRight, float y)
            {
                if (!(xLeft == 0f && xRight == 0f && y == 0f))
                {
                    _lastEyeParameterSampleTime = Time.unscaledTime;
                    our.comms.VariableStore.Submit(our._eyeTrackingActiveAddress, 1f);
                }

                _xLeft = xLeft;
                _xRight = xRight;
                _y = y;
            }

            public void OnDestroy()
            {
                TurnOffEyeTracking();
            }

            public bool IsEyeTrackingParametersActive => _eyeTrackingParametersActive;

            public void OnEyeTrackingActiveAddressUpdated(bool eyeTrackingActive)
            {
                if (_eyeTrackingActive == eyeTrackingActive) return;

                _eyeTrackingActive = eyeTrackingActive;

                BasisLocalEyeDriver.Override = eyeTrackingActive;

                var localPlayer = BasisLocalPlayer.Instance;
                if (localPlayer != null && localPlayer.FacialBlinkDriver != null)
                {
                    localPlayer.FacialBlinkDriver.SetOverride(eyeTrackingActive);
                }
            }

            public void UpdateAfterAvatarChangesApplied()
            {
                ApplyEye(_xLeft, _y, EyeSide.Left);
                ApplyEye(_xRight, _y, EyeSide.Right);
            }

            private void ApplyEye(float x, float y, EyeSide side)
            {
                // Uses EyeCalibration from BasisLocalEyeDriver to handle arbitrary eye bone orientations for local player.
                // Retaining Hai's original FIXME: This could/should be replaced by a WIP normalized muscle system

                var xRad = ClampToEyeAngleLimits(Mathf.Asin(x) * our.multiplyX);
                var yRad = ClampToEyeAngleLimits(Mathf.Asin(-y) * our.multiplyY);

                quaternion yaw = quaternion.AxisAngle(new float3(0, 1, 0), xRad);
                quaternion pitch = quaternion.AxisAngle(new float3(1, 0, 0), yRad);
                quaternion canonical = math.mul(yaw, pitch);

                switch (side)
                {
                    case EyeSide.Left:
                    {
                        var cal = BasisLocalEyeDriver.calLeft;
                        quaternion rigOffset = math.mul(math.mul(cal.basis, canonical), cal.invBasis);
                        BasisLocalEyeDriver.leftEyeTransform.localRotation =
                            math.mul(cal.initialRotation, rigOffset);
                        break;
                    }
                    case EyeSide.Right:
                    {
                        var cal = BasisLocalEyeDriver.calRight;
                        quaternion rigOffset = math.mul(math.mul(cal.basis, canonical), cal.invBasis);
                        BasisLocalEyeDriver.rightEyeTransform.localRotation =
                            math.mul(cal.initialRotation, rigOffset);
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(side), side, null);
                }
            }

            private void TurnOffEyeTracking()
            {
                _eyeTrackingParametersActive = false;
                _lastEyeParameterSampleTime = float.NegativeInfinity;
                our.comms.VariableStore.Submit(our._eyeTrackingActiveAddress, 0f);
                our.comms.VariableStore.Submit(our._eyeLeftXAddress, 0f);
                our.comms.VariableStore.Submit(our._eyeRightXAddress, 0f);
                our.comms.VariableStore.Submit(our._eyeYAddress, 0f);
            }
        }

        private class EyeTracking_Remote : IEyeTracking
        {
            private readonly EyeTrackingBoneActuation our;

            private bool _isTrackingActive;
            private bool _isEyeTrackingActive;
            private float _yClamped;
            private float _xLeftClamped;
            private float _xRightClamped;

            public bool IsEyeTrackingParametersActive => false;

            public EyeTracking_Remote(EyeTrackingBoneActuation our)
            {
                this.our = our;
            }

            public void Update()
            {
            }

            public void OnEyeTrackingActiveAddressUpdated(bool eyeTrackingActive)
            {
                if (_isEyeTrackingActive == eyeTrackingActive) return;

                _isEyeTrackingActive = eyeTrackingActive;
                ReevaluateActivity();
            }

            public void UpdateAfterAvatarChangesApplied()
            {
                if (our.Receiver != null)
                {
                    our.Receiver.EyesAndMouth[0] = _yClamped;
                    our.Receiver.EyesAndMouth[1] = _xLeftClamped;
                    our.Receiver.EyesAndMouth[2] = _yClamped;
                    our.Receiver.EyesAndMouth[3] = _xRightClamped;
                }
            }

            public void OnTrackingActivityUpdated(bool isTrackingActive)
            {
                _isTrackingActive = isTrackingActive;
                ReevaluateActivity();
            }

            private void ReevaluateActivity()
            {
                var shouldOverride = _isTrackingActive && _isEyeTrackingActive;
                if (our.Receiver != null)
                {
                    our.Receiver.RemotePlayer.RemoteFaceDriver.OverrideEye = shouldOverride;
                    our.Receiver.RemotePlayer.RemoteFaceDriver.OverrideBlinking = shouldOverride;
                }
            }

            public void OnEyeAngleAddressUpdated(float xLeft, float xRight, float y)
            {
                // We convert to an angle, multiply, clamp, and convert back.
                _yClamped = -Mathf.Sin(ClampToEyeAngleLimits(Mathf.Asin(-y) * our.multiplyY));
                _xLeftClamped = Mathf.Sin(ClampToEyeAngleLimits(Mathf.Asin(xLeft) * our.multiplyX));
                _xRightClamped = Mathf.Sin(ClampToEyeAngleLimits(Mathf.Asin(xRight) * our.multiplyX));
            }

            public void OnDestroy()
            {
                if (our.Receiver != null)
                {
                    our.Receiver.RemotePlayer.RemoteFaceDriver.OverrideEye = false;
                    our.Receiver.RemotePlayer.RemoteFaceDriver.OverrideBlinking = false;
                }
            }
        }

        private interface IEyeTracking
        {
            void Update();
            void OnTrackingActivityUpdated(bool isTrackingActive);
            void OnEyeAngleAddressUpdated(float xLeft, float xRight, float y);
            void OnDestroy();
            bool IsEyeTrackingParametersActive { get; }
            void OnEyeTrackingActiveAddressUpdated(bool eyeTrackingActive);
            void UpdateAfterAvatarChangesApplied();
        }

        private static float ClampToEyeAngleLimits(float value)
        {
            return Mathf.Clamp(value, -Mathf.PI / 2, Mathf.PI / 2);
        }

        private void OnAddressUpdated(int address, float value)
        {
            if (_eyeTrackingActiveAddress == address)
            {
                _eyeTracking?.OnEyeTrackingActiveAddressUpdated(value > 0.5f);
            }
            else
            {
                float sanitizedValue = SanitizeAndClampEyeValue(value);
                switch (address)
                {
                    case var _ when address == _eyeLeftXAddress:
                        _fEyeLeftX = sanitizedValue;
                        break;
                    case var _ when address == _eyeRightXAddress:
                        _fEyeRightX = sanitizedValue;
                        break;
                    case var _ when address == _eyeYAddress:
                        _fEyeY = sanitizedValue;
                        break;
                    default:
                        return;
                }

                _eyeTracking?.OnEyeAngleAddressUpdated(_fEyeLeftX, _fEyeRightX, _fEyeY);
            }
        }

        private void OnTrackingActivityUpdated(bool isTrackingActive)
        {
            _eyeTracking?.OnTrackingActivityUpdated(isTrackingActive);
        }

        private static float SanitizeAndClampEyeValue(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            return Mathf.Clamp(value, -1f, 1f);
        }

        private enum EyeSide
        {
            Left, Right
        }
    }
}

