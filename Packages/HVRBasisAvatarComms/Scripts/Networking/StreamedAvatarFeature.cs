using System.Collections.Generic;
using System.Linq;
using Basis.Scripts.BasisSdk;
using DarkRift;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/StreamedAvatarFeature")]
    public class StreamedAvatarFeature : MonoBehaviour
    {
        private const byte MessageIndex = 0xC0;

        private const int HeaderBytes = 2;
        private const float DeltaLocalIntToSeconds = 1 / 60f; // 1/60 makes for a maximum encoded delta time of 4.25 seconds. 

        private const DeliveryMethod DeliveryMethod = DarkRift.DeliveryMethod.Sequenced;
        private const float TransmissionDeltaSeconds = 0.1f;

        [SerializeField] private bool isSender;
        [SerializeField] private BasisAvatar avatar; // Can be null on test builds.
        [SerializeField] public byte valueArraySize = 8; // Must not change after first enabled.
        [SerializeField] public byte scopedIndex = 0;

        private readonly Queue<StreamedAvatarFeaturePayload> _queue = new();
        private float[] current;
        private float[] previous;
        private float[] target;
        private float _deltaTime;
        private float _timeLeft;
        private bool _isOutOfTape;
        private bool _writtenThisFrame;

        private void OnEnable()
        {
            previous ??= new float[valueArraySize];
            target ??= new float[valueArraySize];
            current ??= new float[valueArraySize];
            if (avatar != null)
            {
                avatar.OnNetworkMessageReceived -= OnNetworkMessageReceived;
                avatar.OnNetworkMessageReceived += OnNetworkMessageReceived;
            }
        }

        private void OnDisable()
        {
            if (avatar != null)
            {
                avatar.OnNetworkMessageReceived -= OnNetworkMessageReceived;
            }

            _writtenThisFrame = false;
        }

        public bool IsWrittenThisFrame()
        {
            return _writtenThisFrame;
        }

        public float[] ExposeCurrent()
        {
            return current;
        }

        /// Exposed for testing purposes.
        internal void QueueEvent(StreamedAvatarFeaturePayload message)
        {
            _queue.Enqueue(message);
        }

        private void Update()
        {
            if (isSender) OnSender();
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
        }

        #region Network Payload

        private void OnNetworkMessageReceived(byte messageindex, byte[] buffer)
        {
            if (isSender) return;
            
            if (messageindex != MessageIndex) return;
            
            if (TryDecode(buffer, out var result))
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
            buffer[0] = scopedIndex;
            buffer[1] = (byte)(message.DeltaTime / DeltaLocalIntToSeconds);
            
            for (var i = 0; i < current.Length; i++)
            {
                buffer[HeaderBytes + i] = (byte)(message.FloatValues[i] * 255);
            }
            
            avatar.OnNetworkMessageSend(MessageIndex, buffer, DeliveryMethod);
        }

        private bool TryDecode(byte[] buffer, out StreamedAvatarFeaturePayload result)
        {
            if (buffer.Length != HeaderBytes + valueArraySize)
            {
                result = default;
                return false;
            }

            var decodedScopedIndex = buffer[0];
            if (decodedScopedIndex != scopedIndex)
            {
                result = default;
                return false;
            }

            result = new StreamedAvatarFeaturePayload
            {
                DeltaTime = buffer[1] * DeltaLocalIntToSeconds,
                FloatValues = buffer.Skip(HeaderBytes).Select(b => b / 255f).ToArray()
            };
            
            return true;
        }
        
        #endregion
    }

    public class StreamedAvatarFeaturePayload
    {
        public float DeltaTime;
        public float[] FloatValues;
    }
}
