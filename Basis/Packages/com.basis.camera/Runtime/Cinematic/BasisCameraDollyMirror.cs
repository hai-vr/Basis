using System.Collections.Generic;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// One other player's dolly track, as shown on this client.
    ///
    /// <para>The markers are the same local primitives the author uses, built from what arrives
    /// rather than replicated as content — a waypoint is an authoring aid, and turning each one
    /// into a spawned prop would put debug furniture into the world's prop budget and through
    /// content policing for no gain.</para>
    ///
    /// <para>Grabbing one does not move it locally and wait to be corrected. It reports the drag
    /// to the author, who owns where the points are; the position that comes back is the answer.
    /// Guessing locally is what makes two people dragging the same point fight each other.</para>
    /// </summary>
    public sealed class BasisCameraDollyMirror
    {
        private readonly ushort _owner;
        private readonly BasisCameraDollyTrack _track = new BasisCameraDollyTrack();
        private readonly Dictionary<int, ushort> _claims = new Dictionary<int, ushort>();

        private BasisCameraDollySync _mode = BasisCameraDollySync.LocalOnly;

        public BasisCameraDollyMirror(ushort owner)
        {
            _owner = owner;
            _track.IsAuthor = false;
        }

        public ushort Owner => _owner;

        public int Count => _track.Count;

        public BasisCameraDollySync Mode => _mode;

        /// <summary>Whoever is holding a point, or null when nobody is.</summary>
        public bool TryGetClaim(int slot, out ushort holder) => _claims.TryGetValue(slot, out holder);

        /// <summary>
        /// Brings the shown track in line with what the author sent: the same count, the same
        /// places, and the same rule about whether it can be touched.
        /// </summary>
        public void Apply(BasisCameraDollySync mode, BasisCameraDollyPacket.Point[] points, int count)
        {
            _mode = mode;
            _track.SyncMode = mode;

            count = Mathf.Clamp(count, 0, BasisCameraDollyPacket.MaxPoints);

            // Add or remove only the difference. Rebuilding the list every packet would destroy
            // and respawn every marker several times a second, which drops whatever somebody had
            // hold of and restarts the pickup's hover state from nothing.
            while (_track.Count > count)
            {
                _track.RemoveWaypointAt(_track.Count - 1);
            }
            while (_track.Count < count)
            {
                int slot = _track.Count;
                _track.AddWaypoint(points[slot].Position, points[slot].Rotation, 1f);
            }

            for (int Index = 0; Index < count; Index++)
            {
                BasisCameraDollyWaypoint waypoint = _track.GetWaypoint(Index);
                if (waypoint == null) continue;

                // A point somebody here is holding is left alone: they are already dragging it,
                // and writing the author's older copy over the top fights their hand.
                if (waypoint.IsGrabbed) continue;

                waypoint.PlaceFromNetwork(points[Index].Position, points[Index].Rotation);
            }
        }

        /// <summary>Moves one point without waiting for the author's next full send.</summary>
        public void MovePoint(int slot, Vector3 position, Quaternion rotation)
        {
            BasisCameraDollyWaypoint waypoint = _track.GetWaypoint(slot);
            if (waypoint == null || waypoint.IsGrabbed) return;

            waypoint.PlaceFromNetwork(position, rotation);
        }

        public void SetClaimed(int slot, ushort? holder)
        {
            if (holder.HasValue) _claims[slot] = holder.Value;
            else _claims.Remove(slot);
        }

        /// <summary>
        /// Redraws the markers and reports anything being dragged here back to the author. Called
        /// every frame from the camera, the same as the local track.
        /// </summary>
        public void Refresh(float scale, Quaternion labelFacing)
        {
            _track.Refresh(scale, labelFacing);

            if (_mode != BasisCameraDollySync.Networked) return;

            for (int Index = 0; Index < _track.Count; Index++)
            {
                BasisCameraDollyWaypoint waypoint = _track.GetWaypoint(Index);
                if (waypoint == null || !waypoint.IsGrabbed) continue;

                BasisCameraDollyManager.SendPointMove(_owner, Index, waypoint.Position, waypoint.Rotation);
            }
        }

        public void Dispose()
        {
            _track.Clear();
            _track.Dispose();
            _claims.Clear();
        }
    }
}
