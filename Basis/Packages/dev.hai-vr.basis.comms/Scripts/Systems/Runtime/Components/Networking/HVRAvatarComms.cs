using Basis.Scripts.BasisSdk;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Basis.Scripts.Behaviour;
using Basis.Network.Core;
using HVR.Basis.Comms.Vixxy;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/Internal/HVR Avatar Comms")]
    [HelpURL("https://docs.hai-vr.dev/docs/basis/avatar-customization")]
    public class HVRAvatarComms : BasisAvatarMonoBehaviour
    {
        private const int AvatarMessageProcessingCarrier0 = 0;
        private const int VixxyNetworking1 = 1;

        new public static bool VisibleInAvatarMenu = false;
        [HideInInspector] [SerializeField] private BasisAvatar avatar;
        [SerializeField] private bool isFromPrefab = false;

        public HVRDataProvider DataProvider { get; private set; }

        private readonly Nethack _nethack;

        private bool _isWearer;

        internal readonly List<MutualizedInterpolationRangeStorage> _ranges = new();
        private readonly List<HVRNeedsInterpolationCallback> _needsInterpolation = new();
        private readonly List<HVRToSubmitLater> _toStoreLater = new();

        private AvatarMessageProcessing _streamedMessageProcessing;
        private AvatarMessageProcessing _lowFrequencyMessageProcessing;
        internal StreamedAvatarFeature _streamedLateInit;
        private HVRVixxyBasisAvatarNetworking _vixxyNetworkingNullable; // May remain null if Vixxy is not used in the avatar.
        private LowFrequencyFeature _lowFrequencyCompNullable;

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
            DataProvider = isWearer ? AcquisitionService.SceneInstance.DataProvider : new HVRDataProvider();

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

            if (_vixxyNetworkingNullable != null)
            {
                // This should be bound before calling OnHVRReadyBothAvatarAndNetwork below.
                _vixxyNetworkingNullable.transmitter = carriers[VixxyNetworking1];
            }

            var allInitializables = avatar.GetComponentsInChildren<IHVRInitializable>(true);
            foreach (var initializable in allInitializables)
            {
                initializable.OnHVRReadyBothAvatarAndNetwork(isWearer);
            }

            var (highFrequency, lowFrequency) = _ranges.Partition(range => range.isHighFrequency);
            if (highFrequency.Any())
            {
                DeclareMutualizedInterpolator(isWearer, carriers[AvatarMessageProcessingCarrier0], highFrequency);
            }
            if (lowFrequency.Any())
            {
                DeclareLowFrequencyReceiver(isWearer, carriers[VixxyNetworking1], lowFrequency);
            }

            StartCoroutine(SendInitialPacketNextFrame());
        }

        private void DeclareMutualizedInterpolator(bool isWearer, HVRNetworkingCarrier carrier, List<MutualizedInterpolationRangeStorage> partitionRanges)
        {
            var holder = new GameObject($"Generated__Streamed-Mutualized")
            {
                transform = { parent = avatar.transform }
            };
            holder.SetActive(false);
            _streamedLateInit = holder.AddComponent<StreamedAvatarFeature>();
            _streamedLateInit.valueArraySize = (byte)partitionRanges.Count; // TODO: Sanitize count to be within bounds
            _streamedLateInit.transmitter = carrier;
            _streamedLateInit.isWearer = isWearer;
            _streamedLateInit.localIdentifier = 0;
            var pendingStores = _toStoreLater.ToArray();
            holder.SetActive(true);
            _streamedLateInit.InitializeNormalizedValues(BuildNeutralNormalizedValues(partitionRanges));
            // StreamedAvatarFeature only gets the ability to store data AFTER Awake() runs, so order matters here.
            foreach (var toStoreLater in pendingStores)
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

            _streamedMessageProcessing = AvatarMessageProcessing.ForFeature(carrier, isWearer, avatar.LinkedPlayerID, new HVRRedirectToStreamed(_streamedLateInit));
        }

        private void DeclareLowFrequencyReceiver(bool isWearer, HVRNetworkingCarrier carrier, List<MutualizedInterpolationRangeStorage> lowFrequency)
        {
            var holder = new GameObject($"Generated__LowFrequency")
            {
                transform = { parent = avatar.transform }
            };
            holder.SetActive(false);
            _lowFrequencyCompNullable = holder.AddComponent<LowFrequencyFeature>();
            _lowFrequencyCompNullable.transmitter = carrier;
            _lowFrequencyCompNullable.isWearer = isWearer;
            _lowFrequencyCompNullable.InitializeNormalizedValues(BuildNeutralNormalizedValues(lowFrequency));
            _lowFrequencyCompNullable.OnDataChanged += (index, value) =>
            {
            };

            _lowFrequencyMessageProcessing = AvatarMessageProcessing.ForFeature(carrier, isWearer, avatar.LinkedPlayerID, new HVRRedirectToLowFrequency(_lowFrequencyCompNullable));
        }

        IEnumerator SendInitialPacketNextFrame()
        {
            // We want to send the initial packet when all BasisAvatarMonoBehaviours have been initialized.
            yield return null;
            _streamedMessageProcessing.SendInitialPacket();
        }

        public MutualizedFeatureInterpolator NeedsMutualizedInterpolator(List<MutualizedInterpolationRange> inputRanges, CommsNetworking.InterpolatedDataChanged interpolatedDataChanged)
        {
            List<int> oursToMutualizedIndex = new();
            foreach (var inputRange in inputRanges)
            {
                var address = inputRange.addressId;
                var mutualizedIndex = _ranges.FindIndex(range => range.addressId == address);
                if (mutualizedIndex == -1)
                {
                    mutualizedIndex = _ranges.Count;
                    _ranges.Add(new MutualizedInterpolationRangeStorage
                    {
                        index = mutualizedIndex,
                        isHighFrequency = inputRange.isHighFrequency,
                        addressId = address,
                        lower = inputRange.lower,
                        upper = inputRange.upper,
                    });
                }
                else
                {
                    var storedRange = _ranges[mutualizedIndex];
                    if (!storedRange.isHighFrequency)
                    {
                        storedRange.isHighFrequency = true;
                    }
                    if (inputRange.lower < storedRange.lower)
                    {
                        storedRange.lower = inputRange.lower;
                    }
                    if (inputRange.upper > storedRange.upper)
                    {
                        storedRange.upper = inputRange.upper;
                    }
                }

                oursToMutualizedIndex.Add(mutualizedIndex);
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

        public void WhenNetworkMessageReceived(int carrierIndex, ushort remoteUser, byte[] buffer, DeliveryMethod deliveryMethod)
        {
            switch (carrierIndex)
            {
                case AvatarMessageProcessingCarrier0:
                {
                    _streamedMessageProcessing.OnNetworkMessageReceived(remoteUser, buffer, deliveryMethod);
                    break;
                }
                case VixxyNetworking1:
                {
                    if (_vixxyNetworkingNullable != null)
                    {
                        _vixxyNetworkingNullable.OnNetworkMessageReceived(remoteUser, buffer, deliveryMethod);
                    }

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
                    _streamedMessageProcessing.OnNetworkMessageServerReductionSystem(buffer);
                    break;
                }
                case VixxyNetworking1:
                {
                    if (_vixxyNetworkingNullable != null)
                    {
                        // Vixxy does not use the server reduction system, but declare it anyway.
                        _vixxyNetworkingNullable.OnNetworkMessageServerReductionSystem(buffer);
                    }

                    break;
                }
            }
        }

        private float[] BuildNeutralNormalizedValues(List<MutualizedInterpolationRangeStorage> partitionRanges)
        {
            var normalized = new float[partitionRanges.Count];
            for (int index = 0; index < partitionRanges.Count; index++)
            {
                var range = partitionRanges[index];
                normalized[index] = range.lower <= 0f && range.upper >= 0f
                    ? Mathf.Clamp01(range.AbsoluteToRange(0f))
                    : 0f;
            }

            return normalized;
        }

        public void BindVixxy(HVRVixxyBasisAvatarNetworking vixxyNetworking)
        {
            _vixxyNetworkingNullable = vixxyNetworking;
        }

        private class HVRRedirectToStreamed : IFeatureReceiver
        {
            private readonly StreamedAvatarFeature streamed;
            public HVRRedirectToStreamed(StreamedAvatarFeature streamed) => this.streamed = streamed;
            public void OnPacketReceived(byte localIdentifier, ArraySegment<byte> data) => streamed.OnPacketReceived(data);
            public void OnResyncEveryoneRequested() => streamed.OnResyncEveryoneRequested();
            public void OnResyncRequested(ushort[] whoAsked) => streamed.OnResyncRequested(whoAsked);
        }

        private class HVRRedirectToLowFrequency : IFeatureReceiver
        {
            private readonly LowFrequencyFeature lowFrequency;
            public HVRRedirectToLowFrequency(LowFrequencyFeature lowFrequency) => this.lowFrequency = lowFrequency;
            public void OnPacketReceived(byte localIdentifier, ArraySegment<byte> data) => lowFrequency.OnPacketReceived(data);
            public void OnResyncEveryoneRequested() => lowFrequency.OnResyncEveryoneRequested();
            public void OnResyncRequested(ushort[] whoAsked) => lowFrequency.OnResyncRequested(whoAsked);
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
    }
}
