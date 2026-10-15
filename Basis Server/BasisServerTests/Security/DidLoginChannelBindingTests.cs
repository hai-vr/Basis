using System.Diagnostics;
using System.Net;
using Basis.Contrib.Auth.DecentralizedIds;
using Basis.Contrib.Auth.DecentralizedIds.Newtypes;
using Basis.Contrib.Crypto;
using Basis.Network.Core;
using Basis.Network.Server.Generic;
using BasisNetworkCore.Security;
using BasisNetworkServer.Security;
using Xunit;
using static Basis.Network.Core.Compression.BasisAvatarBitPacking;
using static Basis.Network.Core.Serializable.SerializableBasis;
using static SerializableBasis;
using CryptoRng = System.Security.Cryptography.RandomNumberGenerator;

namespace BasisServerTests;

/// <summary>
/// DID login bound to the connection: the client-to-server key derivation, the strict per-peer
/// cipher layer, the signed login transcript, and a relaying server trying to reuse a player's
/// login on another server.
/// </summary>
[Collection("LiteNetLib global logger")]
public class DidLoginChannelBindingTests
{
    private const byte HeaderUnreliable = 0x00;
    private const byte HeaderChanneled = 0x01;
    private const byte HeaderAck = 0x02;
    private const byte HeaderPing = 0x03;
    private const byte HeaderMtuCheck = 0x09;
    private const byte HeaderShutdownOk = 0x0D;

    private sealed class Side
    {
        public byte[] Private = null!;
        public byte[] Public = null!;

        public static Side Create()
        {
            var side = new Side();
            BasisCryptoHandshake.GenerateKeyPair(out side.Private, out side.Public);
            return side;
        }
    }

    private static byte[] Packet(byte header, int payloadSize)
    {
        byte[] packet = new byte[1 + payloadSize + BasisCryptoLayer.Overhead];
        packet[0] = header;
        for (int i = 0; i < payloadSize; i++) packet[1 + i] = (byte)(3 + i * 7);
        return packet;
    }

    private static int Seal(BasisPeerCipherLayer layer, byte[] packet, int length)
    {
        IPEndPoint endPoint = null!;
        int offset = 0;
        layer.ProcessOutBoundPacket(ref endPoint, ref packet, ref offset, ref length);
        return length;
    }

    private static int Open(BasisPeerCipherLayer layer, byte[] packet, int length)
    {
        IPEndPoint endPoint = null!;
        layer.ProcessInboundPacket(ref endPoint, ref packet, ref length);
        return length;
    }

    private static byte[] ClientKey(Side client, Side server)
    {
        Assert.True(BasisCryptoHandshake.DeriveClientKey(client.Private, client.Public, server.Public, out byte[] key));
        return key;
    }

    private static byte[] ServerKey(Side client, Side server)
    {
        Assert.True(BasisCryptoHandshake.DeriveServerKey(server.Private, client.Public, server.Public, out byte[] key));
        return key;
    }

    private static (PrivKey Private, Did Did) NewPlayer()
    {
        byte[] privateBytes = new byte[Ed25519.PrivkeySize];
        CryptoRng.Fill(privateBytes);
        var privateKey = new PrivKey(privateBytes);
        PubKey publicKey = Ed25519.ConvertPrivkeyToPubkey(privateKey)!;
        return (privateKey, DidKeyResolver.EncodePubkeyAsDid(publicKey));
    }

    private static Signature Sign(PrivKey key, byte[] transcript)
    {
        Assert.True(Ed25519.Sign(key, new Payload(transcript), out Signature? sig));
        return sig!;
    }

    private static async Task<bool> Verifies(Did player, byte[] nonce, byte[] transcript, Signature signature)
    {
        var didAuth = new DidAuthentication(new Config());
        var challenge = new Challenge(player, new Nonce(nonce));
        var response = new Response(signature, new DidUrlFragment(string.Empty));
        return (await didAuth.VerifyResponse(response, challenge, new Payload(transcript))).IsOk;
    }

    private static byte[] Nonce()
    {
        byte[] nonce = new byte[BasisDidLoginTranscript.MinNonceSize];
        CryptoRng.Fill(nonce);
        return nonce;
    }

    private static byte[] PayloadHash()
    {
        byte[] hash = new byte[BasisDidLoginTranscript.PayloadHashSize];
        CryptoRng.Fill(hash);
        return hash;
    }

    internal static byte[] ConnectPayload(byte[] connectionKey, ReadyMessage ready)
    {
        var writer = new NetDataWriter();
        new BytesMessage().Serialize(writer, connectionKey);
        ready.Serialize(writer);
        return writer.CopyData();
    }

    // ------------------------------------------------------------ key derivation

    [Fact]
    public void ClientToServerKey_BothEndsDeriveTheSameKey()
    {
        Side client = Side.Create();
        Side server = Side.Create();
        Assert.Equal(ClientKey(client, server), ServerKey(client, server));
    }

    [Fact]
    public void ClientToServerKey_DependsOnBothConnectionKeys()
    {
        Side client = Side.Create();
        Side server = Side.Create();
        Side other = Side.Create();
        Assert.NotEqual(ClientKey(client, server), ClientKey(client, other));
        Assert.NotEqual(ClientKey(client, server), ClientKey(other, server));
    }

    [Fact]
    public void ClientToServerKey_DiffersFromThePeerToPeerKeys()
    {
        Side client = Side.Create();
        Side server = Side.Create();
        Assert.True(BasisCryptoHandshake.DerivePeerKeys(client.Private, client.Public, server.Public, out byte[] send, out byte[] recv));
        byte[] key = ClientKey(client, server);
        Assert.NotEqual(send, key);
        Assert.NotEqual(recv, key);
    }

    [Fact]
    public void ClientToServerKey_RejectsLowOrderAndMisSizedKeys()
    {
        Side client = Side.Create();
        Side server = Side.Create();
        byte[] zero = new byte[BasisCryptoHandshake.PublicKeySize];
        Assert.False(BasisCryptoHandshake.DeriveClientKey(client.Private, client.Public, zero, out _));
        Assert.False(BasisCryptoHandshake.DeriveClientKey(client.Private, client.Public, new byte[16], out _));
        Assert.False(BasisCryptoHandshake.DeriveServerKey(server.Private, zero, server.Public, out _));
        Assert.False(BasisCryptoHandshake.DeriveServerKey(server.Private, new byte[16], server.Public, out _));
    }

