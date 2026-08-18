using Basis.Network.Core;
using BasisNetworkCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using static SerializableBasis;

namespace Basis.Network.Server.Generic
{
    /// <summary>
    /// Keeps the bytes of every shared image in server RAM so a joining player can be handed the
    /// images already in the instance, instead of the original sharer having to send them all over
    /// again to each arrival.
    ///
    /// The cache is deliberately dumb about what an image *is*: it retains the client's own scene
    /// payloads verbatim and replays them stamped with the original owner's player id. A receiving
    /// client therefore sees exactly the OpSpawn/OpChunk stream it would have got from the owner,
    /// and needs no knowledge that the server answered instead.
    ///
    /// Lifetime matches what clients already do with images, so the cache can never show a joiner
    /// something the room cannot see: an entry is dropped on despawn and when its owner disconnects.
    /// </summary>
    public static class BasisNetworkImageCache
    {
        /// <summary>
        /// The image manager registers under this fixed string, so the server can resolve which
        /// dynamic NetworkID carries image traffic. Must match
        /// <c>BasisImagePickupManager.FixedNetworkIdentifier</c>.
        /// </summary>
        public const string ImageManagerIdentifier = "BasisImagePickupManager";

        // Mirrored from BasisImagePickupManager. Wire protocol — changing either side is a break.
        private const byte OpSpawn = 1;
        private const byte OpChunk = 2;
        private const byte OpTransform = 3;
        private const byte OpDespawn = 4;
        private const byte OpAnimationSpawn = 6;
        private const byte OpAnimationChunk = 7;

        /// <summary>
        /// Server → owner: "I am holding this image" / "I am no longer holding it". The owner skips
        /// re-sending held images to each arrival, which is the whole point of the buffer. Sent only
        /// to the owner and stamped with their own player id — the relay never echoes a sender to
        /// itself, so a message arriving under your own id cannot have come from another client.
        /// </summary>
        private const byte OpServerCacheState = 8;

        private const int OpcodeBytes = 1;
        private const int GuidBytes = 16;
        private const int HeaderBytes = OpcodeBytes + GuidBytes;

        /// <summary>Ceiling on a single owner name, matching the client's own read guard.</summary>
        private const int MaxOwnerNameBytes = 1024;

        /// <summary>
        /// A cached image. Stills arrive as OpSpawn + OpChunk; an animated image adds
        /// OpAnimationSpawn + OpAnimationChunk on top of the still that carries its poster frame.
        /// </summary>
        private sealed class CachedImage
        {
            public ushort OwnerId;
            public long Sequence;

            /// <summary>
            /// Where the card is standing, taken from the spawn header and kept current from the
            /// OpTransform stream. This is what makes the replay spatial: without it the cache can
            /// only offer a joiner everything or nothing.
            /// </summary>
            public float PositionX;
            public float PositionY;
            public float PositionZ;

            /// <summary>
            /// Who has already been handed this image. A player crossing the range boundary is served
            /// once and then left alone, so walking back and forth does not re-send megabytes.
            /// </summary>
            public readonly HashSet<ushort> Served = new HashSet<ushort>();

            public byte[] Spawn;
            public byte[][] Chunks;
            public int ChunksHeld;

            public byte[] AnimationSpawn;
            public byte[][] AnimationChunks;
            public int AnimationChunksHeld;

            public long Bytes;

            /// <summary>A still is servable once its spawn header and every chunk has landed.</summary>
            public bool StillComplete => Spawn != null && Chunks != null && ChunksHeld == Chunks.Length;

            /// <summary>
            /// An animation is only replayed alongside a complete still. A half-received animation is
            /// simply not sent — the joiner still gets the still image rather than nothing.
            /// </summary>
            public bool AnimationComplete =>
                AnimationSpawn != null && AnimationChunks != null && AnimationChunksHeld == AnimationChunks.Length;
        }

        private static readonly ConcurrentDictionary<Guid, CachedImage> Images =
            new ConcurrentDictionary<Guid, CachedImage>();

        private static readonly object Gate = new object();

        private static long _totalBytes;
        private static long _sequence;

