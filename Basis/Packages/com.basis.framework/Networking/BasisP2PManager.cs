using Basis.BasisUI;
using Basis.Network.Core;
using Basis.Scripts.Profiler;
using System;
using System.Collections.Concurrent;
using System.Net;
using static SerializableBasis;
using LiteNetManager = LiteNetLib.NetManager;
using LiteNetDataWriter = LiteNetLib.Utils.NetDataWriter;
using LiteNatPunchListener = LiteNetLib.EventBasedNatPunchListener;
using LiteNatAddressType = LiteNetLib.NatAddressType;

namespace Basis.Scripts.Networking
{
    public static class BasisP2PManager
    {
        public enum P2PSessionState : byte
        {
            Idle,
            OutgoingRequested,
            OutgoingArmed,
            IncomingPending,
            Punching,
            Connected,
            Reconnecting,
            Failed,
        }

        private const int MaxPunchAttempts = 5;

        public const string P2PRate_144Hz = "144 Hz";
        public const string P2PRate_120Hz = "120 Hz";
        public const string P2PRate_90Hz  = "90 Hz";
        public const string P2PRate_72Hz  = "72 Hz";
        public const string P2PRate_60Hz  = "60 Hz";
        public const string P2PRate_30Hz  = "30 Hz";
        public const string P2PRate_20Hz  = "20 Hz";

        public static float FastAvatarIntervalSeconds
        {
            get
            {
                string choice = BasisSettingsDefaults.P2PAvatarSyncRate?.RawValue;
                switch (choice)
                {
                    case P2PRate_144Hz: return 1f / 144f;
                    case P2PRate_120Hz: return 1f / 120f;
                    case P2PRate_90Hz:  return 1f / 90f;
                    case P2PRate_72Hz:  return 1f / 72f;
                    case P2PRate_60Hz:  return 1f / 60f;
                    case P2PRate_30Hz:  return 1f / 30f;
                    case P2PRate_20Hz:  return 1f / 20f;
                    default:            return 1f / 60f;
                }
            }
        }

        private sealed class Session
        {
            public string Token;
            public ushort OtherPlayerId;
            public bool LocalIsInitiator;
            public P2PSessionState State;
            public NetPeer P2PPeer;
            public IPAddress ExpectedRemoteAddress;
            public int PunchAttempts;
            public LiteNatAddressType ConnectionType;
            public bool ConnectIssued;
        }

        private static readonly ConcurrentDictionary<string, Session> _sessionsByToken = new();
        private static readonly ConcurrentDictionary<ushort, Session> _sessionsByOtherId = new();

        private static LiteNetManager _p2pManager;
        private static EventBasedNetListener _p2pListener;
        private static LiteNatPunchListener _natListener;
        private static readonly object _initLock = new object();

        public static string ServerHost { get; set; }
        public static int ServerPort { get; set; }

        public static event Action<ushort, string> OnIncomingRequest;
        public static event Action<ushort, P2PSessionState> OnSessionStateChanged;

        public static void StampServerEndpoint(string host, int port)
        {
            ServerHost = host;
            ServerPort = port;
            BasisNetworkProfiler.ConnectedSessionsProvider = GetConnectedSessionCount;
        }

        public static int GetConnectedSessionCount()
        {
            int count = 0;
            foreach (var s in _sessionsByOtherId.Values)
            {
                if (s.State == P2PSessionState.Connected) count++;
            }
            return count;
        }

        public static void Shutdown()
        {
            foreach (var s in _sessionsByOtherId.Values)
            {
                NotifyStateChanged(s.OtherPlayerId, P2PSessionState.Idle);
            }
            _sessionsByOtherId.Clear();
            _sessionsByToken.Clear();

            lock (_initLock)
            {
                if (_p2pManager != null)
                {
                    try { _p2pManager.Stop(); }
                    catch (Exception ex) { BasisDebug.LogError($"[P2P] Stop failed: {ex.Message}"); }
                    _p2pManager = null;
                    _p2pListener = null;
                    _natListener = null;
                }
            }
        }

