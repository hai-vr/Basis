using Unity.Mathematics;

namespace GatorDragonGames.JigglePhysics {

public struct JiggleTransform {
    public bool isVirtual;
    /// <summary>
    /// Whether the read-reset job has to fetch this bone's lossy scale. Only Cache consumes scale, as
    /// `worldRadius = collisionRadius * averageScale` — so for a rig whose collisionRadius is 0 (the
    /// default: ToJigglePointParameters only emits one under the advanced + collision toggles) the
    /// fetch is multiplied by zero, and a whole localToWorldMatrix plus three square roots per bone
    /// per frame is bought for nothing. Rides in the padding after isVirtual, so the struct is the
    /// same 48 bytes it was.
    /// </summary>
    public bool wantsScale;
    public float3 position;
    public quaternion rotation;
    public float3 scale;

    public static JiggleTransform Lerp(JiggleTransform a, JiggleTransform b, float t) {
        return new JiggleTransform() {
            isVirtual = a.isVirtual,
            wantsScale = a.wantsScale,
            position = math.lerp(a.position, b.position, t),
            rotation = math.slerp(a.rotation, b.rotation, t),
            scale = math.lerp(a.scale, b.scale, t),
        };
    }

    public override string ToString() {
        return $"Virtual: {isVirtual}, Position: {position}, Quaternion: {rotation}, Scale: {scale}";
    }
}

}