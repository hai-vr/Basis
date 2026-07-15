namespace Basis.Network.Core
{
    public class BasisNetworkVersion
    {
        // 39: avatar delta compression (DeltaAvatarChannel, ContentShare channel merge).
        // 40: High bone quality 10->12 bits for body/limb joints (anti-shimmer; larger High packet).
        // 41: end-effector anchoring block (+39B on High) — hand/foot world targets + swivel.
        // 42: int24-mm position on Medium/Low/VeryLow (-3B), extended interval-byte encoding past
        //     305ms, upstream/P2P avatar deltas + keyframe request control frames on DeltaAvatarChannel.
        public static ushort ServerVersion = 42;
    }
}