        public static string SendRequest(ushort targetPlayerId)
        {
            if (BasisNetworkConnection.LocalPlayerPeer == null)
            {
                BasisDebug.LogError("[P2P] Not connected to server.");
                return null;
            }
            if (!BasisNetworkConnection.TryGetLocalPlayerID(out ushort localId) || localId == targetPlayerId)
            {
                BasisDebug.LogError("[P2P] Cannot request a session with yourself.");
                return null;
            }
            if (_sessionsByOtherId.ContainsKey(targetPlayerId))
            {
                BasisDebug.LogWarning($"[P2P] Already have a session for player {targetPlayerId}.");
                return null;
            }

            string token = Guid.NewGuid().ToString("N");
            var session = new Session
            {
                Token = token,
                OtherPlayerId = targetPlayerId,
                LocalIsInitiator = true,
                State = P2PSessionState.OutgoingRequested,
            };
            _sessionsByToken[token] = session;
            _sessionsByOtherId[targetPlayerId] = session;

            BasisDebug.Log($"[P2P] SendRequest → player {targetPlayerId} (token {Preview(token)}).");
            SendSubToServer(BasisNetworkCommons.P2PSub_Request, targetPlayerId, token);
            NotifyStateChanged(targetPlayerId, session.State);
            return token;
        }

        private static string Preview(string token)
        {
            if (string.IsNullOrEmpty(token)) return "(empty)";
            return token.Length <= 8 ? token : token.Substring(0, 8);
        }

        public static void CancelSession(ushort otherPlayerId)
        {
            if (!_sessionsByOtherId.TryGetValue(otherPlayerId, out Session s)) return;

            BasisDebug.Log($"[P2P] CancelSession with player {otherPlayerId} (state was {s.State}, token {Preview(s.Token)}).");
            SendSubToServer(BasisNetworkCommons.P2PSub_Cancel, otherPlayerId, s.Token);
            DropSession(s, P2PSessionState.Idle);
        }

        public static void AcceptIncoming(ushort senderPlayerId, string token)
        {
            if (!_sessionsByToken.TryGetValue(token, out Session s))
            {
                BasisDebug.LogError($"[P2P] AcceptIncoming for unknown token {Preview(token)}.");
                return;
            }
            if (s.State != P2PSessionState.IncomingPending)
            {
                BasisDebug.LogWarning($"[P2P] AcceptIncoming in unexpected state {s.State}.");
                return;
            }
            BasisDebug.Log($"[P2P] AcceptIncoming → player {senderPlayerId} (token {Preview(token)}); sending Accept and starting NAT punch.");
            SendSubToServer(BasisNetworkCommons.P2PSub_Accept, senderPlayerId, token);
            StartPunch(s);
        }

        public static void DeclineIncoming(ushort senderPlayerId, string token)
        {
            BasisDebug.Log($"[P2P] DeclineIncoming → player {senderPlayerId} (token {Preview(token)}).");
            SendSubToServer(BasisNetworkCommons.P2PSub_Decline, senderPlayerId, token);
            if (_sessionsByToken.TryGetValue(token, out Session s))
            {
                DropSession(s, P2PSessionState.Idle);
            }
        }

        public static P2PSessionState GetSessionState(ushort otherPlayerId)
        {
            return _sessionsByOtherId.TryGetValue(otherPlayerId, out Session s) ? s.State : P2PSessionState.Idle;
        }

        public static bool TryGetP2PPeer(ushort otherPlayerId, out NetPeer peer)
        {
            peer = null;
            if (!_sessionsByOtherId.TryGetValue(otherPlayerId, out Session s)) return false;
            if (s.State != P2PSessionState.Connected) return false;
            peer = s.P2PPeer;
            return peer != null;
        }

        public static bool IsP2PSessionLocal(ushort otherPlayerId)
        {
            if (!_sessionsByOtherId.TryGetValue(otherPlayerId, out Session s)) return false;
            if (s.State != P2PSessionState.Connected) return false;
            return s.ConnectionType == LiteNatAddressType.Internal;
        }

