using Basis.Network.Core;
using System.Collections.Concurrent;
using System.Net;
using static BasisNetworkCore.Serializable.SerializableBasis;
using static SerializableBasis;
using LiteNatPunchListener = LiteNetLib.EventBasedNatPunchListener;

namespace BasisNetworkServer
{
    public static class BasisServerP2PBroker
    {
        private enum SessionState : byte { Awaiting, ReadyForPunch, Punched }

        private sealed class Session
        {
            public string Token;
            public int InitiatorPeerId;
            public int TargetPeerId;
            public SessionState State;

            public IPEndPoint EndpointA_Internal;
            public IPEndPoint EndpointA_External;
            public IPEndPoint EndpointB_Internal;
            public IPEndPoint EndpointB_External;
            public bool HasA;
            public bool HasB;

            public bool InitiatorLinkUp;
            public bool TargetLinkUp;
        }

        private static readonly ConcurrentDictionary<string, Session> _sessions = new();
        private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> _peerSessions = new();
        private static readonly ConcurrentDictionary<long, byte> _offloadedPairs = new();

        private static long PackPair(int a, int b)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            return ((long)lo << 32) | (uint)hi;
        }

        public static bool IsP2POffloaded(int a, int b)
        {
            if (a == b) return false;
            return _offloadedPairs.ContainsKey(PackPair(a, b));
        }

        private static LiteNatPunchListener _natListener;

        public static void Initialize()
        {
            if (_natListener != null) return;

            var manager = (NetworkServer.Server as LNLNetManager)?.manager;
            if (manager == null)
            {
                BNL.LogError("[P2P] NetManager not initialised or active stack is not LiteNetLib, cannot start P2P broker.");
                return;
            }

            if (!manager.NatPunchEnabled)
            {
                BNL.LogWarning("[P2P] NatPunchEnabled=false in server config — direct peer connections will not work. Set NatPunchEnabled=true to enable.");
            }

            _natListener = new LiteNatPunchListener();
            _natListener.NatIntroductionRequest += OnNatIntroductionRequest;
            manager.NatPunchModule.Init(_natListener);
            manager.NatPunchModule.UnsyncedEvents = true;

            BNL.Log("[P2P] Broker initialised.");
        }

        public static void HandleP2PMessage(NetPacketReader reader, NetPeer peer)
        {
            byte sub = reader.GetByte();
            BasisP2PSignalMessage msg = default;
            msg.Deserialize(reader);
            reader.Recycle();

            switch (sub)
            {
                case BasisNetworkCommons.P2PSub_Request:
                    HandleRequest(peer, msg);
                    break;
                case BasisNetworkCommons.P2PSub_Accept:
                    HandleAccept(peer, msg);
                    break;
                case BasisNetworkCommons.P2PSub_Decline:
                    ForwardAndDrop(peer, msg, BasisNetworkCommons.P2PSub_Decline);
                    break;
                case BasisNetworkCommons.P2PSub_Cancel:
                    ForwardAndDrop(peer, msg, BasisNetworkCommons.P2PSub_Cancel);
                    break;
                case BasisNetworkCommons.P2PSub_LinkLost:
                    HandleLinkLost(peer, msg);
                    break;
                case BasisNetworkCommons.P2PSub_LinkUp:
                    HandleLinkUp(peer, msg);
                    break;
                default:
                    BNL.LogError($"[P2P] Unknown sub-type {sub} from peer {peer.Id}.");
                    break;
            }
        }

        private static void HandleLinkUp(NetPeer sender, BasisP2PSignalMessage msg)
        {
            if (!_sessions.TryGetValue(msg.sessionToken, out Session s)) return;

            if (sender.Id == s.InitiatorPeerId) s.InitiatorLinkUp = true;
            else if (sender.Id == s.TargetPeerId) s.TargetLinkUp = true;
            else return;

            BNL.Log($"[P2P] LinkUp from peer {sender.Id} (token {Preview(s.Token)}); flags InitiatorUp={s.InitiatorLinkUp} TargetUp={s.TargetLinkUp}.");
            if (s.InitiatorLinkUp && s.TargetLinkUp)
            {
                _offloadedPairs[PackPair(s.InitiatorPeerId, s.TargetPeerId)] = 0;
                BNL.Log($"[P2P] OFFLOADED pair ({s.InitiatorPeerId},{s.TargetPeerId}) — server will skip relaying voice + avatar between them.");
            }
        }

