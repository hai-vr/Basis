using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if HVR_HAS_BASIS_SDK
using Basis.Scripts.BasisSdk;
using Basis.Scripts.Behaviour;
using Basis.Network.Core;
#endif

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/Internal/HVR Avatar Comms")]
    [HelpURL("https://docs.hai-vr.dev/docs/basis/avatar-customization")]
    public class HVRAvatarComms :
#if HVR_HAS_BASIS_SDK
        BasisAvatarMonoBehaviour
#else
        MonoBehaviour
#endif
    {
#if HVR_HAS_BASIS_SDK
        [HideInInspector] [SerializeField] private BasisAvatar avatar;
#else
        [HideInInspector] [SerializeField] private Component avatar;
#endif
        [SerializeField] private bool isFromPrefab = false;

        private readonly Nethack _nethack;

        private bool _isWearer;

        private readonly List<int> _addresses = new();
        private readonly List<MutualizedInterpolationRange> _ranges = new();
        private readonly List<HVRNeedsInterpolationCallback> _needsInterpolation = new();
        private readonly List<HVRToSubmitLater> _toStoreLater = new();
        private AvatarMessageProcessing avatarMessageProcessing;
        private StreamedAvatarFeature _streamedLateInit;

        private object _unhook;

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
#if HVR_HAS_BASIS_SDK
                avatar = HVRCommsUtil.GetAvatar(this) as BasisAvatar;
#else
                avatar = HVRCommsUtil.GetAvatar(this);
#endif
            }
            if (avatar == null)
            {
                throw new InvalidOperationException("Broke assumption: Avatar cannot be found.");
            }

            _unhook = HVRCommsUtil.HookAvatarReady(this, OnAvatarReady);
        }

        private void OnDestroy()
        {
            if (_unhook != null) HVRCommsUtil.UnhookAvatarReady(this, _unhook);
        }

        private void OnAvatarReady(bool isWearer)
        {
            _isWearer = true;

            var allInitializables = avatar.GetComponentsInChildren<IHVRInitializable>(true);
            foreach (var initializable in allInitializables)
            {
                initializable.OnHVRAvatarReady(isWearer);
            }

            _nethack.AfterAvatarReady();
        }

#if HVR_HAS_BASIS_SDK
        public override void OnNetworkReady(bool isLocallyOwned)
        {
            _nethack.AfterNetworkReady(isLocallyOwned);
        }
#endif

        private void OnReadyBothAvatarAndNetwork(bool isWearer)
        {
#if HVR_HAS_BASIS_SDK
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

            var allInitializables = avatar.GetComponentsInChildren<IHVRInitializable>(true);
            foreach (var initializable in allInitializables)
            {
                initializable.OnHVRReadyBothAvatarAndNetwork(isWearer);
            }

            DeclareMutualizedInterpolator(isWearer, carriers[0]);
#else
            throw new NotImplementedException("TODO HVRAvatarComms");
#endif
        }

#if HVR_HAS_BASIS_SDK
        private void DeclareMutualizedInterpolator(bool isWearer, HVRNetworkingCarrier carrier)
        {
            var holder = new GameObject("Streamed-Mutualized")
            {
                transform = { parent = avatar.transform }
            };
            holder.SetActive(false);
            _streamedLateInit = holder.AddComponent<StreamedAvatarFeature>();
            _streamedLateInit.valueArraySize = (byte)_addresses.Count; // TODO: Sanitize count to be within bounds
            _streamedLateInit.transmitter = carrier;
            _streamedLateInit.isWearer = isWearer;
            _streamedLateInit.localIdentifier = 0;
            holder.SetActive(true);
            // StreamedAvatarFeature only gets the ability to store data AFTER Awake() runs, so order matters here.
            foreach (var toStoreLater in _toStoreLater)
            {
                var mutualizedIndex = toStoreLater.mutualizedIndex;
                _streamedLateInit.Store(mutualizedIndex, _ranges[mutualizedIndex].AbsoluteToRange(toStoreLater.absolute));
            }
            _toStoreLater.Clear();

            _streamedLateInit.OnInterpolatedDataChanged += mutualizedData =>
            {
                foreach (var callback in _needsInterpolation)
                {
                    for (var ours = 0; ours < callback.floats.Length; ours++)
                    {
                        var mutualizedIndex = callback.oursToMutualizedIndex[ours];
                        var streamed01 = mutualizedData[mutualizedIndex];
                        var absolute = _ranges[mutualizedIndex].RangeToAbsolute(streamed01);
                        callback.floats[ours] = absolute;
                    }

                    callback.callback(callback.floats);
                }
            };

            avatarMessageProcessing = AvatarMessageProcessing.ForFeature(carrier, isWearer, HVRCommsUtil.LinkedAvatarIdOf(avatar), new HVRRedirectToStreamed(_streamedLateInit));

            StartCoroutine(SendInitialPacketNextFrame());
        }
