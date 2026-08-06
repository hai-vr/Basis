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
        // 48: the two over-precise fields in the fixed part of the payload are cut to the precision
        //     the deadbands were already written against. Hips world position becomes int24
        //     millimetres at HIGH too (it was 3 x float32 there, 3 x int24-mm on every lower tier),
        //     and the hips local-position delta drops from 3 x int16 to 3 x signed 13-bit packed
        //     into 5 bytes. High packet 181 -> 177 bytes, Medium 109 -> 108, Low 93 -> 92,
        //     VeryLow 83 -> 82; the tail is 22 -> 21 and position is now uniform across the ladder.
        //     The bandwidth that matters is not the 4 bytes: the delta codec compares position as
        //     raw bytes, so float32 mantissa churn marked it dirty on EVERY frame of a standing
        //     player and shipped 12 bytes with it. At 1 mm steps that field goes quiet. Positions
        //     also stop being transcoded during the server's per-tier repack — they copy.
        //     Wire-incompatible in both directions: a v47 peer reads position and the whole tail
        //     at the wrong offsets.
        // 49: the delta body stops carrying changed fields verbatim and carries per-CHANNEL residuals
        //     instead — the Exponential-Golomb code of each quantized component's difference from the
        //     baseline, with a per-field escape to raw when that is shorter. Field granularity becomes
        //     component granularity: a bone whose one dominant axis moved by a step used to cost its
        //     whole 38-bit field, and hinge joints (elbows, knees, every finger) move on one axis
        //     nearly all the time. Measured at High: a micro-motion frame is 53 B against the previous
        //     scheme's 174 B, and the saving now scales with how far things moved instead of
        //     collapsing to zero the moment anything moves at all.
        //     The dirty mask, the field partition and the whole keyframe/baseline protocol are
        //     UNCHANGED — deltas still reference a keyframe, which is what lets the reduction system
        //     keep decimating them per receiver.
        //     Also adds an UPLINK-ONLY stream frame (DeltaAvatarChannel header bit 4,
        //     BasisAvatarStreamCodec): closed-loop predictive coding against the receiver's
        //     reconstruction plus a Gray-code bit-plane sweep that re-converges after packet loss with
        //     no acknowledgement and no keyframe, so the periodic uplink keyframe is gone. Client
        //     falls back to keyframe+delta when the server disables it, and whenever it holds a P2P
        //     session — a predictive chain cannot be decimated and the P2P path throttles the server.
        //     Wire-incompatible in both directions: a v48 peer parses a v49 delta body as verbatim
        //     field bytes and reconstructs garbage.
        // 50: the compressed avatar bundle (channel 52) groups its entries by channel instead of
        //     repeating the channel byte on every one. Body becomes
        //     [origChannel:1][n:1][msgLen:2-LE] x n [bodies] per group, and the server channel-sorts
        //     a receiver's pending messages (counting sort, one O(n) pass) so the runs are long.
        //     The DeltaAvatarChannel group's bodies are additionally COLUMN-TRANSPOSED — byte j of
        //     every body, then byte j+1 of every body. Delta bodies are short and field-aligned, so
        //     interleaving them puts the same field from different players adjacent, which is the
        //     only correlation left in a stream that is already quantized, bit-packed and
        //     delta-coded. Measured -13.9% wire bytes on a resting crowd and -4.5% on a moving one
        //     against v49; keyframe bundles are -1.1%. Transposition is applied to the delta group
        //     ONLY: doing it to the fixed-size quality groups is a LOSS, because idle players emit
        //     near-identical whole payloads there and transposing shatters the long matches LZ4 was
        //     living on. See BundleCompressionExperiment in BasisServerTests for the corpus and
        //     the full table, including why per-entry lengths are still sent verbatim rather than
        //     derived from the channel (worth 1.6pp on keyframes, zero on deltas, and it would make
        //     the decoder depend on reproducing the serializer's exact byte geometry).
        //     Wire-incompatible in both directions: a v49 peer reads the [n] byte as a length.
        public static ushort ServerVersion = 50;
    }
}
