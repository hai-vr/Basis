namespace Basis.Network.Core
{
    public class BasisNetworkVersion
    {
        // 39: avatar delta compression (DeltaAvatarChannel, ContentShare channel merge).
        // 40: High bone quality 10->12 bits for body/limb joints (anti-shimmer; larger High packet).
        // 41: end-effector anchoring block (+39B on High) — hand/foot world targets + swivel.
        // 42: int24-mm position on Medium/Low/VeryLow (-3B), extended interval-byte encoding past
        //     305ms, upstream/P2P avatar deltas + keyframe request control frames on DeltaAvatarChannel.
        // 43: AdditionalAvatarData entries always carry the [size][messageIndex] header (size-0
        //     entries previously desynced the section) — old/new peers must not mix.
        // 44: end-effector swivel dropped (-4B on High, block 39->35, High 236->232) — the receiver
        //     took its pole from the FK joint and never read it. Body-fit (arm/leg/torso segment
        //     scales, quantized to 3x ushort) added to the avatar-change record, with a
        //     kind-discriminated fit-only update. Join fill batched + Deflate'd per batch instead of
        //     one packet per player (the redundancy is across players, not inside one record).
        //     playerUUID and playerPlatform compactly encoded (BasisCompactId / BasisPlatformCodec).
        // 46: avatar bone rotations (all 51 slots) and the hips tail rotation are now carried in
        //     the RIG-NEUTRAL generic space instead of the sender's bone-local T-pose delta — see
        //     BasisGenericBoneRotation. Same field layout, same bit budget, same packet size, but
        //     the QUANTITY changed: a v45 payload decoded as v46 (or the reverse) reproduces every
        //     joint about the wrong axis, so old/new peers must not mix. This is what makes a pose
        //     replayable on an avatar other than the one that produced it.
        // 47: fingers leave the rotation stream. Slots 21..50 (the thirty finger joints, 41.9% of
        //     the High bitstream) are replaced by ten curl/splay channels — the twenty scalars every
        //     Basis finger backend already reduces its input to, and which BasisFingerSlerpJob
        //     expands into those thirty rotations anyway. The receiver re-expands them through the
        //     grid baked from ITS OWN avatar, so finger geometry never crosses the wire and the
        //     result is correctly scaled per rig. High packet 232 -> 181 bytes (-22%), Medium
        //     153 -> 109, Low 128 -> 93, VeryLow 109 -> 83. The delta dirty mask also shrinks
        //     (57 fields -> 37, 8 mask bytes -> 5) and each finger becomes independently dirty.
        //     Wire-incompatible in both directions: a v46 peer reads the finger block as bone bits.
        public static ushort ServerVersion = 47;
    }
}