        private static void HandleRequest(NetPeer sender, BasisP2PSignalMessage msg)
        {
            if (string.IsNullOrEmpty(msg.sessionToken))
            {
                BNL.LogError($"[P2P] Empty session token from peer {sender.Id}, dropping Request.");
                return;
            }
            if (msg.otherPlayerId == sender.Id)
            {
                BNL.LogError($"[P2P] Peer {sender.Id} tried to request a session with itself.");
                return;
            }
            if (!NetworkServer.AuthenticatedPeers.TryGetValue(msg.otherPlayerId, out NetPeer target))
            {
                SendSub(sender, BasisNetworkCommons.P2PSub_Cancel, msg.sessionToken, msg.otherPlayerId);
                return;
            }

            var session = new Session
            {
                Token = msg.sessionToken,
                InitiatorPeerId = sender.Id,
                TargetPeerId = msg.otherPlayerId,
                State = SessionState.Awaiting,
            };
            _sessions[msg.sessionToken] = session;
            TrackPeerSession(sender.Id, msg.sessionToken);
            TrackPeerSession(msg.otherPlayerId, msg.sessionToken);

            BNL.Log($"[P2P] Forwarding Request from peer {sender.Id} to peer {msg.otherPlayerId} (token {msg.sessionToken}).");
            SendSub(target, BasisNetworkCommons.P2PSub_Request, msg.sessionToken, (ushort)sender.Id);

            // ServerArmed confirms registration before either side starts punching, avoiding a race.
            SendSub(sender, BasisNetworkCommons.P2PSub_ServerArmed, msg.sessionToken, msg.otherPlayerId);
        }

        private static void HandleAccept(NetPeer sender, BasisP2PSignalMessage msg)
        {
            if (!_sessions.TryGetValue(msg.sessionToken, out Session s))
            {
                BNL.LogError($"[P2P] Accept for unknown token from peer {sender.Id}.");
                return;
            }
            if (s.TargetPeerId != sender.Id || s.InitiatorPeerId != msg.otherPlayerId)
            {
                BNL.LogError($"[P2P] Accept from peer {sender.Id} doesn't match session pair ({s.InitiatorPeerId},{s.TargetPeerId}).");
                return;
            }

            s.State = SessionState.ReadyForPunch;

            if (NetworkServer.AuthenticatedPeers.TryGetValue(s.InitiatorPeerId, out NetPeer initiator))
            {
                BNL.Log($"[P2P] Accept from peer {sender.Id} (token {Preview(s.Token)}); session armed, forwarding to initiator {s.InitiatorPeerId}.");
                SendSub(initiator, BasisNetworkCommons.P2PSub_Accept, s.Token, (ushort)sender.Id);
            }
            else
            {
                BNL.LogWarning($"[P2P] Accept arrived but initiator {s.InitiatorPeerId} already gone; dropping session {Preview(s.Token)}.");
                RemoveSession(s.Token);
            }
        }

        private static void HandleLinkLost(NetPeer sender, BasisP2PSignalMessage msg)
        {
            // Re-arm session + clear offload so relay resumes during re-punch window.
            if (_sessions.TryGetValue(msg.sessionToken, out Session s))
            {
                bool wasOffloaded = _offloadedPairs.ContainsKey(PackPair(s.InitiatorPeerId, s.TargetPeerId));
                s.HasA = false;
                s.HasB = false;
                s.InitiatorLinkUp = false;
                s.TargetLinkUp = false;
                s.State = SessionState.ReadyForPunch;
                _offloadedPairs.TryRemove(PackPair(s.InitiatorPeerId, s.TargetPeerId), out _);
                BNL.Log($"[P2P] LinkLost from peer {sender.Id} (token {Preview(s.Token)}); re-armed for punch, offload {(wasOffloaded ? "cleared (relay resumed)" : "already cleared")}.");
            }
            ForwardAndDrop(sender, msg, BasisNetworkCommons.P2PSub_LinkLost, dropSession: false);
        }

        private static void ForwardAndDrop(NetPeer sender, BasisP2PSignalMessage msg, byte sub, bool dropSession = true)
        {
            if (NetworkServer.AuthenticatedPeers.TryGetValue(msg.otherPlayerId, out NetPeer other))
            {
                SendSub(other, sub, msg.sessionToken, (ushort)sender.Id);
            }
            if (dropSession && !string.IsNullOrEmpty(msg.sessionToken))
            {
                RemoveSession(msg.sessionToken);
            }
        }

