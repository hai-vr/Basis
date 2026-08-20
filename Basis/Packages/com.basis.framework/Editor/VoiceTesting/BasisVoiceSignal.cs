namespace Basis.Scripts.Networking.Voice.Testing
{
    public enum BasisVoiceSignal
    {
        /// <summary>Vowel-like harmonic tone with syllable modulation and real inter-utterance silences.</summary>
        SpeechLike,
        /// <summary>Continuous 440 Hz sine — cleanest SNR probe.</summary>
        Sine,
        /// <summary>Log sweep 100 Hz → 8 kHz — resampler/codec fidelity probe.</summary>
        Sweep,
        /// <summary>Damped clicks every 500 ms over a low noise floor — latency probe.</summary>
        ImpulseTrain,
    }
}
