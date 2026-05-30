using Basis.Network.Core;
using System.Threading;
using static BasisNetworkCore.Serializable.SerializableBasis;

namespace BasisNetworkServer.Security
{
    /// <summary>
    /// Server-defined ceilings (in metres) for how far clients may set their microphone (voice
    /// transmit) and hearing (audio receive) range. Seeded from Configuration at boot and pushed to
    /// clients via GlobalGetAudioRangeLimits so they clamp their sliders and effective range to it.
    /// Admins can change it live; the new values are persisted to config.xml and broadcast.
    /// </summary>
    public static class BasisAudioRangeLimitManager
    {
        private const float DefaultMeters = 25f;

        private static float _maxMicrophoneRangeMeters = DefaultMeters;
        private static float _maxHearingRangeMeters = DefaultMeters;

        public static float MaxMicrophoneRangeMeters => Interlocked.CompareExchange(ref _maxMicrophoneRangeMeters, 0f, 0f);
        public static float MaxHearingRangeMeters => Interlocked.CompareExchange(ref _maxHearingRangeMeters, 0f, 0f);

        public static void InitializeFromConfig(Configuration config)
        {
            Interlocked.Exchange(ref _maxMicrophoneRangeMeters, Sanitize(config.MaxMicrophoneRangeMeters));
            Interlocked.Exchange(ref _maxHearingRangeMeters, Sanitize(config.MaxHearingRangeMeters));
        }

        /// <summary>Clamp, set, and report whether either value actually changed.</summary>
        public static bool SetLimits(float microphoneMeters, float hearingMeters)
        {
            float mic = Sanitize(microphoneMeters);
            float hearing = Sanitize(hearingMeters);
            float prevMic = Interlocked.Exchange(ref _maxMicrophoneRangeMeters, mic);
            float prevHearing = Interlocked.Exchange(ref _maxHearingRangeMeters, hearing);
            return prevMic != mic || prevHearing != hearing;
        }

        public static void SendStateToPeer(NetPeer peer)
        {
            NetDataWriter writer = NetworkServer.RentWriter();
            try
            {
                new AdminRequest().Serialize(writer, AdminRequestMode.GlobalGetAudioRangeLimits);
                writer.Put(MaxMicrophoneRangeMeters);
                writer.Put(MaxHearingRangeMeters);
                NetworkServer.TrySend(peer, writer, BasisNetworkCommons.AdminChannel, DeliveryMethod.ReliableOrdered);
            }
            finally
            {
                NetworkServer.ReturnWriter(writer);
            }
        }

        public static void BroadcastState()
        {
            NetDataWriter writer = NetworkServer.RentWriter();
            try
            {
                new AdminRequest().Serialize(writer, AdminRequestMode.GlobalGetAudioRangeLimits);
                writer.Put(MaxMicrophoneRangeMeters);
                writer.Put(MaxHearingRangeMeters);
                NetworkServer.BroadcastMessageToClients(
                    writer,
                    BasisNetworkCommons.AdminChannel,
                    NetworkServer.PeerSnapshot,
                    DeliveryMethod.ReliableOrdered);
            }
            finally
            {
                NetworkServer.ReturnWriter(writer);
            }
        }

        private static float Sanitize(float meters)
        {
            return meters <= 0f ? DefaultMeters : meters;
        }
    }
}
