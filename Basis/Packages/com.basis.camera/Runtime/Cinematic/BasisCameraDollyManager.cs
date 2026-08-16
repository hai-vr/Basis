using System.Collections.Generic;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Network.Core;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// Shares dolly tracks across the instance.
    ///
    /// <para>The handheld camera is a local prop — remotes only ever see a PIP stand-in — so there
    /// is no per-camera network identity to hang a track off. This resolves one well-known name
    /// into an id every client agrees on, the same trick the image pickup manager uses, and keys
    /// each track by the player who authored it. That needs no new channel (0-63 are all spoken
    /// for) and no server change.</para>
    ///
    /// <para>Authority is split. The author owns the roster — what points exist, in what order,
    /// and how the track is shared — and anyone may move a point while the track is Shared, which
    /// is what makes a move something two people can build together. A move is applied to the
    /// author's own waypoints and comes back out in their next roster, so the author's track stays
    /// the single answer to "where are the points" and two people reaching for the same one cannot
    /// leave the instance disagreeing.</para>
    /// </summary>
    public static class BasisCameraDollyManager
    {
        private const string FixedNetworkIdentifier = "BasisCameraDollyManager";
        private const BasisDebug.LogTag LogTag = BasisDebug.LogTag.Networking;

        /// <summary>Seconds between unprompted resends, so a late joiner is never left with nothing.</summary>
        private const float KeyframeInterval = 2f;

        /// <summary>Seconds between roster sends while points are being dragged about.</summary>
        private const float RosterInterval = 0.1f;

        public static bool HasNetworkID { get; private set; }
        public static ushort NetworkID { get; private set; }

        private static bool _initialized;

        /// <summary>The local track being shared, or null while nothing is.</summary>
        private static BasisCameraDollyTrack _local;

        /// <summary>Everyone else's tracks, by the player who authored each one.</summary>
        private static readonly Dictionary<ushort, BasisCameraDollyMirror> _mirrors = new();

        private static readonly BasisCameraDollyPacket.Point[] _scratch =
            new BasisCameraDollyPacket.Point[BasisCameraDollyPacket.MaxPoints];

        private static byte[] _sendBuffer = new byte[BasisCameraDollyPacket.RosterSize(BasisCameraDollyPacket.MaxPoints)];
        private static readonly ushort[] _oneRecipient = new ushort[1];

        private static float _nextRoster;
        private static float _nextKeyframe;
        private static int _lastSentCount = -1;
        private static BasisCameraDollySync _lastSentMode = BasisCameraDollySync.LocalOnly;

        // ---- Lifecycle -------------------------------------------------------------------

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            BasisNetworkPlayer.OnPlayerLeft -= HandlePlayerLeft;
            BasisNetworkPlayer.OnPlayerLeft += HandlePlayerLeft;
            BasisNetworkPlayer.OnPlayerJoined -= HandlePlayerJoined;
            BasisNetworkPlayer.OnPlayerJoined += HandlePlayerJoined;

            ResolveNetworkId();
        }

        public static void Shutdown()
        {
            BasisNetworkPlayer.OnPlayerLeft -= HandlePlayerLeft;
            BasisNetworkPlayer.OnPlayerJoined -= HandlePlayerJoined;

            if (HasNetworkID)
            {
                BasisNetworkGenericMessages.UnregisterDirectHandler(NetworkID);
            }
            HasNetworkID = false;
            NetworkID = 0;
            _initialized = false;
            _local = null;

            ClearAllMirrors();
        }

        private static async void ResolveNetworkId()
        {
            if (HasNetworkID) return;

            BasisIdResolutionResult resolution = await BasisNetworkIdResolver.ResolveAsync(FixedNetworkIdentifier);
            if (!_initialized || HasNetworkID) return;

            if (!resolution.Success)
            {
                BasisDebug.LogError(
                    $"Dolly manager could not resolve the network identifier '{FixedNetworkIdentifier}'.", LogTag);
                return;
            }

            NetworkID = resolution.Id;
            HasNetworkID = true;
            BasisNetworkGenericMessages.RegisterDirectHandler(NetworkID, OnDirectNetworkMessage);
        }

        // ---- The local track --------------------------------------------------------------

        /// <summary>
        /// Names the track this client is sharing. Passing null, or a track set to Local Only,
        /// tells everyone else to drop it.
        /// </summary>
        public static void SetLocalTrack(BasisCameraDollyTrack track)
        {
            if (ReferenceEquals(_local, track)) return;

            // The previous track has to be withdrawn explicitly. Silently swapping would leave it
            // standing on every other screen with nothing left to update it.
            if (_local != null) BroadcastWithdrawal();

            _local = track;
            _lastSentCount = -1;
            _nextRoster = 0f;
            _nextKeyframe = 0f;
        }

        /// <summary>
        /// Sends the local track when it is due. Called from the camera's own frame, so a client
        /// with no camera out costs nothing at all.
        /// </summary>
        public static void Tick(float time)
        {
            if (!HasNetworkID || _local == null) return;

            if (_local.SyncMode == BasisCameraDollySync.LocalOnly)
            {
                // Only once: a track nobody is sharing should not keep announcing that.
                if (_lastSentCount != -1)
                {
                    BroadcastWithdrawal();
                    _lastSentCount = -1;
                }
                return;
            }

            bool changed = _local.Count != _lastSentCount || _local.SyncMode != _lastSentMode;
            bool due = changed ? time >= _nextRoster : time >= _nextKeyframe;
            if (!due) return;

            BroadcastRoster(null);
            _nextRoster = time + RosterInterval;
            _nextKeyframe = time + KeyframeInterval;
            _lastSentCount = _local.Count;
            _lastSentMode = _local.SyncMode;
        }

        private static void BroadcastRoster(ushort[] recipients)
        {
            int count = Mathf.Min(_local.Count, BasisCameraDollyPacket.MaxPoints);
            for (int Index = 0; Index < count; Index++)
            {
                BasisCameraDollyWaypoint waypoint = _local.GetWaypoint(Index);
                if (waypoint == null) continue;

                _scratch[Index] = new BasisCameraDollyPacket.Point
                {
                    Position = waypoint.Position,
                    Rotation = waypoint.Rotation,
                };
            }

            EnsureBuffer(BasisCameraDollyPacket.RosterSize(count));
            int written = BasisCameraDollyPacket.WriteRoster(_sendBuffer, _local.SyncMode, _scratch, count);
            if (written > 0) Send(written, DeliveryMethod.ReliableOrdered, recipients);
        }

        /// <summary>Tells everyone the track is no longer shared, so their copies come down.</summary>
        private static void BroadcastWithdrawal()
        {
            if (!HasNetworkID) return;

            EnsureBuffer(BasisCameraDollyPacket.RosterSize(0));
            int written = BasisCameraDollyPacket.WriteRoster(_sendBuffer, BasisCameraDollySync.LocalOnly, _scratch, 0);
            if (written > 0) Send(written, DeliveryMethod.ReliableOrdered, null);
        }

        /// <summary>
        /// Reports a point somebody is dragging on another player's track. The author applies it;
        /// this client is only asking.
        /// </summary>
        public static void SendPointMove(ushort owner, int slot, Vector3 position, Quaternion rotation)
        {
            if (!HasNetworkID) return;

            EnsureBuffer(BasisCameraDollyPacket.PointMoveSize);
            int written = BasisCameraDollyPacket.WritePointMove(_sendBuffer, owner, slot, position, rotation);
            if (written > 0) Send(written, DeliveryMethod.Unreliable, null);
        }

        public static void SendClaim(ushort owner, int slot, bool claimed)
        {
            if (!HasNetworkID) return;

            EnsureBuffer(BasisCameraDollyPacket.ClaimPacketSize);
            int written = BasisCameraDollyPacket.WriteClaim(_sendBuffer, owner, slot, claimed);
            if (written > 0) Send(written, DeliveryMethod.ReliableOrdered, null);
        }

        // ---- Receiving --------------------------------------------------------------------

        private static void OnDirectNetworkMessage(ushort playerId, byte[] buffer, DeliveryMethod delivery)
        {
            int length = buffer?.Length ?? 0;
            if (!BasisCameraDollyPacket.TryReadType(buffer, length, out BasisCameraDollyPacketType type)) return;

            switch (type)
            {
                case BasisCameraDollyPacketType.Roster:
                    ApplyRoster(playerId, buffer, length);
                    break;

                case BasisCameraDollyPacketType.PointMove:
                    ApplyPointMove(playerId, buffer, length);
                    break;

                case BasisCameraDollyPacketType.Claim:
                    ApplyClaim(playerId, buffer, length);
                    break;
            }
        }

        private static void ApplyRoster(ushort playerId, byte[] buffer, int length)
        {
            if (!BasisCameraDollyPacket.TryReadRoster(buffer, length, _scratch,
                out BasisCameraDollySync mode, out int count))
            {
                return;
            }

            if (mode == BasisCameraDollySync.LocalOnly || count == 0)
            {
                DropMirror(playerId);
                return;
            }

            if (!_mirrors.TryGetValue(playerId, out BasisCameraDollyMirror mirror))
            {
                mirror = new BasisCameraDollyMirror(playerId);
                _mirrors[playerId] = mirror;
            }
            mirror.Apply(mode, _scratch, count);
        }

        private static void ApplyPointMove(ushort playerId, byte[] buffer, int length)
        {
            if (!BasisCameraDollyPacket.TryReadPointMove(buffer, length, out ushort owner, out int slot,
                out Vector3 position, out Quaternion rotation))
            {
                return;
            }

            // Only the author applies a move to real waypoints, and only while the track is open
            // to it. A locked track ignores everyone else, which is the whole of what locked means.
            if (_local != null && owner == LocalPlayerId())
            {
                if (_local.SyncMode != BasisCameraDollySync.Networked) return;

                BasisCameraDollyWaypoint waypoint = _local.GetWaypoint(slot);
                if (waypoint != null) waypoint.PlaceFromNetwork(position, rotation);
                return;
            }

            // Everyone else moves their copy straight away rather than waiting for the author's
            // next roster, so a point being dragged does not stutter at the roster rate.
            if (_mirrors.TryGetValue(owner, out BasisCameraDollyMirror mirror))
            {
                mirror.MovePoint(slot, position, rotation);
            }
        }

        private static void ApplyClaim(ushort playerId, byte[] buffer, int length)
        {
            if (!BasisCameraDollyPacket.TryReadClaim(buffer, length, out ushort owner, out int slot, out bool claimed))
            {
                return;
            }

            if (_mirrors.TryGetValue(owner, out BasisCameraDollyMirror mirror))
            {
                mirror.SetClaimed(slot, claimed ? playerId : (ushort?)null);
            }
        }

        // ---- Roster upkeep ----------------------------------------------------------------

        private static void HandlePlayerJoined(BasisNetworkPlayer player)
        {
            if (!HasNetworkID || _local == null || player == null) return;
            if (_local.SyncMode == BasisCameraDollySync.LocalOnly) return;

            // Straight to the joiner rather than waiting for the keyframe, so a track is there the
            // moment they arrive instead of appearing seconds later.
            _oneRecipient[0] = player.playerId;
            BroadcastRoster(_oneRecipient);
        }

        private static void HandlePlayerLeft(BasisNetworkPlayer player)
        {
            if (player == null) return;
            DropMirror(player.playerId);
        }

        private static void DropMirror(ushort playerId)
        {
            if (_mirrors.TryGetValue(playerId, out BasisCameraDollyMirror mirror))
            {
                mirror.Dispose();
                _mirrors.Remove(playerId);
            }
        }

        private static void ClearAllMirrors()
        {
            foreach (KeyValuePair<ushort, BasisCameraDollyMirror> pair in _mirrors)
            {
                pair.Value.Dispose();
            }
            _mirrors.Clear();
        }

        /// <summary>Refreshes every mirrored track's markers. Driven from the camera's frame.</summary>
        public static void TickMirrors(float scale, Quaternion labelFacing)
        {
            foreach (KeyValuePair<ushort, BasisCameraDollyMirror> pair in _mirrors)
            {
                pair.Value.Refresh(scale, labelFacing);
            }
        }

        // ---- Plumbing ---------------------------------------------------------------------

        private static ushort LocalPlayerId()
            => BasisNetworkConnection.LocalPlayerPeer != null ? (ushort)BasisNetworkConnection.LocalPlayerPeer.Id : (ushort)0;

        private static void EnsureBuffer(int size)
        {
            if (_sendBuffer == null || _sendBuffer.Length < size)
            {
                _sendBuffer = new byte[size];
            }
        }

        private static void Send(int length, DeliveryMethod delivery, ushort[] recipients)
        {
            byte[] payload = _sendBuffer;
            if (length != _sendBuffer.Length)
            {
                payload = new byte[length];
                System.Array.Copy(_sendBuffer, payload, length);
            }
            BasisNetworkGenericMessages.OnNetworkMessageSendDirect(NetworkID, payload, delivery, recipients);
        }

        /// <summary>Whether a track authored by somebody else is currently being shown.</summary>
        public static bool HasMirror(ushort playerId) => _mirrors.ContainsKey(playerId);

        public static int MirrorCount => _mirrors.Count;
    }
}
