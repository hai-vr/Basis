using UnityEngine;

/// <summary>
/// How a prop asks to be placed when a player spawns it. <see cref="Unspecified"/> is what every
/// prop authored before this metadata existed resolves to, and leaves placement to the spawning
/// client exactly as it was.
/// </summary>
public enum BasisPropSpawnPlacement : byte
{
    Unspecified = 0,
    Raycast = 1,
    InFrontOfPlayer = 2,
    AtPlayerOrigin = 3,
    InAirAtDistance = 4,
    OnGround = 5,
    InHand = 6,
}

/// <summary>
/// Which hand <see cref="BasisPropSpawnPlacement.InHand"/> aims for. Resolved against the player's
/// dominant-hand setting at spawn time.
/// </summary>
public enum BasisPropSpawnHand : byte
{
    Dominant = 0,
    NonDominant = 1,
    Left = 2,
    Right = 3,
}

/// <summary>
/// Creator-authored description of how a prop wants to arrive in the world. Rides the prop prefab on
/// <see cref="BasisProp"/> and is harvested into the bundle connector at build time so a client can
/// honor it before the bundle finishes downloading.
/// </summary>
[System.Serializable]
public struct BasisPropSpawnMetaData
{
    public const float FallbackDistance = 1.5f;

    public BasisPropSpawnPlacement Placement;

    public BasisPropSpawnHand Hand;

    public bool HasCustomDistance;

    [Min(0.05f)]
    public float Distance;

    public bool HasCustomScale;

    [Min(0.001f)]
    public float UniformScale;

    /// <summary>
    /// Rotates the prop to stand on the surface normal it lands on rather than staying world-upright.
    /// Only consulted by the surface-seeking placements.
    /// </summary>
    public bool AlignToSurface;

    /// <summary>
    /// Turns the prop to face the player instead of facing away along their view direction.
    /// </summary>
    public bool FaceThePlayer;

    /// <summary>
    /// True once a creator has picked a placement, meaning the client should honor this over its own
    /// default behaviour.
    /// </summary>
    public bool IsSpecified => Placement != BasisPropSpawnPlacement.Unspecified;

    public float ResolvedDistance => HasCustomDistance && Distance > 0f ? Distance : FallbackDistance;

    public float ResolvedUniformScale => HasCustomScale && UniformScale > 0f ? UniformScale : 1f;

    /// <summary>
    /// Inspector defaults for a freshly authored prop: no placement request, but sane numbers behind
    /// the override toggles so enabling one does not start from zero.
    /// </summary>
    public static BasisPropSpawnMetaData Authoring => new BasisPropSpawnMetaData
    {
        Placement = BasisPropSpawnPlacement.Unspecified,
        Hand = BasisPropSpawnHand.Dominant,
        HasCustomDistance = false,
        Distance = FallbackDistance,
        HasCustomScale = false,
        UniformScale = 1f,
        AlignToSurface = true,
        FaceThePlayer = false,
    };
}
