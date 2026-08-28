/// <summary>
/// Which half of the room one instance in a ray tracing acceleration structure belongs to.
///
/// Ambient occlusion and global illumination trace the same avatars and the same world, and each has its
/// own idea of which of the two it wants - the panel offers Avatars, World, or both to each effect
/// separately. Tagging the instance and masking at trace time is what lets ONE structure answer both
/// questions: the structure holds the union of what the two asked for, and each trace walks only its own
/// half of it.
///
/// Without this the only way to give the two effects different content is a structure each, which is what
/// the frame paid for before: two scans of the scene, two transform sweeps and two full builds every
/// frame, over the same geometry, with every avatar's capsules registered twice.
///
/// Lives in Common because both effects name it and neither should have to depend on the other.
/// </summary>
public static class BasisTracedCategory
{
    /// <summary>The people in the room, including the rigid props, accessories and shells they carry.</summary>
    public const byte Avatar = 1 << 0;

    /// <summary>Everything else the trace is allowed to see.</summary>
    public const byte World = 1 << 1;

    /// <summary>Both halves - what a trace asks for when it wants the whole structure.</summary>
    public const byte All = Avatar | World;

    /// <summary>
    /// Which half <paramref name="layer"/> belongs to, given the layers avatars render on.
    ///
    /// The LAYER decides it rather than "is it a skinned mesh": an avatar carries rigid props, accessories
    /// and shells that are ordinary MeshRenderers, and those are still part of the person standing there.
    /// </summary>
    public static byte For(int layer, int avatarLayerMask)
    {
        return (avatarLayerMask & (1 << layer)) != 0 ? Avatar : World;
    }
}
