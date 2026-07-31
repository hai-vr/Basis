using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace GatorDragonGames.JigglePhysics {
public static class JiggleTreeStructExtensions {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JiggleTransform GetInputPose(this JiggleTreeJobData self, NativeArray<JiggleTransform> inputPoses,
        int index) {
        return inputPoses[index + (int)self.transformIndexOffset];
    }

    private static float3 SanitizeOutput(float3 v) {
        if (!math.all(math.isfinite(v))) {
            return float3.zero;
        }
        return v;
    }
    
    private static quaternion SanitizeOutput(quaternion v) {
        if (!math.all(math.isfinite(v.value))) {
            return quaternion.identity;
        }
        // The written rotation is read back as next frame's input pose, so magnitude error compounds without this.
        return math.normalizesafe(v, quaternion.identity);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteOutputPose(this JiggleTreeJobData self, NativeArray<PoseData> outputPoses, int index, JiggleTransform pose, float3 rootOffset, float3 rootPosition, float rootSnapStrength) {
        var old = outputPoses[index + (int)self.transformIndexOffset];
        if (old.pose.isVirtual) {
            return;
        }

        pose.isVirtual = false;
        pose.position = SanitizeOutput(pose.position);
        pose.scale = SanitizeOutput(pose.scale);
        pose.rotation = SanitizeOutput(pose.rotation);
        
        old.pose = pose;
        old.rootOffset = SanitizeOutput(rootOffset);
        old.rootPosition = SanitizeOutput(rootPosition);
        old.rootSnapStrength = math.isfinite(rootSnapStrength) ? rootSnapStrength : 0f;

        outputPoses[index + (int)self.transformIndexOffset] = old;
    }
}
}