        /// <summary>
        /// Resolved NetworkID of the image manager, or -1 until a client has asked for one. Ids are
        /// stable for the life of the database, so this is memoised rather than looked up by string
        /// on every scene message.
        /// </summary>
        private static int _managerNetId = -1;

        /// <summary>Bytes currently held. Diagnostics and tests.</summary>
        public static long TotalBytes => System.Threading.Interlocked.Read(ref _totalBytes);

        /// <summary>Number of complete or in-flight images held.</summary>
        public static int Count => Images.Count;

        /// <summary>
        /// How many held images are complete enough to hand to a joiner. Differs from
        /// <see cref="Count"/> while a share is still arriving.
        /// </summary>
        public static int ServableCount
        {
            get
            {
                int servable = 0;
                foreach (KeyValuePair<Guid, CachedImage> pair in Images)
                {
                    if (pair.Value.StillComplete)
                    {
                        servable++;
                    }
                }
                return servable;
            }
        }

        /// <summary>Test seam: lets the next <see cref="SweepRangeCatchUp"/> run without waiting.</summary>
        internal static void ResetSweepClockForTests() =>
            System.Threading.Volatile.Write(ref _nextSweepTicks, 0);

        /// <summary>Bytes currently held on behalf of one player.</summary>
        public static long BytesHeldFor(ushort ownerId)
        {
            lock (Gate)
            {
                return OwnerBytes(ownerId);
            }
        }

        private static bool Enabled => NetworkServer.Configuration?.ImageCacheEnabled ?? false;

        /// <summary>
        /// Replication range in metres, or 0 for unlimited. The server applies the same figure it
        /// advertises to clients, so a joiner is handed the cards a sharer would have sent them.
        /// </summary>
        private static float RangeMeters
        {
            get
            {
                float configured = NetworkServer.Configuration?.ImagePickupRangeMeters ?? 0f;
                return configured > 0f ? configured : 0f;
            }
        }

        /// <summary>How often the catch-up sweep looks for players who have come into range.</summary>
        private const long SweepIntervalTicks = TimeSpan.TicksPerSecond * 2;

        private static long _nextSweepTicks;

        private const long BytesPerMegabyte = 1024L * 1024L;

        private static long MaxBytes
        {
            get
            {
                int configured = NetworkServer.Configuration?.ImageCacheMaxMegabytes ?? 0;
                return configured > 0 ? configured * BytesPerMegabyte : 0;
            }
        }

        /// <summary>
        /// Every owner is guaranteed this much before fair-share division applies, so a busy
        /// instance cannot shrink each person's allowance to something that fits no image at all.
        /// </summary>
        private static long MinimumOwnerBytes
        {
            get
            {
                int configured = NetworkServer.Configuration?.ImageCacheMinimumPerOwnerMegabytes ?? 0;
                return configured > 0 ? configured * BytesPerMegabyte : 0;
            }
        }

        public static void Reset()
        {
            lock (Gate)
            {
                Images.Clear();
                System.Threading.Interlocked.Exchange(ref _totalBytes, 0);
                _sequence = 0;
                _managerNetId = -1;
                System.Threading.Volatile.Write(ref _nextSweepTicks, 0);
            }
        }

        /// <summary>
        /// True when this scene message is image traffic. Called for every scene message, so it is
        /// a memoised integer compare after the first resolve.
        /// </summary>
        public static bool IsImageTraffic(ushort messageIndex)
        {
            int known = System.Threading.Volatile.Read(ref _managerNetId);
            if (known >= 0)
            {
                return messageIndex == known;
            }

            if (BasisNetworkIDDatabase.UshortNetworkDatabase.TryGetValue(ImageManagerIdentifier, out ushort resolved))
            {
                System.Threading.Volatile.Write(ref _managerNetId, resolved);
                return messageIndex == resolved;
            }

            return false;
        }