        public static bool HasAnyConnectedSession()
        {
            foreach (var s in _sessionsByOtherId.Values)
            {
                if (s.State == P2PSessionState.Connected) return true;
            }
            return false;
        }

        [ThreadStatic] private static NetDataWriter _p2pVoiceWriter;
        [ThreadStatic] private static NetDataWriter _p2pAvatarWriter;

        private static int SendToAllConnected(NetDataWriter writer, byte channel, DeliveryMethod deliveryMethod)
        {
            if (writer == null || writer.Length == 0) return 0;
            int sent = 0;
            foreach (var s in _sessionsByOtherId.Values)
            {
                if (s.State != P2PSessionState.Connected) continue;
                var peer = s.P2PPeer;
                if (peer == null) continue;
                try
                {
                    peer.Send(writer, channel, deliveryMethod);
                    sent++;
                }
                catch (Exception ex)
                {
                    BasisDebug.LogError($"[P2P] Send on channel {channel} to player {s.OtherPlayerId} failed: {ex.Message}");
                }
            }
            return sent;
        }

        public static void BroadcastVoiceViaP2P(NetDataWriter clientFormatWriter)
        {
            if (clientFormatWriter == null || clientFormatWriter.Length == 0) return;
            if (!HasAnyConnectedSession()) return;
            if (!BasisNetworkConnection.TryGetLocalPlayerID(out ushort localId)) return;

            bool largeId = localId > byte.MaxValue;
            byte channel = largeId ? BasisNetworkCommons.VoiceLargeChannel : BasisNetworkCommons.VoiceChannel;

            var w = _p2pVoiceWriter ??= new NetDataWriter();
            w.Reset();
            if (largeId) w.Put(localId);
            else w.Put((byte)localId);
            w.Put(clientFormatWriter.Data, 0, clientFormatWriter.Length);

            SendToAllConnected(w, channel, DeliveryMethod.Unreliable);
        }

        public static void BroadcastAvatarViaP2P(NetDataWriter clientFormatWriter, byte smallChannel)
        {
            if (clientFormatWriter == null || clientFormatWriter.Length == 0) return;
            if (!HasAnyConnectedSession()) return;
            if (!BasisNetworkConnection.TryGetLocalPlayerID(out ushort localId)) return;

            bool largeId = localId > byte.MaxValue;
            byte channel = largeId
                ? (byte)(BasisNetworkCommons.PlayerAvatarVeryLowLargeChannel
                    + (smallChannel - BasisNetworkCommons.PlayerAvatarVeryLowChannel))
                : smallChannel;

            var w = _p2pAvatarWriter ??= new NetDataWriter();
            w.Reset();
            if (largeId) w.Put(localId);
            else w.Put((byte)localId);
            w.Put((byte)0);
            w.Put(clientFormatWriter.Data, 0, clientFormatWriter.Length);

            int sent = SendToAllConnected(w, channel, DeliveryMethod.Unreliable);
            if (sent > 0)
            {
                BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.OutboundAvatarP2P, (long)w.Length * sent);
            }
        }

        public static void StripP2PConnectedFromRecipients(System.Collections.Generic.List<ushort> recipients)
        {
            if (recipients == null || recipients.Count == 0) return;
            if (!HasAnyConnectedSession()) return;
            for (int i = recipients.Count - 1; i >= 0; i--)
            {
                if (_sessionsByOtherId.TryGetValue(recipients[i], out Session s) &&
                    s.State == P2PSessionState.Connected)
                {
                    recipients.RemoveAt(i);
                }
            }
        }

        public static void AddP2PConnectedToExcluded(System.Collections.Generic.List<ushort> excluded)
        {
            if (excluded == null) return;
            if (!HasAnyConnectedSession()) return;
            foreach (var s in _sessionsByOtherId.Values)
            {
                if (s.State != P2PSessionState.Connected) continue;
                if (!excluded.Contains(s.OtherPlayerId))
                {
                    excluded.Add(s.OtherPlayerId);
                }
            }
        }

