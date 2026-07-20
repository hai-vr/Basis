using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Basis.Network.Core;
using Basis.Network.Core.Compression;
using Basis.Scripts.Networking.Compression;
using BasisNetworkClientConsole;
using static Basis.Network.Core.Compression.BasisAvatarBitPacking;
using static SerializableBasis;

namespace Basis.Network
{
    public static class MovementSender
    {
        public static Quaternion Rotation = new Quaternion(0, 0, 0, 1);

        public static Vector3[] PlayersCurrentPosition;
        public static PlayerData[] ActivePlayerData;

        // Animation timer — shared across all players, per-player phase offsets provide variety
        private static readonly Stopwatch AnimTimer = Stopwatch.StartNew();

        // Precomputed byte offsets into the packet for High quality
        private static readonly int RotationRegionOffset = BasisAvatarBitPacking.WritePosition; // 12
        private static readonly int ScaleOffset = BasisAvatarBitPacking.WritePosition
            + BasisBoneRotationCompression.RotationBytes(BitQuality.High);
        // After flip: this is the HIPS WORLD rotation slot (was "body rotation"
        // = root world rotation). 7-byte smallest-three quaternion.
        private static readonly int HipsRotationOffset = ScaleOffset + BasisAvatarBitPacking.WriteScale;
        // 6 bytes — 3 signed shorts at ±1m. Default zero bytes already decode
        // to zero delta thanks to the signed encoding, so we don't need to
        // write anything synthetic here for fake clients.
        private static readonly int HipsLocalDeltaOffset = HipsRotationOffset + BasisAvatarBitPacking.WriteRotation;
        // 7-byte smallest-three quaternion for hips local-rotation delta.
        // Default zero bytes do NOT decode to identity (the encoding treats
        // them as a saturated-low drop-X quat) — so the test client writes an
        // explicit identity once at init.
        private static readonly int HipsLocalRotationOffset = HipsLocalDeltaOffset + BasisAvatarBitPacking.WriteHipsDelta;

        public struct PlayerData
        {
            public NetDataWriter Writer;
            public LocalAvatarSyncMessage Message;
            public byte SequenceByte;
            public float PhaseOffset;
            // v42 uplink delta state — mirrors the real client: a full keyframe every
            // UplinkKeyframeIntervalMs on the High channel (which the server snapshots as the
            // baseline), dirty-mask deltas against it on DeltaAvatarChannel in between.
            public byte[] Baseline;
            public byte BaselineSeq;
            public bool HasBaseline;
            public long LastKeyframeTicks;
            public byte[] DeltaScratch;
            public bool ForceKeyframe;
            // Per-sender strictly-increasing face counter embedded in the synthetic
            // AdditionalAvatarData payload; the observer verifies monotonicity per sender.
            public int FaceCounter;
            public AdditionalAvatarData[] FaceScratch;
        }

        // Send v42 uplink deltas like a real client (false = legacy all-keyframe uploads).
        public static bool UseUplinkDeltas = true;
        private const int UplinkKeyframeIntervalMs = 500;
        private static readonly long UplinkKeyframeIntervalTicks = Stopwatch.Frequency * UplinkKeyframeIntervalMs / 1000;

        // Attach a synthetic AdditionalAvatarData (face-tracking shaped: [16][timing][values...])
        // to every send, mirroring how the real client ships HVR high-frequency variables. The
        // observer side (MessageHandler) logs when these arrive, so a server+2-client run proves
        // additional data end-to-end over real UDP. Off by default — this is a load tester.
        public static bool EmitFaceData = false;

        // BASIS_FACE_SPACING: pin client i at (i * spacing, 1, 0) and stop the random walk, so a
        // run can hold every sender/receiver pair at an exact distance tier (High ≤10m,
        // Medium ≤30m, Low ≤50m, VeryLow beyond) to prove tier-dependent stripping live.
        public static float PinSpacingMeters = 0f;

        /// <summary>Server NACK (DeltaControlUplinkKeyframeRequest) → next send is a keyframe.</summary>
        public static void RequestKeyframe(int index)
        {
            if (ActivePlayerData == null || index < 0 || index >= ActivePlayerData.Length) return;
            ActivePlayerData[index].ForceKeyframe = true;
        }