    // ------------------------------------------------------------ strict peer layer

    [Fact]
    public void PeerLayer_SealedUserDataOpensToTheOriginalBytes()
    {
        byte[] key = ClientKey(Side.Create(), Side.Create());
        var sender = new BasisPeerCipherLayer(key);
        var receiver = new BasisPeerCipherLayer(key);

        byte[] packet = Packet(HeaderChanneled, 40);
        byte[] original = packet.AsSpan(0, 41).ToArray();
        int sealedLength = Seal(sender, packet, 41);
        Assert.Equal(41 + BasisCryptoLayer.Overhead, sealedLength);
        Assert.False(packet.AsSpan(1, 40).SequenceEqual(original.AsSpan(1)));

        Assert.Equal(41, Open(receiver, packet, sealedLength));
        Assert.Equal(original, packet.AsSpan(0, 41).ToArray());
    }

    [Theory]
    [InlineData(HeaderUnreliable)]
    [InlineData(HeaderChanneled)]
    public void PeerLayer_DropsPlaintextUserData(byte header)
    {
        var receiver = new BasisPeerCipherLayer(ClientKey(Side.Create(), Side.Create()));
        byte[] packet = Packet(header, 60);
        Assert.Equal(0, Open(receiver, packet, packet.Length));
    }

    [Fact]
    public void PeerLayer_DropsUserDataSealedWithAnotherKey()
    {
        var sender = new BasisPeerCipherLayer(ClientKey(Side.Create(), Side.Create()));
        var receiver = new BasisPeerCipherLayer(ClientKey(Side.Create(), Side.Create()));
        byte[] packet = Packet(HeaderChanneled, 20);
        int sealedLength = Seal(sender, packet, 21);
        Assert.Equal(0, Open(receiver, packet, sealedLength));
    }

    [Fact]
    public void PeerLayer_SealsConnectionControlPacketsToo()
    {
        byte[] key = ClientKey(Side.Create(), Side.Create());
        var sender = new BasisPeerCipherLayer(key);
        var receiver = new BasisPeerCipherLayer(key);
        byte[] packet = Packet(HeaderAck, 8);
        byte[] original = packet.AsSpan(0, 9).ToArray();

        int sealedLength = Seal(sender, packet, 9);
        Assert.Equal(9 + BasisCryptoLayer.Overhead, sealedLength);
        Assert.Equal(9, Open(receiver, packet, sealedLength));
        Assert.Equal(original, packet.AsSpan(0, 9).ToArray());
    }

    [Fact]
    public void PeerLayer_TakesUnsealedControlOnlyUntilTheFirstSealedPacket()
    {
        byte[] key = ClientKey(Side.Create(), Side.Create());
        var sender = new BasisPeerCipherLayer(key);
        var receiver = new BasisPeerCipherLayer(key);

        Assert.Equal(9, Open(receiver, Packet(HeaderPing, 8), 9));
        Assert.False(receiver.Proven);
        Assert.False(receiver.AllowsEndPointChange);

        byte[] packet = Packet(HeaderChanneled, 16);
        Assert.Equal(17, Open(receiver, packet, Seal(sender, packet, 17)));
        Assert.True(receiver.Proven);
        Assert.True(receiver.AllowsEndPointChange);

        Assert.Equal(0, Open(receiver, Packet(HeaderPing, 8), 9));
        Assert.Equal(0, Open(receiver, Packet(HeaderAck, 8), 9));
        Assert.Equal(1, Open(receiver, Packet(HeaderShutdownOk, 0), 1));
        byte[] ping = Packet(HeaderPing, 8);
        Assert.Equal(9, Open(receiver, ping, Seal(sender, ping, 9)));
    }

    [Fact]
    public void PeerLayer_WithoutAKey_HoldsBackUserDataUntilOneArrives()
    {
        var waiting = new BasisPeerCipherLayer();
        Assert.False(waiting.HasKey);
        Assert.Equal(0, Open(waiting, Packet(HeaderChanneled, 16), 17));
        Assert.Equal(0, Open(waiting, Packet(HeaderUnreliable, 16), 17));
        Assert.Equal(9, Open(waiting, Packet(HeaderPing, 8), 9));
        Assert.False(waiting.AllowsEndPointChange);

        byte[] key = ClientKey(Side.Create(), Side.Create());
        waiting.SetKey(key);
        byte[] packet = Packet(HeaderChanneled, 16);
        Assert.Equal(17, Open(waiting, packet, Seal(new BasisPeerCipherLayer(key), packet, 17)));
        Assert.Equal(0, Open(waiting, Packet(HeaderChanneled, 16), 17));
    }

    [Fact]
    public void PeerLayer_WithoutAKey_SendsUnchangedButReservesRoomForTheSeal()
    {
        var reserved = new BasisPeerCipherLayer();
        Assert.Equal(BasisCryptoLayer.Overhead, reserved.ExtraPacketSizeForLayer);
        Assert.Equal(0, reserved.ExtraSizeFor(HeaderMtuCheck));
        Assert.Equal(17, Seal(reserved, Packet(HeaderChanneled, 16), 17));

        reserved.SetKey(ClientKey(Side.Create(), Side.Create()));
        Assert.Equal(BasisCryptoLayer.Overhead, reserved.ExtraSizeFor(HeaderMtuCheck));
        Assert.Equal(17 + BasisCryptoLayer.Overhead, Seal(reserved, Packet(HeaderChanneled, 16), 17));
    }

    // ------------------------------------------------------------ replay window

    private static byte[] SealedCopy(BasisPeerCipherLayer sender, int payloadSize)
    {
        byte[] packet = Packet(HeaderUnreliable, payloadSize);
        int length = Seal(sender, packet, 1 + payloadSize);
        return packet.AsSpan(0, length).ToArray();
    }

    private static int OpenCopy(BasisPeerCipherLayer receiver, byte[] sealedDatagram)
    {
        byte[] buffer = (byte[])sealedDatagram.Clone();
        return Open(receiver, buffer, buffer.Length);
    }

    [Fact]
    public void ReplayWindow_DropsADatagramSeenBefore()
    {
        byte[] key = ClientKey(Side.Create(), Side.Create());
        var sender = new BasisPeerCipherLayer(key);
        var receiver = new BasisPeerCipherLayer(key);
        byte[] datagram = SealedCopy(sender, 16);

        Assert.Equal(17, OpenCopy(receiver, datagram));
        Assert.Equal(0, OpenCopy(receiver, datagram));
    }

