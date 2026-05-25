namespace Basis.EventDriver
{
    /// <summary>
    /// Profiler section identifiers used by <see cref="BasisEventDriver"/> to tag timing spans.
    /// Split out from the driver so that file stays focused on the per-frame loop; the driver
    /// pulls these in with <c>using static</c>, so they are referenced unqualified there.
    /// </summary>
    internal static class BasisEventDriverProfileSections
    {
        // ── LateUpdate profile section IDs (paired with ProfileBegin / ProfileEnd) ──
        public const int PROF_NETWORK_APPLY = 0;
        public const int PROF_DEVICE_MANAGEMENT = 1;
        public const int PROF_REMOTE_AUDIO_SIMULATE = 2;
        public const int PROF_NAMEPLATE_SCHEDULE = 3;
        public const int PROF_BTWEEN = 4;
        public const int PROF_LOCAL_PLAYER = 5;
        public const int PROF_REMOTE_FACE_SIMULATE = 6;
        public const int PROF_REMOTE_AUDIO_APPLY = 7;
        public const int PROF_BLENDSHAPE_SIMULATE = 8;
        public const int PROF_BLENDSHAPE_APPLY = 9;
        public const int PROF_JIGGLE_SCHEDULE = 10;
        public const int PROF_NETWORK_TRANSMIT = 11;
        public const int PROF_JIGGLE_POSE = 12;
        public const int PROF_MICROPHONE = 13;
        public const int PROF_NAMEPLATE_COMPLETE = 14;
        public const int PROF_JIGGLE_COMPLETE_POSE = 15;
        public const int PROF_SHADOW_CLONE = 16;

        // ── Network-apply sub-section IDs (paired with ProfileBegin2 / ProfileEnd2) ──
        public const int PROF_NET_TRANSMIT_PICKUPS = 0;
        public const int PROF_NET_FIRE_BEFORE_APPLY = 1;
        public const int PROF_NET_SIMULATE_APPLY = 2;
        public const int PROF_NET_COMPLETE_REMOTE_LERP = 3;
        public const int PROF_NET_MICROPHONE = 4;
    }
}
