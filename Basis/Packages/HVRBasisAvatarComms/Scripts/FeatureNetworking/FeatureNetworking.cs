using System;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.Behaviour;
using LiteNetLib;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/Feature Networking")]
    public class FeatureNetworking : MonoBehaviour
    {
        public const byte NewNet_WearerData = 0;
        public const byte NewNet_WearerReady = 1;
        public const byte NewNet_RemoteRequestsInitialization = 2;

        public delegate void InterpolatedDataChanged(float[] current);
        public delegate void EventReceived(ArraySegment<byte> subBuffer);
        public delegate void ResyncRequested(ushort[] whoAsked);
        public delegate void ResyncEveryoneRequested();

        // Unused field since the migration to AvatarMonoBehaviour
        [Obsolete] [SerializeField] private FeatureNetPairing[] netPairings = new FeatureNetPairing[0]; // Unsafe: May contain malformed GUIDs, or null components, or non-networkable components.
        [HideInInspector][SerializeField] private BasisAvatar avatar;

        private GameObject _holder;
        private bool _isWearer;

        private int index;

        private void Awake()
        {
            if (avatar == null) avatar = CommsUtil.GetAvatar(this);
            if (avatar.GetComponentInChildren<HVRAvatarComms>(true) == null)
            {
                avatar.gameObject.AddComponent<HVRAvatarComms>();
            }
        }

        public FeatureInterpolator NewInterpolator(int count, InterpolatedDataChanged interpolatedDataChanged, BasisAvatarMonoBehaviour transmitter)
        {
            var guidIndex = index;
            index++;
            _holder = new GameObject($"Streamed-{guidIndex}")
            {
                transform = { parent = transform }
            };
            _holder.SetActive(false);
            StreamedAvatarFeature streamed = _holder.AddComponent<StreamedAvatarFeature>();
            streamed.valueArraySize = (byte)count; // TODO: Sanitize count to be within bounds
            streamed.transmitter = transmitter;
            _holder.SetActive(true);

            var handle = new FeatureInterpolator(streamed, interpolatedDataChanged);
            streamed.OnInterpolatedDataChanged += handle.OnInterpolatedDataChanged;
            return handle;
        }

        public FeatureEvent NewEventDriven(EventReceived eventReceived, ResyncRequested resyncRequested, ResyncEveryoneRequested resyncEveryoneRequested, BasisAvatarMonoBehaviour transmitter)
        {
            var handle = new FeatureEvent(this, eventReceived, resyncRequested, resyncEveryoneRequested, transmitter);
            return handle;
        }
    }

    public class FeatureEvent : IFeatureReceiver
    {
        private DeliveryMethod DeliveryMethod = DeliveryMethod.Sequenced;

        private readonly FeatureNetworking _featureNetworking;
        private readonly FeatureNetworking.EventReceived _eventReceived;
        private readonly FeatureNetworking.ResyncRequested _resyncRequested;
        private readonly FeatureNetworking.ResyncEveryoneRequested _resyncEveryoneRequested;
        private readonly BasisAvatarMonoBehaviour _transmitter;

        public FeatureEvent(FeatureNetworking featureNetworking, FeatureNetworking.EventReceived eventReceived, FeatureNetworking.ResyncRequested resyncRequested, FeatureNetworking.ResyncEveryoneRequested resyncEveryoneRequested, BasisAvatarMonoBehaviour transmitter)
        {
            _featureNetworking = featureNetworking;
            _eventReceived = eventReceived;
            _resyncRequested = resyncRequested;
            _resyncEveryoneRequested = resyncEveryoneRequested;
            _transmitter = transmitter;
        }

        public void OnPacketReceived(ArraySegment<byte> data)
        {
            _eventReceived.Invoke(data);
        }

        public void OnResyncEveryoneRequested()
        {
            _resyncEveryoneRequested.Invoke();
        }

        public void OnResyncRequested(ushort[] whoAsked)
        {
            _resyncRequested.Invoke(whoAsked);
        }

        public void Submit(ArraySegment<byte> currentState)
        {
            SubmitInternal(currentState, null);
        }

        public void Submit(ArraySegment<byte> currentState, ushort[] whoAsked)
        {
            if (whoAsked == null) throw new ArgumentException("whoAsked cannot be null");
            if (whoAsked.Length == 0) throw new ArgumentException("whoAsked cannot be empty");

            SubmitInternal(currentState, whoAsked);
        }

        private void SubmitInternal(ArraySegment<byte> currentState, ushort[] whoAskedNullable)
        {
            var buffer = new byte[1 + currentState.Count];
            buffer[0] = (byte)0; // Formerly bytes. This class needs to be shelved, really.

            currentState.CopyTo(buffer, 1);


            if (whoAskedNullable == null || whoAskedNullable.Length == 0)
            {
                _transmitter.ServerReductionSystemMessageSend(buffer);
            }
            else
            {
                _transmitter.NetworkMessageSend(buffer, DeliveryMethod, whoAskedNullable);
            }
        }
    }

    public class FeatureInterpolator : IFeatureReceiver
    {
        private readonly StreamedAvatarFeature _streamed;
        private readonly FeatureNetworking.InterpolatedDataChanged _interpolatedDataChanged;

        internal FeatureInterpolator(StreamedAvatarFeature streamed, FeatureNetworking.InterpolatedDataChanged interpolatedDataChanged)
        {
            _streamed = streamed;
            _interpolatedDataChanged = interpolatedDataChanged;
        }

        public void Store(int value, float streamed01)
        {
            _streamed.Store(value, streamed01);
        }

        public void OnPacketReceived(ArraySegment<byte> data)
        {
            _streamed.OnPacketReceived(data);
        }

        public void OnResyncEveryoneRequested()
        {
            _streamed.OnResyncEveryoneRequested();
        }

        public void OnResyncRequested(ushort[] whoAsked)
        {
            _streamed.OnResyncRequested(whoAsked);
        }

        public void OnInterpolatedDataChanged(float[] current)
        {
            _interpolatedDataChanged.Invoke(current);
        }
    }

    [Serializable]
    public class FeatureNetPairing
    {
        public Component component;
        public string guid;
    }

    public class RequestedFeature
    {
        public string identifier;
        public float lower;
        public float upper;
    }
}
