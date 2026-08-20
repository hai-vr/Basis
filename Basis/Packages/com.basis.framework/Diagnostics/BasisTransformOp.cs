public enum BasisTransformOp : byte
{
    GetPosition, SetPosition,
    GetRotation, SetRotation,
    GetPose, SetPose,
    GetLocalPosition, SetLocalPosition,
    GetLocalRotation, SetLocalRotation,
    GetLocalPose, SetLocalPose,
    GetLocalScale, SetLocalScale,
    GetLossyScale,
    GetLocalToWorld, GetWorldToLocal,
    GetForward, GetRight, GetUp,
    GetParent, Reparent,
    ToWorldPoint, ToLocalPoint,
    ToWorldDir, ToLocalDir,
    Count
}