        public static void HandleServerMessage(NetPacketReader reader)
        {
            byte sub = reader.GetByte();
            BasisP2PSignalMessage msg = default;
            msg.Deserialize(reader);
            reader.Recycle();

            switch (sub)
            {
                case BasisNetworkCommons.P2PSub_Request:
                    OnInboundRequest(msg);
                    break;
                case BasisNetworkCommons.P2PSub_Accept:
                    OnInboundAccept(msg);
                    break;
                case BasisNetworkCommons.P2PSub_Decline:
                case BasisNetworkCommons.P2PSub_Cancel:
                    if (_sessionsByToken.TryGetValue(msg.sessionToken, out Session s1))
                    {
                        BasisDebug.Log($"[P2P] Remote sent {(sub == BasisNetworkCommons.P2PSub_Decline ? "Decline" : "Cancel")} for player {s1.OtherPlayerId}; tearing down session (token {Preview(msg.sessionToken)}).");
                        DropSession(s1, P2PSessionState.Idle);
                    }
                    break;
                case BasisNetworkCommons.P2PSub_LinkLost:
                    if (_sessionsByToken.TryGetValue(msg.sessionToken, out Session s2))
                    {
                        BasisDebug.Log($"[P2P] Remote reported LinkLost for player {s2.OtherPlayerId} (token {Preview(msg.sessionToken)}); scheduling reconnect.");
                        ScheduleReconnect(s2);
                    }
                    break;
                case BasisNetworkCommons.P2PSub_ServerArmed:
                    if (_sessionsByToken.TryGetValue(msg.sessionToken, out Session s3) &&
                        s3.State == P2PSessionState.OutgoingRequested)
                    {
                        BasisDebug.Log($"[P2P] Server armed our request for player {s3.OtherPlayerId} (token {Preview(msg.sessionToken)}); waiting on Accept.");
                        s3.State = P2PSessionState.OutgoingArmed;
                        NotifyStateChanged(s3.OtherPlayerId, s3.State);
                    }
                    break;
            }
        }

        private static void OnInboundRequest(BasisP2PSignalMessage msg)
        {
            BasisDebug.Log($"[P2P] Server forwarded a direct-connection Request from player {msg.otherPlayerId} (token {msg.sessionToken}).");
            if (_sessionsByOtherId.ContainsKey(msg.otherPlayerId))
            {
                BasisDebug.LogWarning($"[P2P] Auto-declining: already have a session with player {msg.otherPlayerId}.");
                SendSubToServer(BasisNetworkCommons.P2PSub_Decline, msg.otherPlayerId, msg.sessionToken);
                return;
            }

            var session = new Session
            {
                Token = msg.sessionToken,
                OtherPlayerId = msg.otherPlayerId,
                LocalIsInitiator = false,
                State = P2PSessionState.IncomingPending,
            };
            _sessionsByToken[msg.sessionToken] = session;
            _sessionsByOtherId[msg.otherPlayerId] = session;
            NotifyStateChanged(msg.otherPlayerId, session.State);

            OnIncomingRequest?.Invoke(msg.otherPlayerId, msg.sessionToken);
        }

        private static void OnInboundAccept(BasisP2PSignalMessage msg)
        {
            if (!_sessionsByToken.TryGetValue(msg.sessionToken, out Session s))
            {
                BasisDebug.LogWarning($"[P2P] Accept for unknown token {Preview(msg.sessionToken)} — dropping.");
                return;
            }
            if (s.State != P2PSessionState.OutgoingArmed && s.State != P2PSessionState.OutgoingRequested)
            {
                BasisDebug.LogWarning($"[P2P] Accept arrived in unexpected state {s.State} for player {s.OtherPlayerId}; ignoring.");
                return;
            }
            BasisDebug.Log($"[P2P] Player {s.OtherPlayerId} accepted our request (token {Preview(msg.sessionToken)}); starting NAT punch.");
            StartPunch(s);
        }

