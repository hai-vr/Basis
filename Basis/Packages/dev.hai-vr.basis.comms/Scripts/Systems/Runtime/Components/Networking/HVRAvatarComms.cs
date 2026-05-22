using Basis.Scripts.BasisSdk;
using System;
using System.Collections;
using System.Collections.Generic;
using Basis.Scripts.Behaviour;
using Basis.Scripts.Networking.Behaviour;
using Basis.Network.Core;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/Internal/HVR Avatar Comms")]
    [HelpURL("https://docs.hai-vr.dev/docs/basis/avatar-customization")]
    public class HVRAvatarComms : BasisNetworkAvatarBehaviour
    {
        private const int AvatarMessageProcessingCarrier0 = 0;
        private const int VariableNetworkingCarrier = 1;

        new public static bool VisibleInAvatarMenu = false;
        [HideInInspector] [SerializeField] private BasisAvatar avatar;
        [SerializeField] private bool isFromPrefab = false;

        public HVRVariableStore VariableStore { get; private set; }

        private readonly Nethack _nethack;

        private bool _isWearer;

        private AvatarMessageProcessing _variableNetworkingProcessing;
        private HVRVariableNetworking _variableNetworking;

        private List<GameObject> _netObjects;

        public HVRAvatarComms()
        {
            _nethack = new Nethack(OnReadyBothAvatarAndNetwork);
        }

        private void Awake()
        {
            if (!isFromPrefab)
            {
                Destroy(this);
                return;
            }
            if (avatar == null)
            {
                avatar = HVRCommsUtil.GetAvatar(this);
            }
            if (avatar == null)
            {
                throw new InvalidOperationException("Broke assumption: Avatar cannot be found.");
            }

            avatar.OnAvatarReady += OnAvatarReady;
        }

        private void OnAvatarReady(bool isWearer)
        {
            _isWearer = isWearer;
            if (isWearer)
            {
                VariableStore = AcquisitionService.SceneInstance.VariableStore;
            }
            else
            {
                VariableStore = new HVRVariableStore();
            }


            var allInitializables = avatar.GetComponentsInChildren<IHVRInitializable>(true);
            foreach (var initializable in allInitializables)
            {
                initializable.OnHVRAvatarReady(isWearer);
            }

            _nethack.AfterAvatarReady();
        }

        public override void OnNetworkReady(bool isLocallyOwned)
        {
            _nethack.AfterNetworkReady(isLocallyOwned);
        }

        private void OnReadyBothAvatarAndNetwork(bool isWearer)
        {
            var carriers = avatar.GetComponentsInChildren<HVRNetworkingCarrier>(true);
            if (carriers.Length < 5)
            {
                throw new InvalidOperationException("Broke assumption: At least 5 Networking Carriers are required.");
            }

            for (var index = 0; index < carriers.Length; index++)
            {
                var carrier = carriers[index];
                carrier.index = index;
            }

            if (_netObjects != null)
            {
                foreach (var netObject in _netObjects)
                {
                    if (null != netObject) Destroy(netObject);
                }
            }
            _netObjects = new List<GameObject>();

            var holder = new GameObject("GeneratedNetworking__VariableNetworking")
            {
                transform = { parent = avatar.transform }
            };
            _netObjects.Add(holder);
            holder.SetActive(false);
            _variableNetworking = holder.AddComponent<HVRVariableNetworking>();
            _variableNetworking.isWearer = isWearer;
            _variableNetworking.comms = this;
            _variableNetworking.transmitter = carriers[VariableNetworkingCarrier];
            holder.SetActive(true);

            var allInitializables = avatar.GetComponentsInChildren<IHVRInitializable>(true);
            foreach (var initializable in allInitializables)
            {
                initializable.OnHVRReadyBothAvatarAndNetwork(isWearer);
            }


            _variableNetworkingProcessing = AvatarMessageProcessing.ForFeature(carriers[VariableNetworkingCarrier], isWearer, avatar.LinkedPlayerID, _variableNetworking, true);

            StartCoroutine(SendInitialPacketNextFrame());
        }

        public void RequireVariable(HVRVariable variable)
        {
            if (_variableNetworking == null) throw new InvalidOperationException("Broke assumption: VariableNetworking is not yet initialized, it may have been called before OnHVRReadyBothAvatarAndNetwork was called?");

            _variableNetworking.RequireVariable(variable);
        }

        IEnumerator SendInitialPacketNextFrame()
        {
            // We want to send the initial packet when all BasisAvatarMonoBehaviours have been initialized.
            yield return null;
            _variableNetworkingProcessing.SendInitialPacket();
        }

        public void WhenNetworkMessageReceived(int carrierIndex, ushort remoteUser, byte[] buffer, DeliveryMethod deliveryMethod)
        {
            switch (carrierIndex)
            {
                case AvatarMessageProcessingCarrier0:
                {
                    // Old system, no longer used.
                    break;
                }
                case VariableNetworkingCarrier:
                {
                    _variableNetworkingProcessing.OnNetworkMessageReceived(remoteUser, buffer, deliveryMethod);
                    break;
                }
            }
        }

        public void WhenNetworkMessageServerReductionSystem(int carrierIndex, byte[] buffer)
        {
            switch (carrierIndex)
            {
                case AvatarMessageProcessingCarrier0:
                {
                    // Old system, no longer used.
                    break;
                }
                case VariableNetworkingCarrier:
                {
                    _variableNetworkingProcessing.OnNetworkMessageServerReductionSystem(buffer);
                    break;
                }
            }
        }
    }
}
