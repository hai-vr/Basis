namespace Basis.Network.Core
{
    public class BasisNetworkVersion
    {
        // 52: restricted-DOF bone encoding — 2-DOF limb/extremity joints and 1-DOF toes ship
        // quantized angles instead of smallest-three quaternions (wire-format change).
        // 53: hybrid avatar-bundle codec and developer CompactMerged framing. Byte 0 of the
        // channel-52 bundle header was a message count that every decoder documented as a hint
        // and none read; it now carries the codec id and dictionary generation.
        // 54: CompactMerged mixed framing adds raw Ack/Channeled entries (wire-format change).
        public static ushort ServerVersion = 54;
    }
}
