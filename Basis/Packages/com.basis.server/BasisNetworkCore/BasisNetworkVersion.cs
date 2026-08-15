namespace Basis.Network.Core
{
    public class BasisNetworkVersion
    {
        // 52: restricted-DOF bone encoding — 2-DOF limb/extremity joints and 1-DOF toes ship
        // quantized angles instead of smallest-three quaternions (wire-format change).
        // 53: hybrid avatar-bundle codec. Byte 0 of the channel-52 bundle header was a message
        // count that every decoder documented as a hint and none read; it now carries the codec
        // id and dictionary generation. A v52 client ignores that byte, so it would LZ4-decode a
        // Zstd bundle into garbage rather than reject it — the version gate is what prevents
        // that, which is why this is a bump and not a capability negotiation.
        public static ushort ServerVersion = 53;
    }
}
