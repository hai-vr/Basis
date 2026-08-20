namespace Basis.Scripts.Networking.VoiceRecording
{
    public enum BasisVoiceConsentPurpose : byte
    {
        /// <summary>Capture the voice to PCM / clip / disk (trusted local API only).</summary>
        Record = 0,
        /// <summary>Re-emit the voice from a world object (issue #911; the only Cilbox-reachable purpose).</summary>
        Route = 1,
    }
}
