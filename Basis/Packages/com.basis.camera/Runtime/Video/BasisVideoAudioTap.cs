using Basis.Scripts.Drivers;
using UnityEngine;

namespace Basis
{
    /// <summary>
    /// Rides the scene's one AudioListener while a video is recording and copies the final mix
    /// into the recording's buffer. A filter on the listener's own object hears everything the
    /// player hears — spatialised voice, the world, the lot — from wherever the listener is,
    /// which is the camera itself when Hear From Camera has moved it there. Reads only; the mix
    /// going to the speakers is untouched.
    /// </summary>
    public sealed class BasisVideoAudioTap : MonoBehaviour
    {
        /// <summary>Written on the main thread, read on the audio thread; a stale read costs one buffer.</summary>
        [System.NonSerialized]
        public BasisRecordingAudioBuffer Target;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            BasisRecordingAudioBuffer target = Target;
            target?.Write(data, channels);
        }

        /// <summary>Attaches a tap to the active listener, or returns null where there is none to tap.</summary>
        public static BasisVideoAudioTap Attach(BasisRecordingAudioBuffer buffer)
        {
            AudioListener listener = BasisLocalCameraDriver.Instance != null
                ? BasisLocalCameraDriver.Instance.Listener
                : null;
            if (listener == null) listener = FindAnyObjectByType<AudioListener>();
            if (listener == null) return null;

            BasisVideoAudioTap tap = listener.gameObject.AddComponent<BasisVideoAudioTap>();
            tap.Target = buffer;
            return tap;
        }

        /// <summary>Stops feeding the buffer and removes the component from the listener.</summary>
        public static void Detach(ref BasisVideoAudioTap tap)
        {
            if (tap == null) return;
            tap.Target = null;
            Destroy(tap);
            tap = null;
        }
    }
}
