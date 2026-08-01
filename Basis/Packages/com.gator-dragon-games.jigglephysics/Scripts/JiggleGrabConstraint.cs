using Unity.Mathematics;

namespace GatorDragonGames.JigglePhysics {

public struct JiggleGrabConstraint {
    public const int MaxGrabsPerTree = 4;
    public const int MaxTotalGrabs = 256;

    public int rootID;
    public int pointIndex;
    public float3 targetPosition;
    public float strength;
}

}