        private static void StartPunch(Session s)
        {
            EnsureP2PNetManager();
            s.State = P2PSessionState.Punching;
            s.PunchAttempts++;
            NotifyStateChanged(s.OtherPlayerId, s.State);

            if (_p2pManager == null || string.IsNullOrEmpty(ServerHost) || ServerPort == 0)
            {
                BasisDebug.LogError("[P2P] P2P NetManager or server endpoint missing — cannot punch.");
                DropSession(s, P2PSessionState.Failed);
                return;
            }

            BasisDebug.Log($"[P2P] StartPunch token={Preview(s.Token)} player={s.OtherPlayerId} attempt={s.PunchAttempts}/{MaxPunchAttempts} initiator={s.LocalIsInitiator} → sending NatIntroduceRequest to {ServerHost}:{ServerPort} from P2P port {_p2pManager.LocalPort}.");
            try
            {
                _p2pManager.NatPunchModule.SendNatIntroduceRequest(ServerHost, ServerPort, s.Token);
            }
            catch (Exception ex)
            {
                BasisDebug.LogError($"[P2P] SendNatIntroduceRequest failed: {ex.Message}");
                DropSession(s, P2PSessionState.Failed);
            }
        }

        private static void EnsureP2PNetManager()
        {
            lock (_initLock)
            {
                if (_p2pManager != null) return;

                _p2pListener = new EventBasedNetListener();
                _p2pListener.ConnectionRequestEvent += OnP2PConnectionRequest;
                _p2pListener.PeerConnectedEvent += OnP2PPeerConnected;
                _p2pListener.PeerDisconnectedEvent += OnP2PPeerDisconnected;
                _p2pListener.NetworkReceiveEvent += OnP2PNetworkReceive;

                _natListener = new LiteNatPunchListener();
                _natListener.NatIntroductionSuccess += OnNatIntroductionSuccess;

                _p2pManager = new LiteNetManager(_p2pListener)
                {
                    NatPunchEnabled = true,
                    UnsyncedEvents = true,
                    AutoRecycle = false,
                    ChannelsCount = BasisNetworkCommons.TotalChannels,
                    UpdateTime = BasisNetworkCommons.NetworkIntervalPoll,
                };
                _p2pManager.NatPunchModule.Init(_natListener);
                _p2pManager.NatPunchModule.UnsyncedEvents = true;

                if (!_p2pManager.Start())
                {
                    BasisDebug.LogError("[P2P] Failed to start P2P NetManager.");
                    _p2pManager = null;
                    _p2pListener = null;
                    _natListener = null;
                    return;
                }

                BasisDebug.Log($"[P2P] P2P NetManager listening on port {_p2pManager.LocalPort}.");
            }
        }

        private static void OnNatIntroductionSuccess(IPEndPoint targetEndPoint, LiteNatAddressType type, string token)
        {
            if (!_sessionsByToken.TryGetValue(token, out Session s))
            {
                BasisDebug.LogWarning($"[P2P] NatIntroductionSuccess for unknown token {Preview(token)} from {targetEndPoint}.");
                return;
            }
            if (s.State == P2PSessionState.Connected)
            {
                return;
            }

            // First success wins — Internal arrives before External on same-LAN pairs.
            bool firstSuccess = s.ExpectedRemoteAddress == null;
            if (firstSuccess)
            {
                s.ExpectedRemoteAddress = targetEndPoint.Address;
                s.ConnectionType = type;
                BasisDebug.Log($"[P2P] NatIntroductionSuccess: player {s.OtherPlayerId} reachable at {targetEndPoint} ({type}{(type == LiteNatAddressType.Internal ? " — same LAN" : "")}). Local role: {(s.LocalIsInitiator ? "initiator (will Connect)" : "target (will accept inbound)")}.");
            }

            // Only initiator connects, only once — target waits for inbound to avoid simultaneous-open races.
            if (!s.LocalIsInitiator) return;
            if (s.ConnectIssued) return;
            s.ConnectIssued = true;

            try
            {
                var connectData = new LiteNetDataWriter();
                connectData.Put(token);
                _p2pManager.Connect(targetEndPoint, connectData);
            }
            catch (Exception ex)
            {
                BasisDebug.LogError($"[P2P] Connect to {targetEndPoint} failed: {ex.Message}");
                s.ConnectIssued = false;
            }
        }