        // Precompute compressed scale once; reused for all messages.
        private static readonly ushort CompressedScale = CompressScaleOnce(1f);

        public static void Initialize(int clientCount)
        {
            PlayersCurrentPosition = new Vector3[clientCount];
            ActivePlayerData = new PlayerData[clientCount];

            for (int i = 0; i < clientCount; i++)
            {
                PlayersCurrentPosition[i] = PinSpacingMeters > 0f
                    ? new Vector3 { x = i * PinSpacingMeters, y = 1f, z = 0f }
                    : Randomizer.GetSpawnPosition(Basis.Config.ConfigManager.SpawnRadiusMeters);
                ActivePlayerData[i] = Generate(i);
            }
        }
        /// <summary>
        /// Builds a starting payload. Pass the player's index so the pose carries the position that
        /// player was actually spawned at — the server reads the join pose to decide what quality
        /// every other player should be sent at, so a mismatch here makes the whole join snapshot
        /// tier from the wrong place.
        /// </summary>
        public static PlayerData Generate(int playerIndex = -1)
        {
            var message = new LocalAvatarSyncMessage
            {
                DataQualityLevel = (byte)BitQuality.High,
                AdditionalAvatarDatas = null,
                AdditionalAvatarDataSize = 0,
                LinkedAvatarIndex = 0,
                array = new byte[ClientManager.Size],
            };

            // Per-player random phase offset so idle animations aren't synchronized
            float phase = (float)(Random.Shared.NextDouble() * MathF.PI * 2f);

            Scripts.Networking.Compression.Vector3 spawn =
                (playerIndex >= 0 && PlayersCurrentPosition != null && playerIndex < PlayersCurrentPosition.Length)
                    ? PlayersCurrentPosition[playerIndex]
                    : Randomizer.GetSpawnPosition(Basis.Config.ConfigManager.SpawnRadiusMeters);

            // Build the full initial payload (position, bone rotations, scale, hips rotation)
            WriteInitialPayload(ref message, phase, spawn);

            return new PlayerData
            {
                Writer = new NetDataWriter(),
                Message = message,
                PhaseOffset = phase,
            };
        }

        private static void WriteInitialPayload(ref LocalAvatarSyncMessage message, float phase, Scripts.Networking.Compression.Vector3 spawn)
        {
            // Make sure buffer is correct size for High
            int size = BasisAvatarBitPacking.ConvertToSize(BitQuality.High);
            if (message.array == null || message.array.Length != size)
                message.array = new byte[size];

            double time = AnimTimer.Elapsed.TotalSeconds;

            // 1) Position (after the recent flip this is the HIPS WORLD position)
            int offset = 0;
            WritePosition(spawn, ref message.array, ref offset);

            // 2) Bone rotations: natural standing pose with idle animation
            FakePoseGenerator.WriteBoneRotations(message.array, RotationRegionOffset, BitQuality.High, time, phase);

            // 3) Scale
            WriteScaleUShort(CompressedScale, message.array, ScaleOffset);

            // 4) Hips world rotation: slight body orientation
            FakePoseGenerator.WriteCompressedHipsRotation(message.array, HipsRotationOffset, time, phase);

            // 5) Hips local-position delta — left as zero bytes; the receiver's
            //    signed-short decode treats that as a zero delta, so no synthetic
            //    write is required for fake clients.

            // 6) Hips local-rotation delta — must be an explicit identity, since
            //    smallest-three on all-zero bytes does NOT decode to identity.
            //    Set once here; the test client never animates this channel.
            WriteIdentityQuaternion(message.array, HipsLocalRotationOffset);
        }

