using Basis.Network.Core;
using Basis.Network.Server.Generic;
using BasisNetworkCore;
using System.Net;
using System.Text;
using Xunit;

namespace BasisServerTests;

/// <summary>
/// Records what the cache actually put on the wire. The channel matters as much as the bytes: a
/// client routes scene data to a different handler table per channel, so a payload delivered on the
/// wrong one is silently never dispatched.
/// </summary>
internal sealed class ImageCacheRecordingPeer : NetPeer
{
    public readonly List<(byte Channel, byte[] Data)> Sent = new();

    public ImageCacheRecordingPeer(int id) => Id = id;

    public int Id { get; }
    public IPAddress Address => IPAddress.Loopback;
    public int RemoteId => Id;
    public int RoundTripTime => 0;
    public float TimeSinceLastPacket => 0f;
    public long RemoteTimeDelta => 0;
    public int Mtu => 1200;
    public object? Tag { get; set; }

    public void Disconnect() { }
    public void Disconnect(byte[] b) { }
    public void DisconnectForce() { }

    public void Send(byte[] data, byte channelNumber, DeliveryMethod deliveryMethod) =>
        Sent.Add((channelNumber, (byte[])data.Clone()));

    public void Send(NetDataWriter data, byte channelNumber, DeliveryMethod deliveryMethod) =>
        Sent.Add((channelNumber, data.CopyData()));

    public void SendUnreliableRawMerge(byte[] data, int offset, int length, byte channelNumber, int patchOffset = -1, byte patchValue = 0) { }

    public int GetPacketsCountInQueue(byte channel, DeliveryMethod deliveryMethod) => 0;
}

/// <summary>
/// Covers the server-side image buffer: what it retains, when it hands images to a joiner, and the
/// per-owner fairness that stops one player's uploads crowding out everyone else's.
///
/// These drive <see cref="BasisNetworkImageCache.Observe"/> with the same bytes a client puts on
/// the wire, so the header walking (including the variable-length owner name) is exercised for real
/// rather than against a convenient stand-in.
/// </summary>
// Mutates NetworkServer.Configuration and BasisNetworkIDDatabase, both process-wide statics, so it
// shares the collection that serialises every other test touching them.
[Collection("BasisServer shared network statics")]
public class BasisNetworkImageCacheTests : IDisposable
{
    /// <summary>Chunk payload sized so a megabyte-granularity budget still exercises eviction.</summary>
    private const int ChunkBytes = 64 * 1024;

    private const byte OpSpawn = 1;
    private const byte OpChunk = 2;
    private const byte OpDespawn = 4;
    private const byte OpAnimationSpawn = 6;
    private const byte OpAnimationChunk = 7;

    private const ushort ManagerNetId = 4242;

    private readonly Configuration _previous;
    private readonly List<int> _registeredPeerIds = new();

    public BasisNetworkImageCacheTests()
    {
        _previous = NetworkServer.Configuration;
        NetworkServer.Configuration = new Configuration
        {
            ImageCacheEnabled = true,
            ImageCacheMaxMegabytes = 4,
            ImageCacheMinimumPerOwnerMegabytes = 0,
            // Replay inline, which is what 0 means. These tests are about WHAT the cache serves,
            // not how fast; leaving pacing on would make every one of them drive a pump to observe
            // a result that has nothing to do with what it is asserting. The paced path has its own
            // tests in BasisImageBandwidthGovernorTests.
            ImageShareDownloadMegabitsPerSecond = 0,
            ImagePickupRangeMeters = 0f,
        };

        BasisNetworkImageCache.Reset();
        BasisImageBandwidthGovernor.Reset();
        BasisNetworkIDDatabase.UshortNetworkDatabase[BasisNetworkImageCache.ImageManagerIdentifier] = ManagerNetId;
    }