        /// <summary>
        /// Feeds one relayed image payload to the cache. The caller keeps relaying exactly as before
        /// — this only observes, so a cache that rejects or misparses a message can never stop the
        /// live send. <paramref name="payload"/> is copied where retained; the caller may reuse it.
        /// </summary>
        public static void Observe(
            ushort senderId,
            byte[] payload,
            int payloadLength,
            ushort[] recipients = null,
            int recipientsSize = 0
        )
        {
            if (!Enabled || payload == null || payloadLength < HeaderBytes)
            {
                return;
            }

            try
            {
                byte opcode = payload[0];
                switch (opcode)
                {
                    case OpSpawn:
                        ObserveSpawn(senderId, payload, payloadLength, recipients, recipientsSize);
                        break;
                    case OpChunk:
                        ObserveChunk(senderId, payload, payloadLength, animation: false);
                        break;
                    case OpTransform:
                        ObserveTransform(payload, payloadLength);
                        break;
                    case OpAnimationSpawn:
                        ObserveAnimationSpawn(senderId, payload, payloadLength);
                        break;
                    case OpAnimationChunk:
                        ObserveChunk(senderId, payload, payloadLength, animation: true);
                        break;
                    case OpDespawn:
                        Remove(ReadGuid(payload), senderId, ownerOnly: true);
                        break;
                }
            }
            catch (Exception e)
            {
                // Never let a malformed payload take down the relay thread; the live send already
                // happened and a client that sends nonsense simply goes uncached.
                BNL.LogError($"Image cache ignored a malformed payload from {senderId}: {e.Message}");
            }
        }

        private static void ObserveSpawn(
            ushort senderId,
            byte[] payload,
            int payloadLength,
            ushort[] recipients,
            int recipientsSize
        )
        {
            Guid id = ReadGuid(payload);
            if (
                !TryReadSpawnHeader(
                    payload,
                    payloadLength,
                    out int totalChunks,
                    out float positionX,
                    out float positionY,
                    out float positionZ
                )
                || totalChunks <= 0
            )
            {
                return;
            }

            lock (Gate)
            {
                if (Images.ContainsKey(id))
                {
                    // Already tracked. A join re-send repeats the spawn header; keep the first copy.
                    return;
                }

                // Charge the chunk-array backbone, not just the header. totalChunks is client-supplied
                // and `new byte[totalChunks][]` costs totalChunks references; accounting only
                // payloadLength let a client register an enormous totalChunks (an unbounded
                // allocation, and gigabytes of retained backbone) while the cap saw a few bytes. With
                // the backbone charged, an implausible count trips `cost > cap` inside TryReserve and
                // is refused before anything is allocated.
                long cost = (long)payloadLength + (long)totalChunks * IntPtr.Size;
                if (!TryReserve(senderId, cost, id))
                {
                    return;
                }

                CachedImage entry = new CachedImage
                {
                    OwnerId = senderId,
                    Sequence = ++_sequence,
                    Spawn = Copy(payload, payloadLength),
                    Chunks = new byte[totalChunks][],
                    Bytes = cost,
                    PositionX = positionX,
                    PositionY = positionY,
                    PositionZ = positionZ,
                };
                SeedServedLocked(entry, recipients, recipientsSize);
                Images[id] = entry;
                System.Threading.Interlocked.Add(ref _totalBytes, cost);
            }
        }

        private static void ObserveAnimationSpawn(ushort senderId, byte[] payload, int payloadLength)
        {
            // opcode + guid + format(1) + totalBytes(4) + totalChunks(4) + epoch(8)
            const int ChunkCountOffset = HeaderBytes + 1 + 4;
            if (payloadLength < ChunkCountOffset + 4)
            {
                return;
            }

            Guid id = ReadGuid(payload);
            int totalChunks = BitConverter.ToInt32(payload, ChunkCountOffset);
            if (totalChunks <= 0)
            {
                return;
            }

            lock (Gate)
            {
                if (!Images.TryGetValue(id, out CachedImage entry) || entry.OwnerId != senderId)
                {
                    return;
                }
                if (entry.AnimationSpawn != null)
                {
                    return;
                }

                // Charge the animation chunk-array backbone too — same unbounded-allocation guard as
                // the still spawn above.
                long cost = (long)payloadLength + (long)totalChunks * IntPtr.Size;
                if (!TryReserve(senderId, cost, id))
                {
                    return;
                }

                entry.AnimationSpawn = Copy(payload, payloadLength);
                entry.AnimationChunks = new byte[totalChunks][];
                entry.Bytes += cost;
                System.Threading.Interlocked.Add(ref _totalBytes, cost);
            }
        }