    [Fact]
    public void ReplayWindow_AcceptsReorderingInsideTheWindow()
    {
        byte[] key = ClientKey(Side.Create(), Side.Create());
        var sender = new BasisPeerCipherLayer(key);
        var receiver = new BasisPeerCipherLayer(key);
        byte[] first = SealedCopy(sender, 8);
        byte[] second = SealedCopy(sender, 8);
        byte[] third = SealedCopy(sender, 8);

        Assert.Equal(9, OpenCopy(receiver, third));
        Assert.Equal(9, OpenCopy(receiver, first));
        Assert.Equal(9, OpenCopy(receiver, second));
        Assert.Equal(0, OpenCopy(receiver, first));
    }

    [Fact]
    public void ReplayWindow_DropsADatagramOlderThanTheWindow()
    {
        byte[] key = ClientKey(Side.Create(), Side.Create());
        var sender = new BasisPeerCipherLayer(key);
        var receiver = new BasisPeerCipherLayer(key);
        byte[] held = SealedCopy(sender, 8);
        for (int i = 0; i < BasisPeerCipherLayer.ReplayWindowSize; i++)
        {
            Assert.Equal(9, OpenCopy(receiver, SealedCopy(sender, 8)));
        }

        Assert.Equal(0, OpenCopy(receiver, held));
    }

    [Fact]
    public void ReplayWindow_AJumpAheadStillRejectsWhatWasSeen()
    {
        byte[] key = ClientKey(Side.Create(), Side.Create());
        var sender = new BasisPeerCipherLayer(key);
        var receiver = new BasisPeerCipherLayer(key);
        byte[] early = SealedCopy(sender, 8);
        Assert.Equal(9, OpenCopy(receiver, early));
        for (int i = 0; i < 200; i++) SealedCopy(sender, 8);
        byte[] late = SealedCopy(sender, 8);

        Assert.Equal(9, OpenCopy(receiver, late));
        Assert.Equal(0, OpenCopy(receiver, early));
        Assert.Equal(0, OpenCopy(receiver, late));
    }

    [Fact]
    public void ReplayWindow_AJumpPastTheWholeWindowForgetsEverySlot()
    {
        byte[] key = ClientKey(Side.Create(), Side.Create());
        var sender = new BasisPeerCipherLayer(key);
        var receiver = new BasisPeerCipherLayer(key);
        var held = new List<byte[]>();
        for (int i = 0; i < 5; i++) held.Add(SealedCopy(sender, 8));
        Assert.Equal(9, OpenCopy(receiver, held[4]));
        for (int i = 0; i < BasisPeerCipherLayer.ReplayWindowSize * 3; i++) SealedCopy(sender, 8);
        var recent = new List<byte[]>();
        for (int i = 0; i < 70; i++) recent.Add(SealedCopy(sender, 8));

        Assert.Equal(9, OpenCopy(receiver, recent[^1]));
        for (int i = 0; i < recent.Count - 1; i++) Assert.Equal(9, OpenCopy(receiver, recent[i]));
        foreach (byte[] old in held) Assert.Equal(0, OpenCopy(receiver, old));
    }

    // ------------------------------------------------------------ login transcript

    [Fact]
    public async Task Transcript_SignedByThePlayer_VerifiesAtTheServerItNames()
    {
        var (key, did) = NewPlayer();
        Side client = Side.Create();
        Side server = Side.Create();
        byte[] nonce = Nonce();
        byte[] transcript = BasisDidLoginTranscript.Build(did.V, "did:key:server-b", nonce, client.Public, server.Public, PayloadHash());

        Assert.True(await Verifies(did, nonce, transcript, Sign(key, transcript)));
    }

    [Fact]
    public async Task Transcript_BareNonceSignature_NoLongerLogsIn()
    {
        var (key, did) = NewPlayer();
        byte[] nonce = Nonce();
        byte[] transcript = BasisDidLoginTranscript.Build(did.V, "did:key:server-b", nonce, Side.Create().Public, Side.Create().Public, PayloadHash());

        Assert.False(await Verifies(did, nonce, transcript, Sign(key, nonce)));
    }

    [Fact]
    public void Transcript_EveryFieldChangesTheSignedBytes()
    {
        byte[] nonce = Nonce();
        byte[] c = Side.Create().Public;
        byte[] s = Side.Create().Public;
        byte[] h = PayloadHash();
        byte[] baseline = BasisDidLoginTranscript.Build("did:key:a", "did:key:b", nonce, c, s, h);

        Assert.NotEqual(baseline, BasisDidLoginTranscript.Build("did:key:x", "did:key:b", nonce, c, s, h));
        Assert.NotEqual(baseline, BasisDidLoginTranscript.Build("did:key:a", "did:key:x", nonce, c, s, h));
        Assert.NotEqual(baseline, BasisDidLoginTranscript.Build("did:key:a", "did:key:b", Nonce(), c, s, h));
        Assert.NotEqual(baseline, BasisDidLoginTranscript.Build("did:key:a", "did:key:b", nonce, Side.Create().Public, s, h));
        Assert.NotEqual(baseline, BasisDidLoginTranscript.Build("did:key:a", "did:key:b", nonce, c, Side.Create().Public, h));
        Assert.NotEqual(baseline, BasisDidLoginTranscript.Build("did:key:a", "did:key:b", nonce, c, s, PayloadHash()));
        // Length prefixes keep a byte from moving across a field boundary unnoticed.
        Assert.NotEqual(
            BasisDidLoginTranscript.Build("did:key:ab", "c", nonce, c, s, h),
            BasisDidLoginTranscript.Build("did:key:a", "bc", nonce, c, s, h));
    }

    [Fact]
    public void ConnectPayloadHash_CoversEveryByteAndNothingAround()
    {
        byte[] payload = new byte[64];
        CryptoRng.Fill(payload);
        byte[] baseline = BasisDidLoginTranscript.HashConnectionPayload(payload, 0, payload.Length);
        Assert.Equal(BasisDidLoginTranscript.PayloadHashSize, baseline.Length);
        for (int i = 0; i < payload.Length; i++)
        {
            byte[] changed = (byte[])payload.Clone();
            changed[i] ^= 0x01;
            Assert.NotEqual(baseline, BasisDidLoginTranscript.HashConnectionPayload(changed, 0, changed.Length));
        }
        Assert.NotEqual(baseline, BasisDidLoginTranscript.HashConnectionPayload(payload, 0, payload.Length - 1));

        byte[] framed = new byte[payload.Length + 7];
        CryptoRng.Fill(framed);
        payload.CopyTo(framed, 3);
        Assert.Equal(baseline, BasisDidLoginTranscript.HashConnectionPayload(framed, 3, payload.Length));
    }