        private static void OnP2PConnectionRequest(ConnectionRequest request)
        {
            try
            {
                string token = request.Data.GetString(BasisP2PSignalMessage.MaxTokenLength);
                if (_sessionsByToken.TryGetValue(token, out Session s) &&
                    s.State == P2PSessionState.Punching)
                {
                    BasisDebug.Log($"[P2P] Inbound P2P connect for token {Preview(token)} (player {s.OtherPlayerId}) — accepting.");
                    s.P2PPeer = request.Accept();
                }
                else
                {
                    BasisDebug.LogWarning($"[P2P] Rejecting inbound P2P connect from {request.RemoteEndPoint} — unknown or wrong-state token {Preview(token)}.");
                    request.Reject(new NetDataWriter());
                }
            }
            catch (Exception ex)
            {
                BasisDebug.LogError($"[P2P] Bad connect data: {ex.Message}");
                request.Reject(new NetDataWriter());
            }
        }

        private static void OnP2PPeerConnected(NetPeer peer)
        {
            // Inbound match: P2PPeer already set in OnP2PConnectionRequest, fresh wrapper here equals
            // by underlying LiteNetLib peer. Outbound match: compare against ExpectedRemoteAddress.
            Session matched = null;
            foreach (var s in _sessionsByToken.Values)
            {
                if (s.P2PPeer != null && s.P2PPeer.Equals(peer))
                {
                    s.P2PPeer = peer;
                    matched = s;
                    break;
                }
                if (s.P2PPeer == null &&
                    s.State == P2PSessionState.Punching &&
                    s.ExpectedRemoteAddress != null &&
                    peer.Address != null &&
                    peer.Address.Equals(s.ExpectedRemoteAddress))
                {
                    s.P2PPeer = peer;
                    matched = s;
                    break;
                }
            }

            if (matched == null)
            {
                BasisDebug.LogWarning($"[P2P] PeerConnected from {peer.Address} did not match any pending session.");
                return;
            }

            matched.State = P2PSessionState.Connected;
            matched.PunchAttempts = 0;
            NotifyStateChanged(matched.OtherPlayerId, matched.State);
            BasisDebug.Log($"[P2P] CONNECTED to player {matched.OtherPlayerId} at {peer.Address} via {(matched.ConnectionType == LiteNatAddressType.Internal ? "LAN" : "Internet")} (token {Preview(matched.Token)}); sending LinkUp to server.");

            SendSubToServer(BasisNetworkCommons.P2PSub_LinkUp, matched.OtherPlayerId, matched.Token);
            BasisAvatarRateRegistry.ForceNextAnnouncement();
        }

