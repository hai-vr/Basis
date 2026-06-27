using UnityEngine;

namespace Basis.ImagePickup
{
    /// <summary>
    /// Tunable limits and the user-facing receive toggle for the image pickup feature.
    /// Caps are enforced on both the sending and receiving side.
    /// </summary>
    public static class BasisImagePickupSettings
    {
        public const string ReceiveEnabledKey = "Basis.ImagePickup.ReceiveEnabled";

        public const int MaxImageBytes = 8 * 1024 * 1024;
        public const int MaxSourceBytes = 32 * 1024 * 1024;
        public const int MaxDimension = 2048;
        public const long MaxTotalPixels = 2048L * 2048L;
        public const int MaxSourceDimension = 4096;
        public const long MaxSourceTotalPixels = 4096L * 4096L;
        public const int ChunkPayloadBytes = 16 * 1024;

        public const int MaxConcurrentImagesPerSender = 8;
        public const int MaxInboundTransfersPerSender = 4;
        public const float MinSecondsBetweenSpawnsPerSender = 0.5f;
        public const float InboundTransferTimeoutSeconds = 30f;

        public const float SpawnDistance = 1.5f;
        public const float BaseHeightMeters = 0.5f;

        public const float TransmitTransformHz = 15f;
        public const float MovedPositionEpsilon = 0.001f;
        public const float MovedRotationEpsilonDegrees = 0.5f;
        public const float MovedScaleEpsilon = 0.01f;

        private static bool _loaded;
        private static bool _receiveEnabled = true;

        /// <summary>
        /// When false, inbound images from other players are dropped (the feature still lets you spawn your own).
        /// Persisted across sessions.
        /// </summary>
        public static bool ReceiveEnabled
        {
            get
            {
                if (!_loaded)
                {
                    _receiveEnabled = PlayerPrefs.GetInt(ReceiveEnabledKey, 1) != 0;
                    _loaded = true;
                }
                return _receiveEnabled;
            }
            set
            {
                _receiveEnabled = value;
                _loaded = true;
                PlayerPrefs.SetInt(ReceiveEnabledKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }
}