    public void Dispose()
    {
        BasisNetworkImageCache.Reset();
        BasisNetworkIDDatabase.UshortNetworkDatabase.TryRemove(BasisNetworkImageCache.ImageManagerIdentifier, out _);
        NetworkServer.Configuration = _previous;
        foreach (int id in _registeredPeerIds)
        {
            NetworkServer.AuthenticatedPeers.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Registers a stand-in peer the cache can reach through <see cref="NetworkServer.AuthenticatedPeers"/>,
    /// remembering it so teardown removes only what this fixture added — the dictionary is shared with
    /// every other test in the collection.
    /// </summary>
    private ImageCacheRecordingPeer RegisterPeer(int id)
    {
        ImageCacheRecordingPeer peer = new ImageCacheRecordingPeer(id);
        NetworkServer.AuthenticatedPeers[id] = peer;
        _registeredPeerIds.Add(id);
        return peer;
    }

    // ---- wire helpers: byte-for-byte what BasisImagePickupManager encodes -------------------

    private static byte[] EncodeSpawn(Guid id, ushort ownerId, string ownerName, int totalChunks)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(OpSpawn);
        writer.Write(id.ToByteArray());
        writer.Write(ownerId);
        writer.Write(ownerName);
        writer.Write(64);
        writer.Write(64);
        writer.Write(totalChunks * 16);
        writer.Write(totalChunks);
        for (int axis = 0; axis < 7; axis++)
        {
            writer.Write(0f);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] EncodeChunk(Guid id, int chunkIndex, int payloadBytes, byte opcode = OpChunk)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(opcode);
        writer.Write(id.ToByteArray());
        writer.Write(chunkIndex);
        writer.Write(payloadBytes);
        writer.Write(new byte[payloadBytes]);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] EncodeAnimationSpawn(Guid id, int totalChunks)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(OpAnimationSpawn);
        writer.Write(id.ToByteArray());
        writer.Write((byte)2); // AnimationFormatNativeLz4
        writer.Write(totalChunks * 16);
        writer.Write(totalChunks);
        writer.Write(0L);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] EncodeDespawn(Guid id)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(OpDespawn);
        writer.Write(id.ToByteArray());
        writer.Flush();
        return stream.ToArray();
    }

    private static void Observe(ushort sender, byte[] payload)
    {
        // HandleScene reaches Observe only through IsImageTraffic, which is also what resolves the
        // manager's network id. Replaying and the owner notice both need that id, so going through
        // the same gate here keeps the fixture honest about the order the server does it in.
        BasisNetworkImageCache.IsImageTraffic(ManagerNetId);
        BasisNetworkImageCache.Observe(sender, payload, payload.Length);
    }

    private static Guid ShareImage(ushort owner, int chunks = 2, int chunkBytes = ChunkBytes, string name = "Sharer")
    {
        Guid id = Guid.NewGuid();
        Observe(owner, EncodeSpawn(id, owner, name, chunks));
        for (int index = 0; index < chunks; index++)
        {
            Observe(owner, EncodeChunk(id, index, chunkBytes));
        }
        return id;
    }

    // ---- identification --------------------------------------------------------------------

    [Fact]
    public void IsImageTraffic_MatchesOnlyTheImageManagersNetworkId()
    {
        Assert.True(BasisNetworkImageCache.IsImageTraffic(ManagerNetId));
        Assert.False(BasisNetworkImageCache.IsImageTraffic(ManagerNetId + 1));
    }

    // ---- retention -------------------------------------------------------------------------

    [Fact]
    public void AFullyReceivedImage_IsHeldAndServable()
    {
        ShareImage(owner: 7);

        Assert.Equal(1, BasisNetworkImageCache.Count);
        Assert.Equal(1, BasisNetworkImageCache.ServableCount);
        Assert.True(BasisNetworkImageCache.TotalBytes > 0);
    }

    [Fact]
    public void AnImageMissingChunks_IsHeldButNotServed()
    {
        // A joiner must never be handed a half-received picture; it stays pending until complete.
        Guid id = Guid.NewGuid();
        Observe(7, EncodeSpawn(id, 7, "Sharer", totalChunks: 3));
        Observe(7, EncodeChunk(id, 0, ChunkBytes));

        Assert.Equal(1, BasisNetworkImageCache.Count);
        Assert.Equal(0, BasisNetworkImageCache.ServableCount);
    }

    [Fact]
    public void NetIdZeroOwner_IsCachedLikeAnyOtherPlayer()
    {
        // Peer ids are handed out from zero up, so the first player to join is net id 0 and must
        // not be mistaken for a blank owner.
        ShareImage(owner: 0);

        Assert.Equal(1, BasisNetworkImageCache.ServableCount);
        Assert.True(BasisNetworkImageCache.BytesHeldFor(0) > 0);
    }

    [Fact]
    public void AnimationPayloads_AreHeldAlongsideTheStill()
    {
        Guid id = ShareImage(owner: 7);
        long stillOnly = BasisNetworkImageCache.TotalBytes;

        Observe(7, EncodeAnimationSpawn(id, totalChunks: 2));
        Observe(7, EncodeChunk(id, 0, ChunkBytes, OpAnimationChunk));
        Observe(7, EncodeChunk(id, 1, ChunkBytes, OpAnimationChunk));

        Assert.True(BasisNetworkImageCache.TotalBytes > stillOnly);
        Assert.Equal(1, BasisNetworkImageCache.Count);
    }

    [Fact]
    public void ARepeatedSpawnHeader_DoesNotDoubleCount()
    {
        Guid id = Guid.NewGuid();
        byte[] spawn = EncodeSpawn(id, 7, "Sharer", totalChunks: 1);

        Observe(7, spawn);
        long afterFirst = BasisNetworkImageCache.TotalBytes;
        Observe(7, spawn);

        Assert.Equal(afterFirst, BasisNetworkImageCache.TotalBytes);
        Assert.Equal(1, BasisNetworkImageCache.Count);
    }

    // ---- removal ---------------------------------------------------------------------------

    [Fact]
    public void TheOwnersDespawn_ClearsTheServerCopy()
    {
        Guid id = ShareImage(owner: 7);

        Observe(7, EncodeDespawn(id));

        Assert.Equal(0, BasisNetworkImageCache.Count);
        Assert.Equal(0, BasisNetworkImageCache.TotalBytes);
    }

    [Fact]
    public void ADespawnFromSomebodyElse_LeavesTheServerCopyAlone()
    {
        // Anyone may ask; only the player who shared it removes the server's copy.
        Guid id = ShareImage(owner: 7);

        Observe(9, EncodeDespawn(id));

        Assert.Equal(1, BasisNetworkImageCache.Count);
    }

    [Fact]
    public void RemoveRequest_ClearsRegardlessOfRequesterWhenNotOwnerGated()
    {
        // The moderation path removes on someone else's behalf, so it opts out of the owner gate.
        Guid id = ShareImage(owner: 7);

        Assert.True(BasisNetworkImageCache.Remove(id, requesterId: 9, ownerOnly: false));
        Assert.Equal(0, BasisNetworkImageCache.Count);
    }

    [Fact]
    public void WhenTheSharerDisconnects_TheirImagesAreDropped()
    {
        ShareImage(owner: 7);
        ShareImage(owner: 7);
        ShareImage(owner: 9);

        BasisNetworkImageCache.RemovePlayerImages(7);

        Assert.Equal(1, BasisNetworkImageCache.Count);
        Assert.Equal(0, BasisNetworkImageCache.BytesHeldFor(7));
        Assert.True(BasisNetworkImageCache.BytesHeldFor(9) > 0);
    }

    // ---- budget and fairness ---------------------------------------------------------------

    [Fact]
    public void AnImageBiggerThanTheWholeBuffer_IsNotCached()
    {
        NetworkServer.Configuration.ImageCacheMaxMegabytes = 1;

        ShareImage(owner: 7, chunks: 2, chunkBytes: 1024 * 1024);

        Assert.Equal(0, BasisNetworkImageCache.ServableCount);
        Assert.True(BasisNetworkImageCache.TotalBytes <= 1024 * 1024);
    }

    [Fact]
    public void TheBufferNeverExceedsItsCap()
    {
        NetworkServer.Configuration.ImageCacheMaxMegabytes = 1;
        long cap = 1024 * 1024;

        for (int index = 0; index < 40; index++)
        {
            ShareImage(owner: (ushort)(index % 4), chunks: 2);
            Assert.True(
                BasisNetworkImageCache.TotalBytes <= cap,
                $"cache overran its cap after {index + 1} shares"
            );
        }
    }

    [Fact]
    public void OnePlayerFloodingTheBuffer_CannotEvictAnotherPlayersImages()
    {
        // The fairness rule: an owner over their slice evicts their OWN oldest image. Without it,
        // whoever uploads most simply deletes everybody else's pictures from the cache.
        NetworkServer.Configuration.ImageCacheMaxMegabytes = 1;

        ShareImage(owner: 1, chunks: 1);
        long quietOwnerBytes = BasisNetworkImageCache.BytesHeldFor(1);
        Assert.True(quietOwnerBytes > 0);

        for (int index = 0; index < 30; index++)
        {
            ShareImage(owner: 2, chunks: 2);
        }

        Assert.Equal(quietOwnerBytes, BasisNetworkImageCache.BytesHeldFor(1));
    }

    [Fact]
    public void AnOwnerOverTheirShare_LosesTheirOwnOldestImageFirst()
    {
        NetworkServer.Configuration.ImageCacheMaxMegabytes = 1;

        Guid oldest = ShareImage(owner: 5, chunks: 1);
        for (int index = 0; index < 30; index++)
        {
            ShareImage(owner: 5, chunks: 1);
        }

        // The first image they shared is the first to go, and they still hold something.
        Assert.False(BasisNetworkImageCache.Remove(oldest, requesterId: 5, ownerOnly: true));
        Assert.True(BasisNetworkImageCache.BytesHeldFor(5) > 0);
    }

    // ---- disabled ---------------------------------------------------------------------------

    [Fact]
    public void WithTheCacheOff_NothingIsRetained()
    {
        NetworkServer.Configuration.ImageCacheEnabled = false;

        ShareImage(owner: 7);

        Assert.Equal(0, BasisNetworkImageCache.Count);
        Assert.Equal(0, BasisNetworkImageCache.TotalBytes);
    }

    [Fact]
    public void WithAZeroBudget_NothingIsRetained()
    {
        NetworkServer.Configuration.ImageCacheMaxMegabytes = 0;

        ShareImage(owner: 7);

        Assert.Equal(0, BasisNetworkImageCache.Count);
    }

    // ---- replay to a joiner ------------------------------------------------------------------

    [Fact]
    public void AJoinerIsServedTheSpawnAndEveryChunk()
    {
        ShareImage(owner: 7, chunks: 3);

        ImageCacheRecordingPeer joiner = new ImageCacheRecordingPeer(9);
        BasisNetworkImageCache.SendCachedImagesToPeer(joiner);

        Assert.Equal(4, joiner.Sent.Count);
    }

    [Fact]
    public void ReplayedImages_GoOutOnTheChannelTheImageManagerListensOn()
    {
        // The image pickup manager registers a *direct* scene handler, so its traffic reaches it
        // only on DirectSceneServerChannel. Replaying on SceneChannel lands in the other handler
        // table, where nothing is registered, and the joiner silently sees no image at all.
        ShareImage(owner: 7, chunks: 2);

        ImageCacheRecordingPeer joiner = new ImageCacheRecordingPeer(9);
        BasisNetworkImageCache.SendCachedImagesToPeer(joiner);

        Assert.NotEmpty(joiner.Sent);
        Assert.All(joiner.Sent, sent => Assert.Equal(BasisNetworkCommons.DirectSceneServerChannel, sent.Channel));
    }

    [Fact]
    public void AnIncompleteImage_IsNotReplayed()
    {
        Guid id = Guid.NewGuid();
        Observe(7, EncodeSpawn(id, 7, "Sharer", totalChunks: 3));
        Observe(7, EncodeChunk(id, 0, ChunkBytes));

        ImageCacheRecordingPeer joiner = new ImageCacheRecordingPeer(9);
        BasisNetworkImageCache.SendCachedImagesToPeer(joiner);

        Assert.Empty(joiner.Sent);
    }

    [Fact]
    public void WithFiniteImageRange_AJoinerIsServedNothingByTheServerCache()
    {
        ShareImage(owner: 7);
        NetworkServer.Configuration.ImagePickupRangeMeters = 64f;

        ImageCacheRecordingPeer joiner = new ImageCacheRecordingPeer(9);
        BasisNetworkImageCache.SendCachedImagesToPeer(joiner);

        Assert.Empty(joiner.Sent);
    }

    [Fact]
    public void WithFiniteImageRange_NothingIsRetained()
    {
        NetworkServer.Configuration.ImagePickupRangeMeters = 64f;

        ShareImage(owner: 7);

        Assert.Equal(0, BasisNetworkImageCache.BytesHeldFor(7));
        Assert.Equal(0, BasisNetworkImageCache.TotalBytes);
    }

    [Fact]
    public void WithTheCacheOff_AJoinerIsServedNothing()
    {
        ShareImage(owner: 7);
        NetworkServer.Configuration.ImageCacheEnabled = false;

        ImageCacheRecordingPeer joiner = new ImageCacheRecordingPeer(9);
        BasisNetworkImageCache.SendCachedImagesToPeer(joiner);

        Assert.Empty(joiner.Sent);
    }

    // ---- owner notice ------------------------------------------------------------------------

    [Fact]
    public void TheOwnerIsToldOnTheSameChannelWhenTheirImageBecomesServable()
    {
        // The owner only stops re-uploading to each arrival if this notice reaches its handler,
        // which is the direct table again.
        ImageCacheRecordingPeer owner = RegisterPeer(7);

        ShareImage(owner: 7, chunks: 2);

        Assert.NotEmpty(owner.Sent);
        Assert.All(owner.Sent, sent => Assert.Equal(BasisNetworkCommons.DirectSceneServerChannel, sent.Channel));
    }

    [Fact]
    public void EvictingAnImage_TellsItsOwnerTheyAreProvidingItAgain()
    {
        ImageCacheRecordingPeer owner = RegisterPeer(5);
        NetworkServer.Configuration.ImageCacheMaxMegabytes = 1;

        ShareImage(owner: 5, chunks: 1);
        int afterFirstShare = owner.Sent.Count;
        for (int index = 0; index < 30; index++)
        {
            ShareImage(owner: 5, chunks: 1);
        }

        Assert.True(owner.Sent.Count > afterFirstShare);
    }

    // ---- malformed input --------------------------------------------------------------------

    [Fact]
    public void MalformedPayloads_AreIgnoredWithoutThrowing()
    {
        Observe(7, new byte[0]);
        Observe(7, new byte[] { OpSpawn });
        Observe(7, new byte[] { OpChunk, 1, 2, 3 });

        byte[] truncatedSpawn = EncodeSpawn(Guid.NewGuid(), 7, "Sharer", totalChunks: 2);
        Observe(7, truncatedSpawn.AsSpan(0, 20).ToArray());

        Assert.Equal(0, BasisNetworkImageCache.Count);
    }
}