        /// <summary>
        /// Writes the identity quaternion (0,0,0,1) into a 7-byte smallest-three
        /// slot. Identity has w as the largest component (= 1), so:
        ///   index byte = 3 (drop w)
        ///   three small components = 0 → quantized = midpoint = 32768
        /// </summary>
        private static void WriteIdentityQuaternion(byte[] dst, int offset)
        {
            // QuantizeSmall(0f) = midpoint = 32768 = 0x8000 → lo 0x00, hi 0x80
            dst[offset] = 3;
            dst[offset + 1] = 0x00;
            dst[offset + 2] = 0x80;
            dst[offset + 3] = 0x00;
            dst[offset + 4] = 0x80;
            dst[offset + 5] = 0x00;
            dst[offset + 6] = 0x80;
        }
        private static void WriteScaleUShort(ushort value, byte[] buffer, int byteOffset)
        {
            buffer[byteOffset + 0] = (byte)value;
            buffer[byteOffset + 1] = (byte)(value >> 8);
        }
        /// <summary>
        /// Voice traffic, which the harness previously left out entirely — a silent crowd is not what
        /// a real instance costs the server. Basis culls voice on the CLIENT: each player tells the
        /// server which peers are close enough to hear it, and the server routes only to that list.
        /// So the simulation has to do the same — build a recipient list from the spawn positions
        /// inside the audible radius, then transmit Opus-sized frames on the voice channel.
        ///
        /// Only a slice of the crowd talks at once, because everyone talking simultaneously is not a
        /// realistic load; it is a synthetic worst case that would swamp the measurement of everything
        /// else. Raise VoiceTalkingPercent to 100 if that worst case is what you want to see.
        /// </summary>
        public static class VoiceSender
        {
            private static ushort[][] _recipients;
            private static bool[] _participates;
            private static bool[] _talking;
            private static double[] _nextSwitchMs;
            private static byte[] _seq;
            private static byte[] _frame;
            private static int _built;

            public static void Initialize(int clientCount)
            {
                _recipients = new ushort[clientCount][];
                _participates = new bool[clientCount];
                _talking = new bool[clientCount];
                _nextSwitchMs = new double[clientCount];
                _seq = new byte[clientCount];
                _built = 0;

                int frameBytes = Math.Max(1, Basis.Config.ConfigManager.VoiceBytesPerFrame);
                _frame = new byte[frameBytes];
                Random.Shared.NextBytes(_frame);

                int percent = Math.Clamp(Basis.Config.ConfigManager.VoiceParticipantPercent, 0, 100);
                for (int i = 0; i < clientCount; i++)
                {
                    _participates[i] = Random.Shared.Next(100) < percent;
                    // Start everyone silent and stagger the first burst, so a run does not open with
                    // the entire crowd unmuting on the same tick.
                    _talking[i] = false;
                    _nextSwitchMs[i] = Random.Shared.Next(0, Math.Max(1, Basis.Config.ConfigManager.VoiceSilenceMaxMs));
                }
            }

            /// <summary>
            /// Speech is bursty: a person says something for a few seconds, then listens. Modelling it
            /// as a fixed always-on subset gets the average bitrate roughly right but none of the
            /// shape — no silence gaps, no changing set of speakers, and every recipient list exercised
            /// continuously rather than intermittently. Each participant alternates burst/silence with
            /// randomised durations, so who is talking keeps changing and most are quiet at any moment.
            /// </summary>
            public static bool IsTalking(int index, double nowMs)
            {
                if (_participates == null || index >= _participates.Length || !_participates[index]) return false;

                // Alone in the world: nobody is inside the audible radius, so there is no one to talk
                // to and a real client transmits nothing at all. Hold the burst clock too, so an
                // isolated player does not silently burn through its talk window and come back wrong.
                ushort[] audience = _recipients?[index];
                if (audience == null || audience.Length == 0)
                {
                    _talking[index] = false;
                    return false;
                }

                if (nowMs >= _nextSwitchMs[index])
                {
                    _talking[index] = !_talking[index];
                    int min = _talking[index] ? Basis.Config.ConfigManager.VoiceTalkBurstMinMs : Basis.Config.ConfigManager.VoiceSilenceMinMs;
                    int max = _talking[index] ? Basis.Config.ConfigManager.VoiceTalkBurstMaxMs : Basis.Config.ConfigManager.VoiceSilenceMaxMs;
                    if (max <= min) max = min + 1;
                    _nextSwitchMs[index] = nowMs + Random.Shared.Next(min, max);
                }
                return _talking[index];
            }