        private static void OnP2PPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            foreach (var s in _sessionsByToken.Values)
            {
                if (s.P2PPeer == null || !s.P2PPeer.Equals(peer)) continue;
                BasisDebug.Log($"[P2P] P2P peer for player {s.OtherPlayerId} disconnected: {info.Reason}");
                SendSubToServer(BasisNetworkCommons.P2PSub_LinkLost, s.OtherPlayerId, s.Token);
                ScheduleReconnect(s);
                return;
            }
        }

        private static void OnP2PNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            if (!IsAllowedP2PChannel(channel))
            {
                BasisDebug.LogWarning($"[P2P] Dropping packet on unexpected channel {channel} from P2P peer.");
                reader.Recycle();
                return;
            }

            // Spoof guard — embedded player id must match the session this socket belongs to.
            if (!TryFindSessionByPeer(peer, out ushort expectedOtherId))
            {
                BasisDebug.LogWarning("[P2P] Packet from a P2P peer not bound to any session.");
                reader.Recycle();
                return;
            }

            bool largeId = BasisNetworkCommons.IsLargePlayerIdChannel(channel);
            int origPos = reader.Position;
            if (reader.AvailableBytes < (largeId ? 2 : 1))
            {
                BasisDebug.LogWarning($"[P2P] Truncated packet on channel {channel} from player {expectedOtherId}.");
                reader.Recycle();
                return;
            }
            ushort embeddedId = largeId ? reader.GetUShort() : (ushort)reader.GetByte();
            if (embeddedId != expectedOtherId)
            {
                BasisDebug.LogWarning($"[P2P] Embedded player id {embeddedId} doesn't match session {expectedOtherId} — dropping spoofed packet.");
                reader.Recycle();
                return;
            }
            reader.SetPosition(origPos);

            switch (channel)
            {
                case BasisNetworkCommons.VoiceChannel:
                    _ = BasisNetworkHandleVoice.HandleAudioUpdate(reader, false);
                    break;
                case BasisNetworkCommons.VoiceLargeChannel:
                    _ = BasisNetworkHandleVoice.HandleAudioUpdate(reader, true);
                    break;
                default:
                    BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.InboundAvatarP2P, reader.AvailableBytes);
                    BasisNetworkHandleAvatar.HandleAvatarUpdate(reader, channel);
                    reader.Recycle();
                    break;
            }
        }

        private static bool IsAllowedP2PChannel(byte channel)
        {
            if (channel == BasisNetworkCommons.VoiceChannel ||
                channel == BasisNetworkCommons.VoiceLargeChannel)
                return true;
            return (channel >= BasisNetworkCommons.PlayerAvatarVeryLowChannel &&
                    channel <= BasisNetworkCommons.PlayerAvatarHighAdditionalChannel) ||
                   (channel >= BasisNetworkCommons.PlayerAvatarVeryLowLargeChannel &&
                    channel <= BasisNetworkCommons.PlayerAvatarHighAdditionalLargeChannel);
        }

        private static bool TryFindSessionByPeer(NetPeer peer, out ushort otherPlayerId)
        {
            foreach (var s in _sessionsByToken.Values)
            {
                if (s.P2PPeer != null && s.P2PPeer.Equals(peer))
                {
                    otherPlayerId = s.OtherPlayerId;
                    return true;
                }
            }
            otherPlayerId = 0;
            return false;
        }

        private static void ScheduleReconnect(Session s)
        {
            s.State = P2PSessionState.Reconnecting;
            s.P2PPeer = null;
            s.ExpectedRemoteAddress = null;
            s.ConnectIssued = false;
            NotifyStateChanged(s.OtherPlayerId, s.State);

            if (s.PunchAttempts >= MaxPunchAttempts)
            {
                BasisDebug.LogWarning($"[P2P] Giving up on player {s.OtherPlayerId} after {s.PunchAttempts} punch attempts.");
                DropSession(s, P2PSessionState.Failed);
                return;
            }
            StartPunch(s);
        }

        private static void DropSession(Session s, P2PSessionState finalState)
        {
            _sessionsByToken.TryRemove(s.Token, out _);
            _sessionsByOtherId.TryRemove(s.OtherPlayerId, out _);
            if (s.P2PPeer != null)
            {
                try { s.P2PPeer.Disconnect(); } catch { }
                s.P2PPeer = null;
            }
            NotifyStateChanged(s.OtherPlayerId, finalState);
        }

        private static void NotifyStateChanged(ushort otherPlayerId, P2PSessionState state)
        {
            try { OnSessionStateChanged?.Invoke(otherPlayerId, state); }
            catch (Exception ex) { BasisDebug.LogError($"[P2P] State change handler threw: {ex.Message}"); }
        }

        private static void SendSubToServer(byte sub, ushort otherPlayerId, string token)
        {
            var peer = BasisNetworkConnection.LocalPlayerPeer;
            if (peer == null) return;

            var writer = new NetDataWriter();
            writer.Put(sub);
            var body = new BasisP2PSignalMessage
            {
                otherPlayerId = otherPlayerId,
                sessionToken = token ?? string.Empty,
            };
            body.Serialize(writer);
            peer.Send(writer, BasisNetworkCommons.P2PChannel, DeliveryMethod.ReliableOrdered);
        }
    }
}