#endif

        IEnumerator SendInitialPacketNextFrame()
        {
            // We want to send the initial packet when all BasisAvatarMonoBehaviours have been initialized.
            yield return null;
            avatarMessageProcessing.SendInitialPacket();
        }

        public MutualizedFeatureInterpolator NeedsMutualizedInterpolator(List<MutualizedInterpolationRange> inputRanges, CommsNetworking.InterpolatedDataChanged interpolatedDataChanged)
        {
            List<int> oursToMutualizedIndex = new();
            foreach (var inputRange in inputRanges)
            {
                var address = inputRange.address;
                if (!_addresses.Contains(address))
                {
                    _addresses.Add(address);
                    _ranges.Add(new MutualizedInterpolationRange
                    {
                        address = address,
                        lower = inputRange.lower,
                        upper = inputRange.upper,
                    });
                }

                var mutualizedIndex = _addresses.IndexOf(address);
                oursToMutualizedIndex.Add(mutualizedIndex);

                var storedRange = _ranges[mutualizedIndex];
                if (inputRange.lower < storedRange.lower)
                {
                    storedRange.lower = inputRange.lower;
                }
                if (inputRange.upper > storedRange.upper)
                {
                    storedRange.upper = inputRange.upper;
                }
            }

            _needsInterpolation.Add(new HVRNeedsInterpolationCallback
            {
                oursToMutualizedIndex = oursToMutualizedIndex,
                floats = new float[oursToMutualizedIndex.Count],
                callback = interpolatedDataChanged
            });

            return new MutualizedFeatureInterpolator(oursToMutualizedIndex, this);
        }

        public void SubmitAbsolute(int mutualizedIndex, float absolute)
        {
            if (_streamedLateInit != null)
            {
                _streamedLateInit.Store(mutualizedIndex, _ranges[mutualizedIndex].AbsoluteToRange(absolute));
            }
            else
            {
                _toStoreLater.Add(new HVRToSubmitLater
                {
                    mutualizedIndex = mutualizedIndex,
                    absolute = absolute
                });
            }
        }

        public void WhenNetworkMessageReceived(int carrierIndex, ushort remoteUser, byte[] buffer)
        {
            if (carrierIndex == 0)
            {
                avatarMessageProcessing.OnNetworkMessageReceived(remoteUser, buffer);
            }
        }

        public void WhenNetworkMessageServerReductionSystem(int carrierIndex, byte[] buffer)
        {
            if (carrierIndex == 0)
            {
                avatarMessageProcessing.OnNetworkMessageServerReductionSystem(buffer);
            }
        }

        private class HVRRedirectToStreamed : IFeatureReceiver
        {
            private readonly StreamedAvatarFeature streamed;

            public HVRRedirectToStreamed(StreamedAvatarFeature streamed)
            {
                this.streamed = streamed;
            }

            public void OnPacketReceived(byte localIdentifier, ArraySegment<byte> data)
            {
                streamed.OnPacketReceived(data);
            }

            public void OnResyncEveryoneRequested()
            {
                streamed.OnResyncEveryoneRequested();
            }

            public void OnResyncRequested(ushort[] whoAsked)
            {
                streamed.OnResyncRequested(whoAsked);
            }
        }

        private class HVRNeedsInterpolationCallback
        {
            public List<int> oursToMutualizedIndex;
            public float[] floats;
            public CommsNetworking.InterpolatedDataChanged callback;
        }

        private class HVRToSubmitLater
        {
            public int mutualizedIndex;
            public float absolute;
        }

#if HVR_HAS_HVR_INTEGRATION
        private static HashSet<Component> alreadyProcessed = new();
        public static void FlagHVRFullyInitialized(HVR.Integration.UGC.HVRUGCAvatar avatar)
        {
            if (!alreadyProcessed.Add(avatar)) return;

            var initializables = avatar.GetComponentInChildren<IHVRInitializable>();
            initializables.OnHVRAvatarReady(true);
        }
#endif
    }
}
