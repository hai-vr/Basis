using Basis.Scripts.Common;
using System;
[Serializable]
public class BasisStoredHandPose
{
    public BasisFingerPoseParams FingerPoseForPosition;
    public BasisCalibratedCoords[] FingerJoints;
    public BasisStoredHandPose(BasisFingerPoseParams FingerPose, BasisCalibratedCoords[] HandBonePositions)
    {
        FingerPoseForPosition = FingerPose;
        FingerJoints = HandBonePositions;
    }
}
[Serializable]
public class BasisFingerPoseParams
{
    public float Stretch, Spread;
    public BasisFingerPoseParams(float stretch, float spread)
    {
        Stretch = stretch;
        Spread = spread;
    }
}