    // ------------------------------------------------------------ relay

    [Fact]
    public async Task Relay_ReplacingThePlayersConnectionKey_BreaksTheSignature()
    {
        // The player connects to relay A. A opens its own connection to B under the player's DID,
        // using its own connection key so that it would hold B's session key, and forwards B's
        // challenge to the player.
        var (key, did) = NewPlayer();
        Side player = Side.Create();
        Side relay = Side.Create();
        Side serverB = Side.Create();
        byte[] nonceB = Nonce();
        byte[] payload = PayloadHash();

        byte[] playerSigned = BasisDidLoginTranscript.Build(did.V, "did:key:server-b", nonceB, player.Public, serverB.Public, payload);
        byte[] serverBExpects = BasisDidLoginTranscript.Build(did.V, "did:key:server-b", nonceB, relay.Public, serverB.Public, payload);

        Assert.False(await Verifies(did, nonceB, serverBExpects, Sign(key, playerSigned)));
    }

    [Fact]
    public async Task Relay_ChangingWhatThePlayerSentOnConnect_BreaksTheSignature()
    {
        var (key, did) = NewPlayer();
        Side player = Side.Create();
        Side serverB = Side.Create();
        byte[] nonceB = Nonce();
        byte[] sent = ConnectPayload(player.Public, LifecycleSupport.MakeReady(did.V, "Connie"));
        byte[] altered = ConnectPayload(player.Public, LifecycleSupport.MakeReady(did.V, "Somebody Else"));

        byte[] playerSigned = BasisDidLoginTranscript.Build(did.V, "did:key:server-b", nonceB, player.Public, serverB.Public, BasisDidLoginTranscript.HashConnectionPayload(sent, 0, sent.Length));
        byte[] serverBExpects = BasisDidLoginTranscript.Build(did.V, "did:key:server-b", nonceB, player.Public, serverB.Public, BasisDidLoginTranscript.HashConnectionPayload(altered, 0, altered.Length));

        Assert.True(await Verifies(did, nonceB, playerSigned, Sign(key, playerSigned)));
        Assert.False(await Verifies(did, nonceB, serverBExpects, Sign(key, playerSigned)));
    }

    [Fact]
    public async Task Relay_ForwardingThePlayersConnectionKey_LeavesTheRelayUnableToSend()
    {
        // A forwards everything unchanged, so B accepts the login, but B now only takes user data
        // sealed with a key derived from the player's private connection key.
        var (key, did) = NewPlayer();
        Side player = Side.Create();
        Side relay = Side.Create();
        Side serverB = Side.Create();
        byte[] nonceB = Nonce();

        byte[] transcript = BasisDidLoginTranscript.Build(did.V, "did:key:server-b", nonceB, player.Public, serverB.Public, PayloadHash());
        Assert.True(await Verifies(did, nonceB, transcript, Sign(key, transcript)));

        var serverBInbound = new BasisPeerCipherLayer(ServerKey(player, serverB));

        // Anything A can derive uses its own private key, never the player's.
        Assert.True(BasisCryptoHandshake.DeriveClientKey(relay.Private, player.Public, serverB.Public, out byte[] relayGuess));
        byte[] forged = Packet(HeaderChanneled, 32);
        int forgedLength = Seal(new BasisPeerCipherLayer(relayGuess), forged, 33);
        Assert.Equal(0, Open(serverBInbound, forged, forgedLength));

        byte[] plaintext = Packet(HeaderChanneled, 32);
        Assert.Equal(0, Open(serverBInbound, plaintext, 33));

        // The player's own packets still get through.
        byte[] genuine = Packet(HeaderChanneled, 32);
        int genuineLength = Seal(new BasisPeerCipherLayer(ClientKey(player, serverB)), genuine, 33);
        Assert.Equal(33, Open(serverBInbound, genuine, genuineLength));
        Assert.Equal(0, Open(serverBInbound, Packet(HeaderPing, 8), 9));
    }

    [Fact]
    public void Relay_ResendingThePlayersSealedPackets_IsDropped()
    {
        // A relay that passes traffic through sees every sealed datagram the player sends and can
        // send any of them to the server again.
        Side player = Side.Create();
        Side serverB = Side.Create();
        var playerOutbound = new BasisPeerCipherLayer(ClientKey(player, serverB));
        var serverBInbound = new BasisPeerCipherLayer(ServerKey(player, serverB));

        byte[] command = SealedCopy(playerOutbound, 24);
        Assert.Equal(25, OpenCopy(serverBInbound, command));
        for (int i = 0; i < 10; i++) Assert.Equal(25, OpenCopy(serverBInbound, SealedCopy(playerOutbound, 24)));

        Assert.Equal(0, OpenCopy(serverBInbound, command));
    }

    [Fact]
    public async Task Relay_SignatureForOneServer_DoesNotLogInToAnother()
    {
        var (key, did) = NewPlayer();
        Side player = Side.Create();
        Side server = Side.Create();
        byte[] nonce = Nonce();
        byte[] payload = PayloadHash();

        byte[] forA = BasisDidLoginTranscript.Build(did.V, "did:key:server-a", nonce, player.Public, server.Public, payload);
        byte[] forB = BasisDidLoginTranscript.Build(did.V, "did:key:server-b", nonce, player.Public, server.Public, payload);

        Assert.False(await Verifies(did, nonce, forB, Sign(key, forA)));
    }

    // ------------------------------------------------------------ LiteNetLib peer layers