        private static void ObserveChunk(ushort senderId, byte[] payload, int payloadLength, bool animation)
        {
            // opcode + guid + chunkIndex(4) + length(4) + bytes
            const int ChunkIndexOffset = HeaderBytes;
            if (payloadLength < ChunkIndexOffset + 8)
            {
                return;
            }

            Guid id = ReadGuid(payload);
            int chunkIndex = BitConverter.ToInt32(payload, ChunkIndexOffset);
            if (chunkIndex < 0)
            {
                return;
            }

            bool becameServable = false;
            lock (Gate)
            {
                if (!Images.TryGetValue(id, out CachedImage entry) || entry.OwnerId != senderId)
                {
                    return;
                }

                byte[][] slots = animation ? entry.AnimationChunks : entry.Chunks;
                if (slots == null || chunkIndex >= slots.Length || slots[chunkIndex] != null)
                {
                    return;
                }

                long cost = payloadLength;
                if (!TryReserve(senderId, cost, id))
                {
                    return;
                }

                slots[chunkIndex] = Copy(payload, payloadLength);
                if (animation)
                {
                    entry.AnimationChunksHeld++;
                }
                else
                {
                    entry.ChunksHeld++;
                }
                entry.Bytes += cost;
                System.Threading.Interlocked.Add(ref _totalBytes, cost);

                becameServable = !animation && entry.StillComplete;
            }

            if (becameServable)
            {
                NotifyOwner(senderId, id, held: true);
            }
        }