            /// <summary>
            /// Recipient lists are derived from spawn positions, which do not move in this harness,
            /// so this is computed once per client rather than every frame. Returns false until the
            /// peer has a server-assigned id to advertise.
            /// </summary>
            public static bool BuildRecipients(NetPeer[] peers, int index)
            {
                if (_recipients == null || index >= _recipients.Length) return false;
                if (_recipients[index] != null) return true;
                if (PlayersCurrentPosition == null || index >= PlayersCurrentPosition.Length) return false;

                float range = Basis.Config.ConfigManager.VoiceRangeMeters;
                float rangeSq = range * range;
                Vector3 self = PlayersCurrentPosition[index];

                List<ushort> near = new List<ushort>();
                for (int j = 0; j < peers.Length && j < PlayersCurrentPosition.Length; j++)
                {
                    if (j == index) continue;
                    NetPeer other = Volatile.Read(ref peers[j]);
                    if (other == null) continue;

                    Vector3 p = PlayersCurrentPosition[j];
                    float dx = p.x - self.x, dy = p.y - self.y, dz = p.z - self.z;
                    if (dx * dx + dy * dy + dz * dz <= rangeSq)
                    {
                        near.Add((ushort)other.RemoteId);
                    }
                }

                _recipients[index] = near.ToArray();
                Interlocked.Increment(ref _built);
                return true;
            }

            public static void SendRecipients(NetPeer peer, int index)
            {
                ushort[] list = _recipients?[index];
                if (list == null) return;

                // The count is byte-width on the small channel, so anything past 255 recipients has
                // to go out on the large one or the server reads a truncated list.
                bool large = list.Length > byte.MaxValue;
                NetDataWriter writer = new NetDataWriter();
                if (large) writer.Put((ushort)list.Length);
                else writer.Put((byte)list.Length);
                for (int i = 0; i < list.Length; i++) writer.Put(list[i]);

                peer.Send(writer,
                    large ? BasisNetworkCommons.AudioRecipientsLargeChannel : BasisNetworkCommons.AudioRecipientsChannel,
                    DeliveryMethod.ReliableOrdered);
            }

            public static void SendFrame(NetPeer peer, int index)
            {
                if (_frame == null || _recipients?[index] == null || _recipients[index].Length == 0) return;

                NetDataWriter writer = new NetDataWriter();
                writer.Put(_seq[index]++);
                writer.Put((byte)0);
                writer.Put(_frame);
                peer.Send(writer, BasisNetworkCommons.VoiceChannel, DeliveryMethod.Sequenced);
            }

            public static int BuiltCount => Volatile.Read(ref _built);
        }

