namespace Basis.Network.Core
{
    public class BasisNetworkVersion
    {
        // 39: avatar delta compression (DeltaAvatarChannel, ContentShare channel merge).
        // 40: High bone quality 10->12 bits for body/limb joints (anti-shimmer; larger High packet).
        // 41: end-effector anchoring block (+39B on High) — hand/foot world targets + swivel.
        public static ushort ServerVersion = 41;
    }
}