    [Fact]
    public void LiteNetLib_InboundPeerLayer_DropsPlaintextAndAcceptsSealedData()
    {
        byte[] key = ClientKey(Side.Create(), Side.Create());

        var serverListener = new LiteNetLib.EventBasedNetListener();
        var clientListener = new LiteNetLib.EventBasedNetListener();
        var server = new LiteNetLib.NetManager(serverListener);
        var client = new LiteNetLib.NetManager(clientListener);
        var received = new List<string>();
        LiteNetLib.NetPeer? serverSide = null;

        serverListener.ConnectionRequestEvent += request => request.Accept();
        serverListener.PeerConnectedEvent += peer => serverSide = peer;
        serverListener.NetworkReceiveEvent += (peer, reader, channel, method) =>
        {
            received.Add(System.Text.Encoding.UTF8.GetString(reader.GetRemainingBytes()));
            reader.Recycle();
        };

        try
        {
            Assert.True(server.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, 0));
            Assert.True(client.Start());
            LiteNetLib.NetPeer clientSide = client.Connect(new IPEndPoint(IPAddress.Loopback, server.LocalPort), ReadOnlySpan<byte>.Empty);

            Assert.True(PollUntil(server, client, () => serverSide != null && clientSide.ConnectionState == LiteNetLib.ConnectionState.Connected));
            serverSide!.SetInboundPacketLayer(new BasisPeerCipherLayer(key));

            clientSide.Send(System.Text.Encoding.UTF8.GetBytes("plaintext"), LiteNetLib.DeliveryMethod.Unreliable);
            PollUntil(server, client, () => received.Count > 0, 500);
            Assert.Empty(received);

            clientSide.SetOutboundPacketLayer(new BasisPeerCipherLayer(key));
            clientSide.Send(System.Text.Encoding.UTF8.GetBytes("sealed"), LiteNetLib.DeliveryMethod.ReliableOrdered);
            Assert.True(PollUntil(server, client, () => received.Count > 0));
            Assert.Equal(new[] { "sealed" }, received);
        }
        finally
        {
            client.Stop();
            server.Stop();
        }
    }

    [Fact]
    public void LiteNetLib_AcceptAwaitingSeal_HoldsBackDataSentBeforeTheKey()
    {
        byte[] key = ClientKey(Side.Create(), Side.Create());

        var serverListener = new LiteNetLib.EventBasedNetListener();
        var clientListener = new LiteNetLib.EventBasedNetListener();
        var server = new LiteNetLib.NetManager(serverListener);
        var client = new LiteNetLib.NetManager(clientListener);
        var received = new List<string>();
        BasisPeerCipherLayer? pending = null;
        LiteNetLib.NetPeer? serverSide = null;
        bool layerInPlaceWhenConnected = false;

        serverListener.ConnectionRequestEvent += request =>
        {
            pending = new BasisPeerCipherLayer();
            request.Accept(pending);
        };
        serverListener.PeerConnectedEvent += peer =>
        {
            serverSide = peer;
            layerInPlaceWhenConnected = ReferenceEquals(peer.InboundPacketLayer, pending);
        };
        serverListener.NetworkReceiveEvent += (peer, reader, channel, method) =>
        {
            received.Add(System.Text.Encoding.UTF8.GetString(reader.GetRemainingBytes()));
            reader.Recycle();
        };

        try
        {
            Assert.True(server.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, 0));
            Assert.True(client.Start());
            LiteNetLib.NetPeer clientSide = client.Connect(new IPEndPoint(IPAddress.Loopback, server.LocalPort), ReadOnlySpan<byte>.Empty);
            Assert.True(PollUntil(server, client, () => serverSide != null && clientSide.ConnectionState == LiteNetLib.ConnectionState.Connected));
            Assert.True(layerInPlaceWhenConnected);

            clientSide.Send(System.Text.Encoding.UTF8.GetBytes("early"), LiteNetLib.DeliveryMethod.Unreliable);
            PollUntil(server, client, () => received.Count > 0, 500);
            Assert.Empty(received);

            pending!.SetKey(key);
            clientSide.SetOutboundPacketLayer(new BasisPeerCipherLayer(key));
            clientSide.Send(System.Text.Encoding.UTF8.GetBytes("sealed"), LiteNetLib.DeliveryMethod.ReliableOrdered);
            Assert.True(PollUntil(server, client, () => received.Count > 0));
            Assert.Equal(new[] { "sealed" }, received);
            Assert.Equal(LiteNetLib.ConnectionState.Connected, serverSide!.ConnectionState);
        }
        finally
        {
            client.Stop();
            server.Stop();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LiteNetLib_OutboundPeerLayer_ReservesItsOverheadInTheMtu(bool keyed)
    {
        var listener = new LiteNetLib.EventBasedNetListener();
        var serverListener = new LiteNetLib.EventBasedNetListener();
        var server = new LiteNetLib.NetManager(serverListener);
        var client = new LiteNetLib.NetManager(listener);
        serverListener.ConnectionRequestEvent += request => request.Accept();
        try
        {
            Assert.True(server.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, 0));
            Assert.True(client.Start());
            LiteNetLib.NetPeer peer = client.Connect(new IPEndPoint(IPAddress.Loopback, server.LocalPort), ReadOnlySpan<byte>.Empty);
            int before = peer.Mtu;
            peer.SetOutboundPacketLayer(keyed ? new BasisPeerCipherLayer(new byte[BasisCryptoHandshake.KeySize]) : new BasisPeerCipherLayer());
            Assert.Equal(before - BasisCryptoLayer.Overhead, peer.Mtu);
        }
        finally
        {
            client.Stop();
            server.Stop();
        }
    }

    private sealed class MtuProbeRecorder : LiteNetLib.Layers.PacketLayerBase
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<int> Probes = new();

        public MtuProbeRecorder() : base(0)
        {
        }

        public override void ProcessInboundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int length)
        {
            if (length > 0 && (data[0] & 0x1F) == HeaderMtuCheck) Probes.Enqueue(length);
        }

        public override void ProcessOutBoundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int offset, ref int length)
        {
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LiteNetLib_MtuProbe_IsAsLargeOnTheWireAsTheDataItClears(bool keyed)
    {
        byte[] key = ClientKey(Side.Create(), Side.Create());
        var recorder = new MtuProbeRecorder();
        var serverListener = new LiteNetLib.EventBasedNetListener();
        var server = new LiteNetLib.NetManager(serverListener, recorder);
        var client = new LiteNetLib.NetManager(new LiteNetLib.EventBasedNetListener()) { MtuDiscovery = true };
        serverListener.ConnectionRequestEvent += request => request.Accept(new BasisPeerCipherLayer(key));
        try
        {
            Assert.True(server.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, 0));
            Assert.True(client.Start());
            LiteNetLib.NetPeer peer = client.Connect(new IPEndPoint(IPAddress.Loopback, server.LocalPort), ReadOnlySpan<byte>.Empty);
            peer.SetOutboundPacketLayer(keyed ? new BasisPeerCipherLayer(key) : new BasisPeerCipherLayer());
            int dataMtuBefore = peer.Mtu;

            Assert.True(PollUntil(server, client, () => !recorder.Probes.IsEmpty && peer.Mtu > dataMtuBefore, 6000));
            Assert.True(recorder.Probes.TryPeek(out int wire));
            Assert.Contains(wire, LiteNetLib.NetConstants.PossibleMtu);
            Assert.Equal(wire - BasisCryptoLayer.Overhead, peer.Mtu);
        }
        finally
        {
            client.Stop();
            server.Stop();
        }
    }

    private static bool PollUntil(LiteNetLib.NetManager server, LiteNetLib.NetManager client, Func<bool> done, int timeoutMs = 5000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            server.PollEvents();
            client.PollEvents();
            if (done()) return true;
            Thread.Sleep(10);
        }
        return done();
    }
}

