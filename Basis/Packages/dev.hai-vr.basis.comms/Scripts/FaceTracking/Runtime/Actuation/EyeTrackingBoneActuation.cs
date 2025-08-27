using System;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Behaviour;
using Basis.Scripts.Eye_Follow;
using Basis.Scripts.Networking.Receivers;
using HVR.Basis.Comms.HVRUtility;
using LiteNetLib;
using Unity.Mathematics;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [DefaultExecutionOrder(15010)] // Run after BasisEyeFollowBase
    [AddComponentMenu("HVR.Basis/Comms/Eye Tracking Bone Actuation")]
    public class EyeTrackingBoneActuation : BasisAvatarMonoBehaviour, ICommsNetworkable
    {
        private const string EyeLeftX = "FT/v2/EyeLeftX";
        private const string EyeRightX = "FT/v2/EyeRightX";
        private const string EyeY = "FT/v2/EyeY";
        private static readonly string[] OurAddresses = { EyeLeftX, EyeRightX, EyeY };

        [HideInInspector] [SerializeField] private BasisAvatar avatar;
        [HideInInspector] [SerializeField] private FeatureNetworking featureNetworking;
        [HideInInspector] [SerializeField] private AcquisitionService acquisition;
        [SerializeField] internal float multiplyX = 1f;
        [SerializeField] internal float multiplyY = 1f;

        public float _fEyeLeftX;
        public float _fEyeRightX;
        public float _fEyeY;
        public bool _anyAddressUpdated;
        public bool IsLocal;
        #region NetworkingFields
        // Can be null due to:
        // - Application with no network, or
        // - Network late initialization.
        // Nullability is needed for local tests without initialization scene.
        // - Becomes non-null after HVRAvatarComms.OnAvatarNetworkReady is successfully invoked
        [NonSerialized] internal FeatureInterpolator featureInterpolator;
        [NonSerialized] public BasisLocalEyeDriver _eyeFollowDriverLateInit;
        #endregion
        public BasisNetworkReceiver Receiver = null;
        private AvatarMessageProcessing _network;
        private bool _networkReady;
        private bool _eyeFollowDriverApplicable;
        private readonly Nethack _nethack;

        private ITransmitter _autoTransmitterNullable;

        public EyeTrackingBoneActuation()
        {
            _nethack = new Nethack(OnReadyBothAvatarAndNetwork);
        }

        public void AutoDefine(ITransmitter transmitter)
        {
            _autoTransmitterNullable = transmitter;
        }

        private void Awake()
        {
            if (avatar == null) avatar = CommsUtil.GetAvatar(this);
            if (featureNetworking == null) featureNetworking = CommsUtil.FeatureNetworkingFromAvatar(avatar);
            if (acquisition == null) acquisition = AcquisitionService.SceneInstance;

            avatar.OnAvatarReady += OnAvatarReady;
        }

        internal void OnAvatarReady(bool isOwner)
        {
            if (isOwner)
            {
                acquisition.RegisterAddresses(OurAddresses, OnAddressUpdated);
                _eyeFollowDriverApplicable = true;
                _eyeFollowDriverLateInit = BasisLocalPlayer.Instance.LocalEyeDriver;
            }

            _nethack.AfterAvatarReady();
        }

        public override void OnNetworkReady(bool isLocallyOwned)
        {
            HVRLogging.ProtocolDebug("OnNetworkReady called on EyeTrackingBoneActuation.");
            _nethack.AfterNetworkReady(isLocallyOwned);
        }

        private void OnReadyBothAvatarAndNetwork(bool isLocallyOwned)
        {
            HVRLogging.ProtocolDebug("OnReadyBothAvatarAndNetwork called on BlendshapeActuation.");
            IsLocal = isLocallyOwned;

            if (!IsLocal)
            {
                Receiver = NetworkedPlayer as BasisNetworkReceiver;
            }

            var transmitter = _autoTransmitterNullable != null ? _autoTransmitterNullable : new Transmitter(this);
            featureInterpolator = CommsNetworking.NewInterpolator(avatar, 3, OnInterpolatedDataChanged, transmitter, isLocallyOwned, 2);
            _network = AvatarMessageProcessing.ForFeature(transmitter, isLocallyOwned, avatar.LinkedPlayerID, featureInterpolator);
            _networkReady = true;

            _network.SendInitialPacket();
        }

        public override void OnNetworkMessageReceived(ushort remoteUser, byte[] buffer, DeliveryMethod deliveryMethod, bool isADifferentAvatarLocally)
        {
            if (!_networkReady) return;

            _network.OnNetworkMessageReceived(remoteUser, buffer, deliveryMethod, isADifferentAvatarLocally);
        }

        public override void OnNetworkMessageServerReductionSystem(byte[] buffer, bool isADifferentAvatarLocally)
        {
            if (!_networkReady) return;

            _network.OnNetworkMessageServerReductionSystem(buffer, isADifferentAvatarLocally);
        }

        private void OnEnable()
        {
            SetBuiltInEyeFollowDriverOverriden(true);
        }

        private void OnDisable()
        {
            SetBuiltInEyeFollowDriverOverriden(false);
        }

        private void OnDestroy()
        {
            avatar.OnAvatarReady -= OnAvatarReady;
            if (IsLocal)
            {
                acquisition.UnregisterAddresses(OurAddresses, OnAddressUpdated);
            }
        }

        private void OnAddressUpdated(string address, float value)
        {
            // FIXME: Temp fix, we'll need to hook to NetworkReady instead.
            // This is a quick fix so that we don't need to reupload the avatar.
            _anyAddressUpdated = _anyAddressUpdated || value != 0f;
            if (_anyAddressUpdated && _eyeFollowDriverLateInit != null)
            {
                _eyeFollowDriverLateInit.IsEnabled = false;
            }

            switch (address)
            {
                case EyeLeftX:
                {
                    _fEyeLeftX = value;
                    if (featureInterpolator != null) featureInterpolator.Store(0, (value + 1) / 2f);
                    break;
                }
                case EyeRightX:
                {
                    _fEyeRightX = value;
                    if (featureInterpolator != null) featureInterpolator.Store(1, (value + 1) / 2f);
                    break;
                }
                case EyeY:
                {
                    _fEyeY = value;
                    if (featureInterpolator != null) featureInterpolator.Store(2, (value + 1) / 2f);
                    break;
                }
            }
        }
/* this should not be required? (lD)
        private void Update()
        {
            ForceUpdate();
        }
*/
        private void LateUpdate()
        {
            ForceUpdate();
        }

        private void ForceUpdate()
        {
            if (IsLocal && !_anyAddressUpdated)
            {
                return;
            }
            SetEyeRotation(_fEyeLeftX, _fEyeY, EyeSide.Left);
            SetEyeRotation(_fEyeRightX, _fEyeY, EyeSide.Right);
        }
        private void SetEyeRotation(float x, float y, EyeSide side)
        {
            if (_eyeFollowDriverApplicable)
            {
                var xDeg = Mathf.Asin(x) * Mathf.Rad2Deg * multiplyX;
                var yDeg = Mathf.Asin(-y) * Mathf.Rad2Deg * multiplyY;
                Quaternion Euler = Quaternion.Euler(yDeg, xDeg, 0);
                switch (side)
                {
                    // FIXME: This wrongly assumes that eye bone transforms are oriented the same.
                    // This needs to be fixed later by using the work-in-progress normalized muscle system instead.
                    case EyeSide.Left:
                        _eyeFollowDriverLateInit.leftEyeTransform.localRotation = math.mul(_eyeFollowDriverLateInit.leftEyeInitialRotation, Euler);
                        break;
                    case EyeSide.Right:
                        _eyeFollowDriverLateInit.rightEyeTransform.localRotation = math.mul(_eyeFollowDriverLateInit.rightEyeInitialRotation, Euler);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(side), side, null);
                }
            }
            else
            {
                if (IsLocal && Receiver != null)
                {
                    switch (side)
                    {
                        case EyeSide.Left:
                            float result0 = (y + 1) / 2;
                            float result1 = (x + 1) / 2;
                            Receiver.Eyes[0] = result0;
                            Receiver.Eyes[1] = result1;
                            break;
                        case EyeSide.Right:
                            result0 = (y + 1) / 2;
                            result1 = (x + 1) / 2;
                            Receiver.Eyes[2] = result0;
                            Receiver.Eyes[3] = result1;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(side), side, null);
                    }
                }
            }
        }
        private void SetBuiltInEyeFollowDriverOverriden(bool value)
        {
            if (_eyeFollowDriverLateInit == null)
            {
                return;
            }
            BasisLocalEyeDriver.Override = value;
        }

        private enum EyeSide
        {
            Left, Right
        }

#region NetworkingMethods
        private void OnInterpolatedDataChanged(float[] current)
        {
            _fEyeLeftX = current[0] * 2 - 1;
            _fEyeRightX = current[1] * 2 - 1;
            _fEyeY = current[2] * 2 - 1;
        }
        #endregion
    }
}
