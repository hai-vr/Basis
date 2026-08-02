namespace Basis.Scripts.BasisSdk
{
    /// <summary>
    /// How <c>FaceVisemeMovement</c> weights are turned into blendshape weights.
    /// </summary>
    public enum BasisVisemeDriveMode
    {
        /// <summary>
        /// Every viseme drives its blendshape by its own probability. Suits meshes whose
        /// viseme shapes blend together continuously.
        /// </summary>
        Continuous = 0,

        /// <summary>
        /// Only the strongest viseme is shown, at full weight; everything else rests. Suits
        /// avatars whose mouth shapes are discrete (billboard/texture-swap style) and look
        /// wrong when two of them are partially blended at once.
        /// </summary>
        WinnerTakeAll = 1,
    }

    /// <summary>
    /// Per-viseme response shaping applied between the lip-sync probability (0..1) and the
    /// blendshape weight (0..100). Defaults reproduce the untouched <c>probability * 100</c>
    /// behaviour, so an avatar with no profiles authored is unchanged.
    /// </summary>
    [System.Serializable]
    public struct BasisVisemeProfile
    {
        /// <summary>Multiplies the incoming probability before any other stage.</summary>
        public float Gain;

        /// <summary>Probabilities at or below this (post-gain) hold the shape at <see cref="OutMin"/>.</summary>
        public float Threshold;

        /// <summary>Weight this shape rests at when the viseme is inactive.</summary>
        public float OutMin;

        /// <summary>Weight this shape reaches when the viseme is fully active.</summary>
        public float OutMax;

        /// <summary>Seconds for a full <see cref="OutMin"/> to <see cref="OutMax"/> rise. 0 is instant.</summary>
        public float AttackSeconds;

        /// <summary>Seconds for a full <see cref="OutMax"/> to <see cref="OutMin"/> fall. 0 is instant.</summary>
        public float ReleaseSeconds;

        /// <summary>Snap to <see cref="OutMax"/> above <see cref="Threshold"/> instead of blending through it.</summary>
        public bool Binary;

        public static BasisVisemeProfile Default => new BasisVisemeProfile
        {
            Gain = 1f,
            Threshold = 0f,
            OutMin = 0f,
            OutMax = 100f,
            AttackSeconds = 0f,
            ReleaseSeconds = 0f,
            Binary = false,
        };

        public bool IsDefault =>
            Gain == 1f &&
            Threshold == 0f &&
            OutMin == 0f &&
            OutMax == 100f &&
            AttackSeconds == 0f &&
            ReleaseSeconds == 0f &&
            !Binary;

        /// <summary>
        /// True only for the entirely-zero entry a deserializer hands back for a table that was
        /// allocated but never authored.
        /// <para>Deliberately strict rather than "cannot move its shape". Zeroing
        /// <see cref="Gain"/> or collapsing the output range is exactly how a creator switches a
        /// viseme OFF, and that intent has to survive — rebuilding those on the default hands the
        /// shape back to the model, which parks the mouth open on whatever the model reports at
        /// rest. A profile with nothing set at all is the only unambiguous "absent".</para>
        /// </summary>
        public bool IsUnset =>
            Gain == 0f &&
            Threshold == 0f &&
            OutMin == 0f &&
            OutMax == 0f &&
            AttackSeconds == 0f &&
            ReleaseSeconds == 0f &&
            !Binary;
    }

    /// <summary>
    /// Avatar-wide lip-sync response settings.
    /// <para>Every field's "absent" value is also a legal authored value — most dangerously
    /// <see cref="BackendSmoothing"/>, where 0 means "no smoothing whatsoever" — so an instance
    /// that reaches the runtime without its field initialisers having run is indistinguishable
    /// from a deliberate request for silence. Consumers must test <see cref="IsUnset"/> before
    /// honouring a config rather than assuming the initialisers below always apply.</para>
    /// </summary>
    [System.Serializable]
    public class BasisVisemeDriveConfig
    {
        public const int VisemeCount = 15;
        public const int SilVisemeIndex = 0;
        public const int DefaultBackendSmoothing = 70;
        public const int WinnerTakeAllBackendSmoothing = 30;

        public BasisVisemeDriveMode Mode = BasisVisemeDriveMode.Continuous;

        /// <summary>
        /// How far a challenger must exceed the current winner before the mouth switches to it.
        /// Guards against the shape strobing when two visemes sit near a tie.
        /// </summary>
        public float WinnerMargin = 0.05f;

        /// <summary>
        /// Minimum time the current winner is held before any switch is allowed.
        /// </summary>
        public float WinnerHoldSeconds = 0.06f;

        /// <summary>
        /// Overall mouth activity below which the mouth drops to rest. Measured against the
        /// strongest viseme of the frame rather than the held winner, so a winner kept through
        /// its dwell does not flash to rest while the next viseme is taking over.
        /// </summary>
        public float SilenceFloor = 0.15f;

        /// <summary>
        /// Treat the <c>sil</c> viseme winning as "rest" rather than driving its shape. Most
        /// avatars already read as closed-mouth at rest, so driving a sil shape doubles up.
        /// </summary>
        public bool SilIsRest = true;

        /// <summary>
        /// Smoothing handed to the lip-sync backend (0..100, weight kept on the previous frame).
        /// Winner-take-all wants less of it, because smoothing blurs the peak the winner is
        /// picked from and delays every switch.
        /// </summary>
        public int BackendSmoothing = DefaultBackendSmoothing;

        public bool IsDefault =>
            Mode == BasisVisemeDriveMode.Continuous &&
            BackendSmoothing == DefaultBackendSmoothing;

        /// <summary>
        /// True for the all-zero instance produced when a payload predates these fields, or when
        /// the serializer builds the object without running the field initialisers above. Treat
        /// it as "the creator authored nothing" and substitute a fresh default — the zeroed
        /// values are not neutral, they are the most aggressive setting of every knob.
        /// </summary>
        public bool IsUnset =>
            Mode == BasisVisemeDriveMode.Continuous &&
            BackendSmoothing == 0 &&
            WinnerMargin == 0f &&
            WinnerHoldSeconds == 0f &&
            SilenceFloor == 0f &&
            !SilIsRest;
    }
}