        private static void OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, string token)
        {
            if (string.IsNullOrEmpty(token)) return;
            if (!_sessions.TryGetValue(token, out Session s))
            {
                BNL.LogWarning($"[P2P] NatIntroduceRequest with unknown token {Preview(token)} from {remoteEndPoint} — dropping.");
                return;
            }
            if (s.State < SessionState.ReadyForPunch)
            {
                BNL.LogWarning($"[P2P] NatIntroduceRequest for token {Preview(token)} in state {s.State} — not ready, dropping.");
                return;
            }
            BNL.Log($"[P2P] NatIntroduceRequest token={Preview(token)} from internal={localEndPoint} external={remoteEndPoint}; HasA={s.HasA} HasB={s.HasB}.");

            // Arrival order labels the slots; NatIntroduce is symmetric so it doesn't matter which is which.
            lock (s)
            {
                if (!s.HasA)
                {
                    s.EndpointA_Internal = localEndPoint;
                    s.EndpointA_External = remoteEndPoint;
                    s.HasA = true;
                }
                else if (!s.HasB)
                {
                    s.EndpointB_Internal = localEndPoint;
                    s.EndpointB_External = remoteEndPoint;
                    s.HasB = true;
                }

                if (s.HasA && s.HasB)
                {
                    bool sameNat = s.EndpointA_External != null &&
                                   s.EndpointB_External != null &&
                                   s.EndpointA_External.Address.Equals(s.EndpointB_External.Address);
                    string lanTag = sameNat ? " [SAME-NETWORK]" : "";
                    BNL.Log($"[P2P] Both NAT endpoints collected for token {Preview(token)}: A={s.EndpointA_External} (int {s.EndpointA_Internal}), B={s.EndpointB_External} (int {s.EndpointB_Internal}). Firing NatIntroduce.{lanTag}");
                    LiteNetLib.NetManager lnlManager = (NetworkServer.Server as LNLNetManager)?.manager;
                    if (lnlManager == null) return;
                    lnlManager.NatPunchModule.NatIntroduce(
                        s.EndpointA_Internal,
                        s.EndpointA_External,
                        s.EndpointB_Internal,
                        s.EndpointB_External,
                        token);
                    s.State = SessionState.Punched;
                }
            }
        }

        private static string Preview(string token)
        {
            if (string.IsNullOrEmpty(token)) return "(empty)";
            return token.Length <= 8 ? token : token.Substring(0, 8);
        }

        public static void RemovePeer(int peerId)
        {
            if (!_peerSessions.TryRemove(peerId, out var tokens)) return;
            BNL.Log($"[P2P] Peer {peerId} disconnected; closing out {tokens.Count} P2P session(s).");
            foreach (var kv in tokens)
            {
                string token = kv.Key;
                if (!_sessions.TryGetValue(token, out Session s)) continue;
                int otherId = s.InitiatorPeerId == peerId ? s.TargetPeerId : s.InitiatorPeerId;
                if (NetworkServer.AuthenticatedPeers.TryGetValue(otherId, out NetPeer other))
                {
                    BNL.Log($"[P2P] Notifying peer {otherId} via Cancel that peer {peerId} is gone (token {Preview(token)}).");
                    SendSub(other, BasisNetworkCommons.P2PSub_Cancel, token, (ushort)peerId);
                }
                RemoveSession(token);
            }
        }

        private static void RemoveSession(string token)
        {
            if (!_sessions.TryRemove(token, out Session s)) return;
            UntrackPeerSession(s.InitiatorPeerId, token);
            UntrackPeerSession(s.TargetPeerId, token);
            _offloadedPairs.TryRemove(PackPair(s.InitiatorPeerId, s.TargetPeerId), out _);
        }

        private static void TrackPeerSession(int peerId, string token)
        {
            var inner = _peerSessions.GetOrAdd(peerId, _ => new ConcurrentDictionary<string, byte>());
            inner[token] = 0;
        }

        private static void UntrackPeerSession(int peerId, string token)
        {
            if (_peerSessions.TryGetValue(peerId, out var inner))
            {
                inner.TryRemove(token, out _);
            }
        }

        private static void SendSub(NetPeer to, byte sub, string token, ushort otherPlayerId)
        {
            NetDataWriter writer = NetworkServer.RentWriter();
            writer.Put(sub);
            var body = new BasisP2PSignalMessage
            {
                otherPlayerId = otherPlayerId,
                sessionToken = token ?? string.Empty,
            };
            body.Serialize(writer);
            NetworkServer.TrySend(to, writer, BasisNetworkCommons.P2PChannel, DeliveryMethod.ReliableOrdered);
            NetworkServer.ReturnWriter(writer);
        }
    }
}
