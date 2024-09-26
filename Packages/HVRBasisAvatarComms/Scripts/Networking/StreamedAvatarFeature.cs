using System;
using System.Collections.Generic;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedPlayer;
using DarkRift;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/Streamed Avatar Feature")]
    public class StreamedAvatarFeature : MonoBehaviour
    {
        private const int HeaderBytes = 2;
        // 1/60 makes for a maximum encoded delta time of 4.25 seconds.
        private const float DeltaLocalIntToSeconds = 1 / 60f;
        // We use 254, not 255 (leaving 1 value out), because 254 divided by 2 is a round number, 127.
        // This makes the value of 0 in range [-1:1] encodable as 127.
        private const float EncodingRange = 254f;

        private const DeliveryMethod DeliveryMethod = DarkRift.DeliveryMethod.Sequenced;
        private const float TransmissionDeltaSeconds = 0.1f;

        internal BasisAvatar avatar;
        [SerializeField] public byte valueArraySize = 8; // Must not change after first enabled.

        private readonly Queue<StreamedAvatarFeaturePayload> _queue = new();
        private float[] current;
        private float[] previous;
        private float[] target;
        private float _deltaTime;
        private float _timeLeft;
        private bool _isOutOfTape;
        private bool _writtenThisFrame;
        private bool _isWearer;
        private byte _scopedIndex;

        public event InterpolatedDataChanged OnInterpolatedDataChanged;
        public delegate void InterpolatedDataChanged(float[] current);
        
        private void Awake()
        {
            previous ??= new float[valueArraySize];
            target ??= new float[valueArraySize];
            current ??= new float[valueArraySize];
        }

        private void OnDisable()
        {
            _writtenThisFrame = false;
        }

        public void Store(int index, float value)
        {
            current[index] = value;
        }

        /// Exposed for testing purposes.
        internal void QueueEvent(StreamedAvatarFeaturePayload message)
        {
            _queue.Enqueue(message);
        }

        private void Update()
        {
            if (_isWearer) OnSender();
            else OnReceiver();
        }

        private void OnSender()
        {
            _timeLeft += Time.deltaTime;

            if (_timeLeft > TransmissionDeltaSeconds)
            {
                var toSend = new StreamedAvatarFeaturePayload
                {
                    DeltaTime = _timeLeft,
                    FloatValues = current // Not copied: Process this message immediately
                };

                EncodeAndSubmit(toSend);
                
                _timeLeft = 0;
            }
        }

        private void OnReceiver()
        {
            var timePassed = Time.deltaTime;
            _timeLeft -= timePassed;
            
            while (_timeLeft <= 0 && _queue.TryDequeue(out var eval))
            {
                _timeLeft += eval.DeltaTime;
                previous = target;
                target = eval.FloatValues;
                _deltaTime = eval.DeltaTime;
            }

            if (_timeLeft <= 0)
            {
                if (!_isOutOfTape)
                {
                    _writtenThisFrame = true;
                    for (var i = 0; i < valueArraySize; i++)
                    {
                        current[i] = target[i];
                    }

                    _isOutOfTape = true;
                    // Debug.Log($"Ran out of tape. End values are: {string.Join("; ", current)}");
                }
                else
                {
                    _writtenThisFrame = false;
                }
                _timeLeft = 0;
            }
            else
            {
                _writtenThisFrame = true;
                var progression01 = 1 - Mathf.Clamp01(_timeLeft / _deltaTime);
                for (var i = 0; i < valueArraySize; i++)
                {
                    current[i] = Mathf.Lerp(previous[i], target[i], progression01);
                }
                // Debug.Log($"Unrolling tape. Values are: {string.Join("; ", current)}");
                _isOutOfTape = false;
            }

            if (_writtenThisFrame)
            {
                OnInterpolatedDataChanged?.Invoke(current);
            }
        }

        public void SetEncodingInfo(bool isWearer, byte scopedIndex)
        {
            _isWearer = isWearer;
            _scopedIndex = scopedIndex;
        }

        #region Network Payload

        public void OnPacketReceived(ArraySegment<byte> subBuffer)
        {
            if (!isActiveAndEnabled) return;
            
            if (TryDecode(subBuffer, out var result))
            {
                _queue.Enqueue(result);
            }
        }

        // Header:
        // - Scoped Index (1 byte)
        // - Delta Time (1 byte)
        // - Float Values (valueArraySize bytes)

        private void EncodeAndSubmit(StreamedAvatarFeaturePayload message)
        {
            var buffer = new byte[HeaderBytes + valueArraySize];
            buffer[0] = _scopedIndex;
            buffer[1] = (byte)(message.DeltaTime / DeltaLocalIntToSeconds);
            
            for (var i = 0; i < current.Length; i++)
            {
                buffer[HeaderBytes + i] = (byte)(message.FloatValues[i] * EncodingRange);
            }
            
            Debug.Log($"Sending {Convert.ToBase64String(buffer)}");
            avatar.NetworkMessageSend(HVRAvatarComms.OurMessageIndex, buffer, DeliveryMethod);
        }

        private bool TryDecode(ArraySegment<byte> subBuffer, out StreamedAvatarFeaturePayload result)
        {
            if (subBuffer.Count != HeaderBytes + valueArraySize)
            {
                result = default;
                return false;
            }

            var decodedScopedIndex = subBuffer[0];
            if (decodedScopedIndex != _scopedIndex)
            {
                result = default;
                return false;
            }

            var floatValues = new float[subBuffer.Count - HeaderBytes];
            for (var i = HeaderBytes; i < subBuffer.Count; i++)
            {
                floatValues[i - HeaderBytes] = subBuffer[i] / EncodingRange;
            }
            
            result = new StreamedAvatarFeaturePayload
            {
                DeltaTime = subBuffer[1] * DeltaLocalIntToSeconds,
                FloatValues = floatValues
            };
            
            return true;
        }

        private static ushort TEMP_HELPER_GetLocalClientId()
        {
            return BasisNetworkManagement.Instance.Client.ID;
        }

        private static Dictionary<ushort, BasisNetworkedPlayer> TEMP_HELPER_GetPlayers()
        {
            return BasisNetworkManagement.Instance.Players;
        }
        
        #endregion
    }

    public class StreamedAvatarFeaturePayload
    {
        public float DeltaTime;
        public float[] FloatValues;
    }
}