        /// <summary>
        /// Makes room for <paramref name="cost"/> on behalf of <paramref name="ownerId"/>. Fairness
        /// is per owner: an owner over their share evicts their OWN oldest images and never anyone
        /// else's, so one person filling the buffer cannot push out everybody else's pictures.
        /// Caller must hold <see cref="Gate"/>.
        /// </summary>
        private static bool TryReserve(ushort ownerId, long cost, Guid exclude)
        {
            long cap = MaxBytes;
            if (cap <= 0 || cost <= 0 || cost > cap)
            {
                return false;
            }

            long share = OwnerShare(ownerId, cap);
            if (cost > share)
            {
                // No amount of evicting this owner's other images makes a single oversized one fit.
                return false;
            }

            while (OwnerBytes(ownerId) + cost > share)
            {
                if (!EvictOldestOwnedBy(ownerId, exclude))
                {
                    return false;
                }
            }

            while (System.Threading.Interlocked.Read(ref _totalBytes) + cost > cap)
            {
                if (!EvictOldestOfHeaviestOwner(exclude))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Each distinct owner gets an equal slice of the buffer, never less than the configured
        /// minimum. The owner being admitted is counted even when they hold nothing yet, so the
        /// first image of a new sharer is sized against the room they are about to join.
        /// </summary>
        private static long OwnerShare(ushort ownerId, long cap)
        {
            HashSet<ushort> owners = new HashSet<ushort> { ownerId };
            foreach (KeyValuePair<Guid, CachedImage> pair in Images)
            {
                owners.Add(pair.Value.OwnerId);
            }

            long share = cap / Math.Max(1, owners.Count);
            long floor = Math.Min(cap, MinimumOwnerBytes);
            return Math.Max(share, floor);
        }

        private static long OwnerBytes(ushort ownerId)
        {
            long total = 0;
            foreach (KeyValuePair<Guid, CachedImage> pair in Images)
            {
                if (pair.Value.OwnerId == ownerId)
                {
                    total += pair.Value.Bytes;
                }
            }
            return total;
        }

        private static bool EvictOldestOwnedBy(ushort ownerId, Guid exclude)
        {
            Guid oldest = Guid.Empty;
            long oldestSequence = long.MaxValue;
            bool found = false;

            foreach (KeyValuePair<Guid, CachedImage> pair in Images)
            {
                if (pair.Value.OwnerId != ownerId || pair.Key == exclude)
                {
                    continue;
                }
                if (pair.Value.Sequence < oldestSequence)
                {
                    oldestSequence = pair.Value.Sequence;
                    oldest = pair.Key;
                    found = true;
                }
            }

            return found && DropLocked(oldest);
        }

        private static bool EvictOldestOfHeaviestOwner(Guid exclude)
        {
            Dictionary<ushort, long> byOwner = new Dictionary<ushort, long>();
            foreach (KeyValuePair<Guid, CachedImage> pair in Images)
            {
                byOwner.TryGetValue(pair.Value.OwnerId, out long held);
                byOwner[pair.Value.OwnerId] = held + pair.Value.Bytes;
            }

            ushort heaviest = 0;
            long heaviestBytes = -1;
            foreach (KeyValuePair<ushort, long> pair in byOwner)
            {
                if (pair.Value > heaviestBytes)
                {
                    heaviestBytes = pair.Value;
                    heaviest = pair.Key;
                }
            }

            return heaviestBytes >= 0 && EvictOldestOwnedBy(heaviest, exclude);
        }

        /// <summary>Caller must hold <see cref="Gate"/>.</summary>
        private static bool DropLocked(Guid id)
        {
            if (!Images.TryRemove(id, out CachedImage entry))
            {
                return false;
            }
            System.Threading.Interlocked.Add(ref _totalBytes, -entry.Bytes);

            // Tell the owner they are back on the hook for this one. Without it an evicted image
            // would silently stop reaching new arrivals: the owner still believes we hold it and
            // skips re-sending, and we no longer have anything to send.
            if (entry.StillComplete)
            {
                NotifyOwner(entry.OwnerId, id, held: false);
            }
            return true;
        }

        /// <summary>
        /// Tells one player whether the server is holding a given image of theirs. Best effort: a
        /// peer that has already gone simply is not there to tell.
        /// </summary>
        private static void NotifyOwner(ushort ownerId, Guid id, bool held)
        {
            int managerNetId = System.Threading.Volatile.Read(ref _managerNetId);
            if (managerNetId < 0)
            {
                return;
            }
            if (!NetworkServer.AuthenticatedPeers.TryGetValue(ownerId, out NetPeer owner))
            {
                return;
            }

            byte[] payload = new byte[HeaderBytes + 1];
            payload[0] = OpServerCacheState;
            Buffer.BlockCopy(id.ToByteArray(), 0, payload, OpcodeBytes, GuidBytes);
            payload[HeaderBytes] = held ? (byte)1 : (byte)0;

            NetDataWriter writer = NetworkServer.RentWriter();
            SendPayload(owner, writer, (ushort)managerNetId, ownerId, payload);
            NetworkServer.ReturnWriter(writer);
        }

        /// <summary>
        /// Drops a cached image. <paramref name="ownerOnly"/> restricts it to the player who shared
        /// it, which is what an OpDespawn off the wire gets: anyone may ask, only the owner's word
        /// removes the server's copy.
        /// </summary>
        public static bool Remove(Guid id, ushort requesterId, bool ownerOnly)
        {
            lock (Gate)
            {
                if (!Images.TryGetValue(id, out CachedImage entry))
                {
                    return false;
                }
                if (ownerOnly && entry.OwnerId != requesterId)
                {
                    return false;
                }
                return DropLocked(id);
            }
        }

        /// <summary>
        /// Drops everything a departing player shared. Clients already destroy that player's images
        /// on disconnect, so keeping them cached would hand a joiner pictures nobody else can see.
        /// </summary>
        public static void RemovePlayerImages(int peerId)
        {
            ushort ownerId = (ushort)peerId;
            lock (Gate)
            {
                List<Guid> doomed = new List<Guid>();
                foreach (KeyValuePair<Guid, CachedImage> pair in Images)
                {
                    if (pair.Value.OwnerId == ownerId)
                    {
                        doomed.Add(pair.Key);
                    }
                }

                for (int index = 0; index < doomed.Count; index++)
                {
                    DropLocked(doomed[index]);
                }

                foreach (KeyValuePair<Guid, CachedImage> pair in Images)
                {
                    pair.Value.Served.Remove(ownerId);
                }
            }
        }

        /// <summary>
        /// Replays the cached images an arriving peer is entitled to, in the order they were shared
        /// so the room rebuilds the way it was built. Each payload goes out stamped with its
        /// original owner, not the server, so the receiver attributes it correctly.
        ///
        /// With a finite range this serves nothing at the moment of joining - the peer has not sent
        /// a position yet - and <see cref="SweepRangeCatchUp"/> picks them up as soon as they have.
        /// </summary>
        public static void SendCachedImagesToPeer(NetPeer newConnection)
        {
            if (newConnection == null)
            {
                return;
            }
            ServePeer(newConnection, "joining");
        }

        /// <summary>
        /// Hands every player the cards that have come within range of them since the last pass.
        ///
        /// A join-time replay alone cannot do this. An arriving peer has not sent an avatar update
        /// yet, so the server does not know where they are standing; and once they are in, walking
        /// toward a card is exactly the case the range is there to serve. This is the server's half
        /// of the owner-driven catch-up the sharing client does, and it is why the cache can stay on
        /// with a finite range instead of being switched off by one.
        ///
        /// Self-throttled, so the tick can call it unconditionally.
        /// </summary>
        public static void SweepRangeCatchUp()
        {
            if (!Enabled)
            {
                return;
            }

            long now = DateTime.UtcNow.Ticks;
            if (now < System.Threading.Volatile.Read(ref _nextSweepTicks))
            {
                return;
            }
            System.Threading.Volatile.Write(ref _nextSweepTicks, now + SweepIntervalTicks);

            if (Images.IsEmpty)
            {
                return;
            }

            foreach (KeyValuePair<int, NetPeer> pair in NetworkServer.AuthenticatedPeers)
            {
                ServePeer(pair.Value, "in-range");
            }
        }

        /// <summary>
        /// Queues whatever <paramref name="peer"/> is owed and has not already been given. Returns
        /// without touching the ledger when nothing qualifies, so a quiet instance costs one
        /// dictionary walk per sweep.
        /// </summary>
        private static void ServePeer(NetPeer peer, string reason)
        {
            if (!Enabled || peer == null)
            {
                return;
            }

            int managerNetId = System.Threading.Volatile.Read(ref _managerNetId);
            if (managerNetId < 0)
            {
                return;
            }

            int peerId = peer.Id;
            if (peerId < 0 || peerId > ushort.MaxValue)
            {
                return;
            }
            ushort recipient = (ushort)peerId;

            float rangeMeters = RangeMeters;
            float x = 0f;
            float y = 0f;
            float z = 0f;
            if (rangeMeters > 0f && !TryGetPeerPosition(peerId, out x, out y, out z))
            {
                return;
            }

            // Flatten in the order the room was built, so the pump can meter the stream without
            // knowing anything about images. Ordering matters on the wire: a chunk before its spawn
            // header is discarded by the receiver.
            List<BasisImageBandwidthGovernor.PendingPayload> queued =
                new List<BasisImageBandwidthGovernor.PendingPayload>();

            lock (Gate)
            {
                List<KeyValuePair<Guid, CachedImage>> ordered =
                    new List<KeyValuePair<Guid, CachedImage>>(Images);
                ordered.Sort((left, right) => left.Value.Sequence.CompareTo(right.Value.Sequence));

                for (int index = 0; index < ordered.Count; index++)
                {
                    CachedImage entry = ordered[index].Value;
                    if (!entry.StillComplete || entry.OwnerId == recipient)
                    {
                        continue;
                    }
                    if (entry.Served.Contains(recipient))
                    {
                        continue;
                    }
                    if (!IsWithinRange(entry, rangeMeters, x, y, z))
                    {
                        continue;
                    }

                    entry.Served.Add(recipient);

                    queued.Add(new BasisImageBandwidthGovernor.PendingPayload(entry.OwnerId, entry.Spawn));
                    for (int chunk = 0; chunk < entry.Chunks.Length; chunk++)
                    {
                        queued.Add(new BasisImageBandwidthGovernor.PendingPayload(entry.OwnerId, entry.Chunks[chunk]));
                    }

                    if (entry.AnimationComplete)
                    {
                        queued.Add(new BasisImageBandwidthGovernor.PendingPayload(entry.OwnerId, entry.AnimationSpawn));
                        for (int chunk = 0; chunk < entry.AnimationChunks.Length; chunk++)
                        {
                            queued.Add(new BasisImageBandwidthGovernor.PendingPayload(entry.OwnerId, entry.AnimationChunks[chunk]));
                        }
                    }
                }
            }

            if (queued.Count == 0)
            {
                return;
            }

            // Paced when the operator has set a download rate, inline when they have not. Sending
            // inline is the historical behaviour and stays reachable deliberately: a small instance
            // on a fast LAN has nothing to gain from metering, and 0 should mean "as fast as it
            // will go" rather than some hidden default.
            BasisImageBandwidthGovernor.SendPayload = ReplaySinglePayload;
            if (BasisImageBandwidthGovernor.EnqueueReplay(peer, queued))
            {
                BNL.Log($"Image cache queued {queued.Count} payload(s) for {reason} peer {peerId} (paced).");
                return;
            }

            NetDataWriter writer = NetworkServer.RentWriter();
            int sent = 0;
            for (int index = 0; index < queued.Count; index++)
            {
                sent += SendPayload(peer, writer, (ushort)managerNetId, queued[index].OwnerId, queued[index].Payload);
            }
            NetworkServer.ReturnWriter(writer);

            if (sent > 0)
            {
                BNL.Log($"Image cache served {sent} payload(s) to {reason} peer {peerId}.");
            }
        }

        /// <summary>
        /// Pump callback: one metered payload, rented writer and all. Resolves the manager id at
        /// send time rather than capturing it, because a replay now spans many milliseconds and the
        /// id can be reset by a despawn of the whole manager in between.
        /// </summary>
        private static void ReplaySinglePayload(NetPeer peer, ushort ownerId, byte[] payload)
        {
            int managerNetId = System.Threading.Volatile.Read(ref _managerNetId);
            if (managerNetId < 0)
            {
                return;
            }

            NetDataWriter writer = NetworkServer.RentWriter();
            try
            {
                SendPayload(peer, writer, (ushort)managerNetId, ownerId, payload);
            }
            finally
            {
                NetworkServer.ReturnWriter(writer);
            }
        }

        private static int SendPayload(NetPeer peer, NetDataWriter writer, ushort managerNetId, ushort ownerId, byte[] payload)
        {
            if (payload == null)
            {
                return 0;
            }

            ServerSceneDataMessage message = new ServerSceneDataMessage
            {
                sceneDataMessage = new RemoteSceneDataMessage
                {
                    messageIndex = managerNetId,
                    payload = payload,
                    payloadLength = payload.Length,
                },
                playerIdMessage = new PlayerIdMessage
                {
                    playerID = ownerId,
                },
            };

            writer.Reset();
            message.Serialize(writer);
            NetworkServer.TrySend(peer, writer, BasisNetworkCommons.DirectSceneServerChannel, DeliveryMethod.ReliableOrdered);
            return 1;
        }

        private static Guid ReadGuid(byte[] payload)
        {
            byte[] raw = new byte[GuidBytes];
            Buffer.BlockCopy(payload, OpcodeBytes, raw, 0, GuidBytes);
            return new Guid(raw);
        }

        private static byte[] Copy(byte[] payload, int payloadLength)
        {
            byte[] copy = new byte[payloadLength];
            Buffer.BlockCopy(payload, 0, copy, 0, payloadLength);
            return copy;
        }

        /// <summary>
        /// Reads totalChunks out of an OpSpawn header. The owner name in front of it is a
        /// BinaryWriter string — a 7-bit encoded byte length then UTF8 — so the fields after it sit
        /// at a variable offset and have to be walked to rather than indexed.
        /// </summary>
        private static bool TryReadSpawnHeader(
            byte[] payload,
            int payloadLength,
            out int totalChunks,
            out float positionX,
            out float positionY,
            out float positionZ
        )
        {
            totalChunks = 0;
            positionX = 0f;
            positionY = 0f;
            positionZ = 0f;

            int offset = HeaderBytes + 2; // opcode + guid + ushort ownerId
            if (!TrySkipWireString(payload, payloadLength, ref offset))
            {
                return false;
            }

            // width, height, totalBytes, totalChunks
            offset += 4 + 4 + 4;
            if (offset + 4 > payloadLength)
            {
                return false;
            }

            totalChunks = BitConverter.ToInt32(payload, offset);
            offset += 4;

            // position, then the rotation the replay does not need
            if (offset + 12 > payloadLength)
            {
                return false;
            }

            positionX = BitConverter.ToSingle(payload, offset);
            positionY = BitConverter.ToSingle(payload, offset + 4);
            positionZ = BitConverter.ToSingle(payload, offset + 8);
            return true;
        }

        /// <summary>
        /// Keeps a held card's position current as it is carried around. Taken from whoever is
        /// sending transforms rather than the original owner, because a claimed pickup is driven by
        /// the player holding it.
        /// </summary>
        private static void ObserveTransform(byte[] payload, int payloadLength)
        {
            if (payloadLength < HeaderBytes + 12)
            {
                return;
            }

            Guid id = ReadGuid(payload);
            float x = BitConverter.ToSingle(payload, HeaderBytes);
            float y = BitConverter.ToSingle(payload, HeaderBytes + 4);
            float z = BitConverter.ToSingle(payload, HeaderBytes + 8);

            lock (Gate)
            {
                if (!Images.TryGetValue(id, out CachedImage entry))
                {
                    return;
                }
                entry.PositionX = x;
                entry.PositionY = y;
                entry.PositionZ = z;
            }
        }

        /// <summary>
        /// Marks whoever the sharer already sent this image to as served. The relay knows precisely
        /// who that was, and without seeding it here the catch-up sweep would hand the same picture a
        /// second time to everyone who was standing nearby when it was shared - the exact traffic the
        /// range is there to avoid. An untargeted share went to the whole room, so everyone counts.
        /// </summary>
        private static void SeedServedLocked(CachedImage entry, ushort[] recipients, int recipientsSize)
        {
            if (recipients != null && recipientsSize > 0)
            {
                int count = Math.Min(recipientsSize, recipients.Length);
                for (int index = 0; index < count; index++)
                {
                    entry.Served.Add(recipients[index]);
                }
                return;
            }

            foreach (KeyValuePair<int, NetPeer> pair in NetworkServer.AuthenticatedPeers)
            {
                int peerId = pair.Key;
                if (peerId >= 0 && peerId <= ushort.MaxValue)
                {
                    entry.Served.Add((ushort)peerId);
                }
            }
        }

        private static bool IsWithinRange(CachedImage entry, float rangeMeters, float x, float y, float z)
        {
            if (rangeMeters <= 0f)
            {
                return true;
            }
            float dx = entry.PositionX - x;
            float dy = entry.PositionY - y;
            float dz = entry.PositionZ - z;
            return dx * dx + dy * dy + dz * dz <= rangeMeters * rangeMeters;
        }

        /// <summary>
        /// A peer's position as the reduction system last saw it. False until they have sent their
        /// first avatar update, which matters: an arriving peer sits at the origin until then, and
        /// treating that as real would hand them every card standing near world zero.
        /// </summary>
        private static bool TryGetPeerPosition(int peerId, out float x, out float y, out float z)
        {
            x = 0f;
            y = 0f;
            z = 0f;
            if (
                !BasisNetworkServer
                    .BasisNetworkingReductionSystem
                    .BasisServerReductionSystemEvents
                    .playerStates.TryGetValue(peerId, out var state)
                || state == null
                || !state.IsActive
            )
            {
                return false;
            }
            x = state.Position.x;
            y = state.Position.y;
            z = state.Position.z;
            return true;
        }

        private static bool TrySkipWireString(byte[] payload, int payloadLength, ref int offset)
        {
            int length = 0;
            int shift = 0;
            while (true)
            {
                if (offset >= payloadLength || shift > 4 * 7)
                {
                    return false;
                }

                byte piece = payload[offset++];
                length |= (piece & 0x7F) << shift;
                if ((piece & 0x80) == 0)
                {
                    break;
                }
                shift += 7;
            }

            if (length < 0 || length > MaxOwnerNameBytes || offset + length > payloadLength)
            {
                return false;
            }

            offset += length;
            return true;
        }
    }
}
