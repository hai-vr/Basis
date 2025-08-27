using System;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.Behaviour;
using HVR.Basis.Comms.HVRUtility;
using LiteNetLib;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("/")]
    public class FeatureNetworking : MonoBehaviour
    {
        private void Awake()
        {
            Destroy(this);
        }
    }

    public class FeatureEvent : IFeatureReceiver
    {
        private DeliveryMethod DeliveryMethod = DeliveryMethod.Sequenced;

        private readonly CommsNetworking.EventReceived _eventReceived;
        private readonly CommsNetworking.ResyncRequested _resyncRequested;
        private readonly CommsNetworking.ResyncEveryoneRequested _resyncEveryoneRequested;
        private readonly ITransmitter _transmitter;

        public FeatureEvent(CommsNetworking.EventReceived eventReceived, CommsNetworking.ResyncRequested resyncRequested, CommsNetworking.ResyncEveryoneRequested resyncEveryoneRequested, ITransmitter transmitter)
        {
            _eventReceived = eventReceived;
            _resyncRequested = resyncRequested;
            _resyncEveryoneRequested = resyncEveryoneRequested;
            _transmitter = transmitter;
        }

        public void OnPacketReceived(byte localIdentifier, ArraySegment<byte> data)
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
        private readonly CommsNetworking.InterpolatedDataChanged _interpolatedDataChanged;

        internal FeatureInterpolator(StreamedAvatarFeature streamed, CommsNetworking.InterpolatedDataChanged interpolatedDataChanged)
        {
            _streamed = streamed;
            _interpolatedDataChanged = interpolatedDataChanged;
        }

        public void Store(int value, float streamed01)
        {
            _streamed.Store(value, streamed01);
        }

        public void OnPacketReceived(byte localIdentifier, ArraySegment<byte> data)
        {
            HVRLogging.ProtocolDebug($"OnPacketReceived called on FeatureInterpolator. Local identifier is {localIdentifier}. Streamed local identifier is {_streamed.localIdentifier}");
            if (_streamed.localIdentifier != localIdentifier) return;
            HVRLogging.ProtocolDebug($"Pass!");

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

    public class RequestedFeature
    {
        public string identifier;
        public float lower;
        public float upper;
    }
}
