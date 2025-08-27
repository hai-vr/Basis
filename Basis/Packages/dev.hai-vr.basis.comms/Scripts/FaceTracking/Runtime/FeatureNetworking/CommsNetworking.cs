using System;
using Basis.Scripts.BasisSdk;
using UnityEngine;

namespace HVR.Basis.Comms
{
    public class CommsNetworking
    {
        public static FeatureInterpolator NewInterpolator(BasisAvatar avatar, int count, InterpolatedDataChanged interpolatedDataChanged, ITransmitter transmitter, bool isWearer, byte localIdentifier)
        {
            var holder = new GameObject($"Streamed-L{localIdentifier}")
            {
                transform = { parent = avatar.transform }
            };
            holder.SetActive(false);
            StreamedAvatarFeature streamed = holder.AddComponent<StreamedAvatarFeature>();
            streamed.valueArraySize = (byte)count; // TODO: Sanitize count to be within bounds
            streamed.transmitter = transmitter;
            streamed.isWearer = isWearer;
            streamed.localIdentifier = localIdentifier;
            holder.SetActive(true);

            var handle = new FeatureInterpolator(streamed, interpolatedDataChanged);
            streamed.OnInterpolatedDataChanged += handle.OnInterpolatedDataChanged;
            return handle;
        }

        public static FeatureEvent NewEventDriven(EventReceived eventReceived, ResyncRequested resyncRequested, ResyncEveryoneRequested resyncEveryoneRequested, ITransmitter transmitter)
        {
            var handle = new FeatureEvent(eventReceived, resyncRequested, resyncEveryoneRequested, transmitter);
            return handle;
        }

        public delegate void InterpolatedDataChanged(float[] current);

        public delegate void EventReceived(ArraySegment<byte> subBuffer);

        public delegate void ResyncRequested(ushort[] whoAsked);

        public delegate void ResyncEveryoneRequested();
    }
}