/// <summary>
/// The same binding through the real admission path: HandleConnectionRequest with DID login on,
/// the challenge the server sends, the key it installs on the peer and the transcript it checks.
/// </summary>
[Collection("BasisServer shared network statics")]
public class DidLoginAdmissionTests
{
    private sealed class Login
    {
        public BasisDidLink.BasisDIDAuthIdentity Identity = null!;
        public FakeNetPeer Peer = null!;
        public RecordingConnectionRequest Request = null!;
        public byte[] ClientPrivate = null!;
        public byte[] ClientPublic = null!;
        public byte[] PayloadHash = null!;
        public PrivKey PlayerKey = null!;
        public Did PlayerDid = null!;
    }

    private static BasisDidLink.BasisDIDAuthIdentity UseDidLogin(int duplicates = 2)
    {
        NetworkServer.Configuration = new Configuration
        {
            PeerLimit = 100,
            UseAuth = false,
            UseAuthIdentity = true,
            HowManyDuplicateAuthCanExist = duplicates,
            BasisUserRestrictionMode = BasisUserRestrictionMode.Normal,
        };
        NetworkServer.Server = new FakeNetManager { ConnectedPeersCount = 0 };
        NetworkServer.Auth = new FakeAuth { Result = true };
        NetworkServer.AllowList = new BasisAllowList();
        NetworkServer.BanList = new BasisBanList();
        NetworkServer.HighQualityLength = ConvertToSize(BitQuality.High);
        var identity = new BasisDidLink.BasisDIDAuthIdentity();
        NetworkServer.AuthIdentity = identity;
        return identity;
    }

    private static (PrivKey Key, Did Did) NewPlayer()
    {
        byte[] privateBytes = new byte[Ed25519.PrivkeySize];
        CryptoRng.Fill(privateBytes);
        var key = new PrivKey(privateBytes);
        return (key, DidKeyResolver.EncodePubkeyAsDid(Ed25519.ConvertPrivkeyToPubkey(key)!));
    }

    private static Login Connect(bool canSeal = true, bool canAwaitSeal = true)
        => Connect(UseDidLogin(), NewPlayer(), canSeal, canAwaitSeal);

    private static Login Connect(BasisDidLink.BasisDIDAuthIdentity identity, (PrivKey Key, Did Did) player, bool canSeal = true, bool canAwaitSeal = true)
    {
        var login = new Login { Identity = identity, PlayerKey = player.Key, PlayerDid = player.Did };
        BasisCryptoHandshake.GenerateKeyPair(out login.ClientPrivate, out login.ClientPublic);

        login.Peer = LifecycleSupport.Peer(LifecycleSupport.NextPeerId());
        login.Peer.CanSeal = canSeal;
        ReadyMessage ready = LifecycleSupport.MakeReady(login.PlayerDid.V, "Connie");
        byte[] payload = DidLoginChannelBindingTests.ConnectPayload(login.ClientPublic, ready);
        login.PayloadHash = BasisDidLoginTranscript.HashConnectionPayload(payload, 0, payload.Length);
        byte[] data = LifecycleSupport.ConnectPayload(BasisNetworkVersion.ServerVersion, new byte[] { 1 }, ready, connectionKey: login.ClientPublic);
        login.Request = new RecordingConnectionRequest
        {
            Data = new NetDataReader(data),
            RemoteEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.9"), 6006),
            PeerToReturn = login.Peer,
            CanAwaitSeal = canAwaitSeal,
        };
        BasisServerHandle.BasisServerHandleEvents.HandleConnectionRequest(login.Request);
        Assert.True(WaitFor(() => login.Request.WasRejected || login.Peer.DisconnectCalls > 0 || login.Peer.HasSentOn(BasisNetworkCommons.AuthIdentityChannel)));
        return login;
    }