        public static void ProcessSingle(NetPeer peer, int index)
        {
            if (peer == null) return;

            ref PlayerData pd = ref ActivePlayerData[index];

            double time = AnimTimer.Elapsed.TotalSeconds;
            float phase = pd.PhaseOffset;

            // Update position (held fixed when pinned to a distance tier)
            if (PinSpacingMeters <= 0f)
            {
                PlayersCurrentPosition[index] += Randomizer.GetRandomOffset();
            }

            var msg = pd.Message;

            // 1) Position (first 12 bytes)
            int offset = 0;
            WritePosition(PlayersCurrentPosition[index], ref msg.array, ref offset);

            // 2) Animated bone rotations (natural pose + idle animation, all 51 bones fresh per send)
            FakePoseGenerator.WriteBoneRotations(msg.array, RotationRegionOffset, BitQuality.High, time, phase);

            // 3) Scale unchanged

            // 4) Animated hips rotation
            FakePoseGenerator.WriteCompressedHipsRotation(msg.array, HipsRotationOffset, time, phase);

            byte seq = pd.SequenceByte;
            unchecked { pd.SequenceByte++; }

            // Face-data test mode: ride one AdditionalAvatarData on this frame, exactly like the
            // real client ships HVR high-frequency face variables (messageIndex 1, payload
            // [16][timing][counter…]). The per-sender counter lets the observer verify ordering.
            bool hasAdditional = false;
            if (EmitFaceData)
            {
                int counter = unchecked((ushort)(++pd.FaceCounter));
                pd.FaceScratch ??= new AdditionalAvatarData[1];
                pd.FaceScratch[0] = new AdditionalAvatarData
                {
                    messageIndex = 1,
                    array = new byte[] { 16, 1, (byte)(counter & 0xFF), (byte)((counter >> 8) & 0xFF), 200, 150, 100 },
                };
                msg.AdditionalAvatarDatas = pd.FaceScratch;
                msg.LinkedAvatarIndex = 0;
                hasAdditional = true;
            }
            else
            {
                msg.AdditionalAvatarDatas = null;
                msg.AdditionalAvatarDataSize = 0;
            }

            long now = Stopwatch.GetTimestamp();
            bool keyframe = !UseUplinkDeltas
                || pd.ForceKeyframe
                || !pd.HasBaseline
                || pd.Baseline == null
                || pd.Baseline.Length != msg.array.Length
                || now - pd.LastKeyframeTicks >= UplinkKeyframeIntervalTicks;

            int deltaLen = -1;
            if (!keyframe)
            {
                int cap = BasisAvatarDeltaCompression.MaxDeltaSize(BitQuality.High);
                if (pd.DeltaScratch == null || pd.DeltaScratch.Length < cap)
                    pd.DeltaScratch = new byte[cap];
                deltaLen = BasisAvatarDeltaCompression.BuildDelta(pd.Baseline, msg.array, BitQuality.High, pd.DeltaScratch, 0);
                if (deltaLen < 0 || deltaLen >= msg.array.Length) keyframe = true;
            }

            var writer = pd.Writer;
            writer.Reset();
            if (keyframe)
            {
                // Full keyframe on the High channel — the server snapshots it as this
                // sender's uplink delta baseline. Odd channel when additional data rides along.
                writer.Put(seq);
                msg.SerializeForChannel(writer, BitQuality.High);
                byte channel = BasisNetworkCommons.GetPlayerAvatarChannelForQuality((int)BitQuality.High, hasAdditional);
                peer.Send(writer, channel, DeliveryMethod.Unreliable);

                if (UseUplinkDeltas)
                {
                    if (pd.Baseline == null || pd.Baseline.Length != msg.array.Length)
                        pd.Baseline = new byte[msg.array.Length];
                    System.Array.Copy(msg.array, pd.Baseline, msg.array.Length);
                    pd.BaselineSeq = seq;
                    pd.HasBaseline = true;
                    pd.LastKeyframeTicks = now;
                    pd.ForceKeyframe = false;
                }
            }
            else
            {
                // v42 uplink delta: [hdr][seq][baseSeq][body][additional?] on DeltaAvatarChannel.
                writer.Put(BasisNetworkCommons.BuildDeltaHeader((int)BitQuality.High, hasAdditional, false));
                writer.Put(seq);
                writer.Put(pd.BaselineSeq);
                writer.Put(pd.DeltaScratch, 0, deltaLen);
                if (hasAdditional) msg.SerializeAdditionalOnly(writer);
                peer.Send(writer, BasisNetworkCommons.DeltaAvatarChannel, DeliveryMethod.Unreliable);
            }

            pd.Message = msg;
        }

        public static void WritePosition(Scripts.Networking.Compression.Vector3 position, ref byte[] buffer, ref int offset)
        {
            unsafe
            {
                fixed (byte* dst = &buffer[offset])
                {
                    float* f = (float*)dst;
                    f[0] = position.x;
                    f[1] = position.y;
                    f[2] = position.z;
                }
            }
            offset += 12;
        }

        public unsafe static void WriteQuaternionToBytes(Quaternion q, ref byte[] bytes, ref int offset)
        {
            fixed (byte* ptr = &bytes[offset])
            {
                *((float*)ptr) = float.IsNaN(q.value.x) ? 0f : q.value.x;
                *((float*)(ptr + 4)) = float.IsNaN(q.value.y) ? 0f : q.value.y;
                *((float*)(ptr + 8)) = float.IsNaN(q.value.z) ? 0f : q.value.z;
                *((float*)(ptr + 12)) = float.IsNaN(q.value.w) ? 1f : q.value.w;
            }

            offset += 16;
        }

        private static ushort CompressScaleOnce(float scale)
        {
            if (scale != 1f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(scale), scale, "MovementSender only supports precomputed scale 1.0.");
            }

            return 0x4000;
        }

        public static void WriteUShort(ushort value, ref byte[] bytes, ref int offset)
        {
            bytes[offset++] = (byte)value;
            bytes[offset++] = (byte)(value >> 8);
        }
    }
}
