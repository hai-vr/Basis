using Unity.Mathematics;

namespace GatorDragonGames.JigglePhysics {

public struct JiggleGrabConstraint {
    public const int MaxGrabsPerTree = 4;
    public const int MaxTotalGrabs = 256;

    // Multiplier on a point's distance from the chain root, bounding how far a grab may drag it
    // from its animated pose. 1 is roughly "can be pulled taut but not stretched into a rubber
    // band", and because the bound scales with chain length a long ponytail gets more travel than
    // a short tuft from the same number.
    public const float DefaultMaxStretchFactor = 1f;

    public int rootID;
    public int pointIndex;
    public float3 targetPosition;
    public float strength;
    /// <summary>Zero or less leaves the grab unbounded.</summary>
    public float maxStretchFactor;
}

}