    private static bool WaitFor(Func<bool> done, int timeoutMs = 5000)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            if (watch.ElapsedMilliseconds > timeoutMs) return false;
            Thread.Sleep(5);
        }
        return true;
    }

    private static (byte[] Nonce, byte[] ServerPublic, string ServerId) ReadChallenge(FakeNetPeer peer)
    {
        byte[] data;
        lock (peer.Sent) data = Assert.Single(peer.Sent, s => s.Channel == BasisNetworkCommons.AuthIdentityChannel).Data;
        var reader = new NetDataReader(data);
        var bytes = new BytesMessage();
        Assert.True(bytes.Deserialize(reader, out byte[] nonce));
        Assert.True(bytes.Deserialize(reader, out byte[] serverPublic));
        Assert.True(bytes.Deserialize(reader, out byte[] serverId));
        return (nonce, serverPublic, System.Text.Encoding.UTF8.GetString(serverId));
    }

    private static byte[] GenuineTranscript(Login login)
    {
        var (nonce, serverPublic, serverId) = ReadChallenge(login.Peer);
        return BasisDidLoginTranscript.Build(login.PlayerDid.V, serverId, nonce, login.ClientPublic, serverPublic, login.PayloadHash);
    }

    private static Task<bool> Answer(Login login, byte[] transcript)
    {
        Assert.True(Ed25519.Sign(login.PlayerKey, new Payload(transcript), out Signature? sig));
        var entry = login.Identity.AuthIdentity[login.Peer.Id];
        return login.Identity.RecvChallengeResponse(new Response(sig!, new DidUrlFragment(string.Empty)), entry.Challenge, entry.Transcript);
    }

    private static void Respond(Login login, byte[] transcript)
    {
        Assert.True(Ed25519.Sign(login.PlayerKey, new Payload(transcript), out Signature? sig));
        var writer = new NetDataWriter();
        new BytesMessage().Serialize(writer, sig!.V);
        new BytesMessage().Serialize(writer, System.Text.Encoding.UTF8.GetBytes("N/A"));
        BasisServerHandle.BasisServerHandleEvents.HandleAuth(NetPacketReader.Create(writer.CopyData(), 0, writer.Length, static () => { }), login.Peer);
    }

    private static bool Admitted(Login login) => login.Identity.NetIDToUUID(login.Peer, out _);

    private static string RefusalReason(FakeNetPeer peer)
    {
        lock (peer.DisconnectData) return LifecycleSupport.RejectReason(Assert.Single(peer.DisconnectData));
    }

    private static void Release(Login login)
    {
        login.Identity.RemoveConnection(login.Peer.Id);
        login.Identity.DeInitialize();
    }

    [Fact]
    public async Task Challenge_CarriesTheServerKeyAndId_AndTheGenuineLoginVerifies()
    {
        using var scope = new ServerStaticsScope();
        Login login = Connect();
        try
        {
            Assert.True(login.Request.AcceptedAwaitingSeal);
            var (nonce, serverPublic, serverId) = ReadChallenge(login.Peer);
            Assert.True(nonce.Length >= BasisDidLoginTranscript.MinNonceSize);
            Assert.Equal(NetworkServer.Configuration.ServerUUID, serverId);
            Assert.True(Guid.TryParseExact(serverId, "N", out _));

            // The server only takes data sealed with the key the client derives on its side.
            Assert.True(BasisCryptoHandshake.DeriveClientKey(login.ClientPrivate, login.ClientPublic, serverPublic, out byte[] clientKey));
            Assert.Equal(clientKey, login.Peer.InboundKey);

            Assert.True(await Answer(login, GenuineTranscript(login)));
        }
        finally
        {
            Release(login);
        }
    }

    [Fact]
    public void GenuineLogin_IsAdmittedOnlyOnceItsSignatureChecksOut()
    {
        using var scope = new ServerStaticsScope();
        Login login = Connect();
        try
        {
            Assert.False(Admitted(login));
            Assert.False(login.Identity.UUIDToNetID(login.PlayerDid.V, out _));
            Assert.Equal(0, login.Identity.CheckForDuplicates(login.PlayerDid));
            Assert.False(NetworkServer.AuthenticatedPeers.ContainsKey(login.Peer.Id));

            Respond(login, GenuineTranscript(login));
            Assert.True(WaitFor(() => NetworkServer.AuthenticatedPeers.ContainsKey(login.Peer.Id) || login.Peer.DisconnectCalls > 0));

            Assert.True(login.Identity.NetIDToUUID(login.Peer, out string uuid));
            Assert.Equal(login.PlayerDid.V, uuid);
            Assert.Equal(1, login.Identity.CheckForDuplicates(login.PlayerDid));
            Assert.Equal(0, login.Peer.DisconnectCalls);
        }
        finally
        {
            Release(login);
        }
    }

    [Fact]
    public async Task RelayedLogin_SignedForAnotherServerConnectionOrPayload_IsRefused()
    {
        using var scope = new ServerStaticsScope();
        Login login = Connect();
        try
        {
            var (nonce, serverPublic, serverId) = ReadChallenge(login.Peer);
            BasisCryptoHandshake.GenerateKeyPair(out _, out byte[] relayPublic);
            byte[] relayPayload = DidLoginChannelBindingTests.ConnectPayload(login.ClientPublic, LifecycleSupport.MakeReady(login.PlayerDid.V, "Somebody Else"));
            byte[] relayHash = BasisDidLoginTranscript.HashConnectionPayload(relayPayload, 0, relayPayload.Length);

            // The player signed this server's nonce, but for the relay's connection, name or payload.
            Assert.False(await Answer(login, BasisDidLoginTranscript.Build(login.PlayerDid.V, "relay-server-uuid", nonce, login.ClientPublic, serverPublic, login.PayloadHash)));
            Assert.False(await Answer(login, BasisDidLoginTranscript.Build(login.PlayerDid.V, serverId, nonce, login.ClientPublic, relayPublic, login.PayloadHash)));
            Assert.False(await Answer(login, BasisDidLoginTranscript.Build(login.PlayerDid.V, serverId, nonce, relayPublic, serverPublic, login.PayloadHash)));
            Assert.False(await Answer(login, BasisDidLoginTranscript.Build(login.PlayerDid.V, serverId, nonce, login.ClientPublic, serverPublic, relayHash)));
        }
        finally
        {
            Release(login);
        }
    }

    [Fact]
    public void ForgedLogin_IsRefusedAndNeverCounted()
    {
        using var scope = new ServerStaticsScope();
        Login login = Connect();
        try
        {
            var (nonce, serverPublic, _) = ReadChallenge(login.Peer);
            Respond(login, BasisDidLoginTranscript.Build(login.PlayerDid.V, "relay-server-uuid", nonce, login.ClientPublic, serverPublic, login.PayloadHash));
            Assert.True(WaitFor(() => login.Peer.DisconnectCalls > 0));

            Assert.False(Admitted(login));
            Assert.False(NetworkServer.AuthenticatedPeers.ContainsKey(login.Peer.Id));
            Assert.Equal(0, login.Identity.CheckForDuplicates(login.PlayerDid));
        }
        finally
        {
            Release(login);
        }
    }

    [Fact]
    public void DuplicateLimit_CountsVerifiedLoginsOnly()
    {
        using var scope = new ServerStaticsScope();
        BasisDidLink.BasisDIDAuthIdentity identity = UseDidLogin(duplicates: 1);
        var player = NewPlayer();
        Login first = Connect(identity, player);
        Login second = Connect(identity, player);
        try
        {
            Assert.True(second.Peer.HasSentOn(BasisNetworkCommons.AuthIdentityChannel));

            Respond(first, GenuineTranscript(first));
            Assert.True(WaitFor(() => Admitted(first)));
            Respond(second, GenuineTranscript(second));
            Assert.True(WaitFor(() => second.Peer.DisconnectCalls > 0));

            Assert.Equal("To Many Auths From this DID!", RefusalReason(second.Peer));
            Assert.False(Admitted(second));
            Assert.True(Admitted(first));
            Assert.Equal(1, identity.CheckForDuplicates(player.Did));
        }
        finally
        {
            identity.RemoveConnection(second.Peer.Id);
            Release(first);
        }
    }

    [Fact]
    public void TransportThatCannotHoldBackUnsealedData_IsRefusedBeforeAccepting()
    {
        using var scope = new ServerStaticsScope();
        Login login = Connect(canAwaitSeal: false);
        try
        {
            Assert.True(login.Request.WasRejected);
            Assert.False(login.Request.WasAccepted);
            Assert.Equal("Transport cannot protect the login.", LifecycleSupport.RejectReason(login.Request.RejectPayload));
            Assert.False(login.Identity.AuthIdentity.ContainsKey(login.Peer.Id));
        }
        finally
        {
            Release(login);
        }
    }

    [Fact]
    public void TransportThatCannotSeal_RefusesTheLoginBeforeSendingAChallenge()
    {
        using var scope = new ServerStaticsScope();
        Login login = Connect(canSeal: false);
        try
        {
            Assert.True(WaitFor(() => login.Peer.DisconnectCalls > 0));
            Assert.False(login.Peer.HasSentOn(BasisNetworkCommons.AuthIdentityChannel));
            Assert.False(login.Identity.AuthIdentity.ContainsKey(login.Peer.Id));
            Assert.Equal("Transport cannot protect the login.", RefusalReason(login.Peer));
        }
        finally
        {
            Release(login);
        }
    }

    [Fact]
    public void MissingConnectionKey_IsRefused()
    {
        using var scope = new ServerStaticsScope();
        BasisDidLink.BasisDIDAuthIdentity identity = UseDidLogin();
        try
        {
            FakeNetPeer peer = LifecycleSupport.Peer(LifecycleSupport.NextPeerId());
            var writer = new NetDataWriter();
            writer.Put(BasisNetworkVersion.ServerVersion);
            BasisNetworkApplication.Write(writer, BasisNetworkApplication.DefaultCompanyName, BasisNetworkApplication.DefaultProductName);
            new BytesMessage().Serialize(writer, new byte[] { 1 });
            // A pre-56 client goes straight from the password to the ReadyMessage.
            LifecycleSupport.MakeReady(LifecycleSupport.NewUuid(), "Connie").Serialize(writer);
            BasisServerHandle.BasisServerHandleEvents.HandleConnectionRequest(LifecycleSupport.Request(writer.CopyData(), accepted: peer));

            Assert.False(peer.HasSentOn(BasisNetworkCommons.AuthIdentityChannel));
            Assert.Null(peer.InboundKey);
            Assert.Equal("Invalid connection key.", RefusalReason(peer));
        }
        finally
        {
            identity.DeInitialize();
        }
    }

    [Fact]
    public void TransportVerifiedIdentity_IsAdmittedWithoutAChallengeUnderThatIdentity()
    {
        using var scope = new ServerStaticsScope();
        BasisDidLink.BasisDIDAuthIdentity identity = UseDidLogin();
        const string verified = "steam:76561197960287930";
        FakeNetPeer peer = LifecycleSupport.Peer(LifecycleSupport.NextPeerId());
        try
        {
            byte[] data = LifecycleSupport.ConnectPayload(BasisNetworkVersion.ServerVersion, new byte[] { 1 }, LifecycleSupport.MakeReady(NewPlayer().Did.V, "Connie"));
            var request = new RecordingConnectionRequest
            {
                Data = new NetDataReader(data),
                RemoteEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.9"), 6006),
                PeerToReturn = peer,
                VerifiedIdentity = verified,
            };
            BasisServerHandle.BasisServerHandleEvents.HandleConnectionRequest(request);

            Assert.True(request.WasAccepted);
            Assert.False(request.AcceptedAwaitingSeal);
            Assert.False(peer.HasSentOn(BasisNetworkCommons.AuthIdentityChannel));
            Assert.Null(peer.InboundKey);
            Assert.True(NetworkServer.AuthenticatedPeers.ContainsKey(peer.Id));
            Assert.True(identity.NetIDToUUID(peer, out string uuid));
            Assert.Equal(verified, uuid);
            Assert.True(identity.UUIDToNetID(verified, out int id));
            Assert.Equal(peer.Id, id);
        }
        finally
        {
            identity.RemoveConnection(peer.Id);
            identity.DeInitialize();
        }
    }

    [Fact]
    public void TransportVerifiedIdentity_WithDidLoginOff_StillNamesThePlayer()
    {
        using var scope = new ServerStaticsScope();
        NetworkServer.Configuration = new Configuration
        {
            PeerLimit = 100,
            UseAuth = false,
            UseAuthIdentity = false,
            BasisUserRestrictionMode = BasisUserRestrictionMode.Normal,
        };
        NetworkServer.Server = new FakeNetManager { ConnectedPeersCount = 0 };
        NetworkServer.Auth = new FakeAuth { Result = true };
        NetworkServer.AuthIdentity = new MapAuthIdentity();
        NetworkServer.AllowList = new BasisAllowList();
        NetworkServer.BanList = new BasisBanList();
        NetworkServer.HighQualityLength = ConvertToSize(BitQuality.High);
        const string verified = "steam:76561197960287931";
        FakeNetPeer peer = LifecycleSupport.Peer(LifecycleSupport.NextPeerId());
        byte[] data = LifecycleSupport.ConnectPayload(BasisNetworkVersion.ServerVersion, new byte[] { 1 }, LifecycleSupport.MakeReady(LifecycleSupport.NewUuid(), "Connie"));
        var request = new RecordingConnectionRequest
        {
            Data = new NetDataReader(data),
            RemoteEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.9"), 6006),
            PeerToReturn = peer,
            VerifiedIdentity = verified,
        };

        BasisServerHandle.BasisServerHandleEvents.HandleConnectionRequest(request);

        Assert.True(NetworkServer.AuthenticatedPeers.ContainsKey(peer.Id));
        Assert.True(BasisSavedState.GetLastPlayerMetaData(peer, out ClientMetaDataMessage meta));
        Assert.Equal(verified, meta.playerUUID);
    }
}
