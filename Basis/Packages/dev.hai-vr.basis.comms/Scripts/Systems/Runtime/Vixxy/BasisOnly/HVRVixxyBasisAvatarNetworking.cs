#if HVR_VIXXY_IS_IN_BASIS
using System;
using Basis.Network.Core;
using HVR.Basis.Comms;
using HVR.Basis.Comms.HVRUtility;
using HVR.Vixxy;
using UnityEngine;
using Basis.Scripts.BasisSdk;

namespace HVR.Basis.Vixxy.Runtime
{
    public class HVRVixxyBasisAvatarNetworking : MonoBehaviour, IHVRInitializable, IHVRVixxyNetworkable
    {
        private const DeliveryMethod MainMessageDeliveryMethod = DeliveryMethod.Sequenced;

        [SerializeField] public HVRVixxyOrchestrator orchestrator;
        [SerializeField] public BasisAvatar avatar;

        public IHVRTransmitter transmitter { get; set; }

        private ushort _wearerId;
        private bool _isNetworkInitialized;
        private IHVRVixxyBasisNet _relayLateInit;

        private HVRAvatarComms _comms;

        private void Awake()
        {
            orchestrator.OnNetworkDataUpdateRequired += OnNetworkDataUpdateRequired;

            if (avatar == null) avatar = GetComponentInParent<BasisAvatar>(true);
        }

        private void OnNetworkDataUpdateRequired()
        {
            if (!_isNetworkInitialized) return;
        }

        internal void Wearer_SubmitFullSnapshot_ToAllNonWearers()
        {
        }

        public void OnHVRAvatarReady(bool isWearer)
        {
            if (avatar != null) // dooly
            {
                _comms = avatar.GetComponent<HVRAvatarComms>();
                _comms.BindVixxy(this);
            }
        }

        public void OnHVRReadyBothAvatarAndNetwork(bool isWearer)
        {
            _wearerId = avatar.LinkedPlayerID;
            if (_relayLateInit != null)
            {
                HVRLogging.ProtocolAccident("Received OnNetworkChange more than once in this object's lifetime, this is not normal.");
                return;
            }
            _relayLateInit = isWearer ? new HVRWearer(this) : new HVRNonWearer(this);
            _relayLateInit.OnNetworkInitialized();
        }

        public virtual void OnNetworkMessageReceived(ushort RemoteUser, byte[] unsafeBuffer, DeliveryMethod DeliveryMethod)
        {
            if (_relayLateInit != null) _relayLateInit.OnNetworkMessageReceived(User(RemoteUser), unsafeBuffer, DeliveryMethod);
            else HVRLogging.ProtocolAccident("Received OnNetworkMessageReceived before any OnNetworkChange was received.");
        }

        public virtual void OnNetworkMessageServerReductionSystem(byte[] unsafeBuffer)
        {
            if (_relayLateInit != null) _relayLateInit.OnNetworkMessageServerReductionSystem(unsafeBuffer);
            else HVRLogging.ProtocolAccident("Received OnNetworkMessageServerReductionSystem before any OnNetworkChange was received.");
        }

        public void SubmitReliable(byte[] buffer)
        {
            transmitter.NetworkMessageSend(buffer, MainMessageDeliveryMethod);
        }

        private HVRAvatarContextualUser User(ushort user)
        {
            return new HVRAvatarContextualUser
            {
                User = user,
                IsWearer = user == _wearerId
            };
        }

        public void Wearer_SubmitFullSnapshotTo(HVRAvatarContextualUser remoteUser)
        {
            throw new System.NotImplementedException();
        }

        public void NonWearer_ProcessFullSnapshot(object subBuffer)
        {
            throw new NotImplementedException();
        }

        public void RequireNetworked(string address, float defaultValue)
        {
        }
    }

    public struct HVRAvatarContextualUser
    {
        public ushort User;
        public bool IsWearer;
    }

    internal interface IHVRVixxyBasisNet
    {
        /// A Non-Wearer requests a full snapshot from the Wearer.
        internal const byte RequestState_NW_to_W = 0x01;
        /// The Wearer submits a full snapshot to a Non-Wearer.
        internal const byte SubmitFullSnapshot_W_to_NW = 0x02;
        /// The Wearer submits an incremental update to a Non-Wearer.
        internal const byte SubmitIncremental_W_to_NW = 0x03;
        /// The Wearer submits a piece of information that is not an incremental update to a Non-Wearer,
        /// but that information will cause the state to change from the perspective of that Non-Wearer.
        /// This can be, for example, information that pertains to a change in outfit, which would incur an implied change in the state,
        /// without needing to submit a change of the state itself.
        internal const byte SubmitEvent_W_to_NW = 0x04;

        void OnNetworkInitialized();
        void OnNetworkMessageReceived(HVRAvatarContextualUser RemoteUser, byte[] unsafeBuffer, DeliveryMethod DeliveryMethod);
        void OnNetworkMessageServerReductionSystem(byte[] unsafeBuffer);
    }
}
#endif
