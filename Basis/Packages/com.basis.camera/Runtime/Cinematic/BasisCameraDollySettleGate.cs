using System.Collections.Generic;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// Keeps a point where it was put down until the player who owns the track says the same thing
    /// back.
    ///
    /// <para>Letting go of a point does not make the author's copy right. A roster is a snapshot,
    /// and one captured before a drag can arrive after it — taking that at face value is what pulls
    /// a point back to where it started a moment after somebody moved it. So a released point holds
    /// its place, and the only thing that unlatches it is the author sending back the position they
    /// were asked for.</para>
    ///
    /// <para>The wait is bounded. A track locked mid-drag, or a move refused for any other reason,
    /// has no confirmation coming, and a point that stayed where a client wanted it forever would be
    /// that client quietly authoring somebody else's track.</para>
    ///
    /// <para>Kept free of waypoints, transforms and transport so the rule can be asserted on
    /// directly. It is a rule about time and arrival order, which is the kind that cannot be
    /// eyeballed and only ever shows up live as a point springing back.</para>
    /// </summary>
    public sealed class BasisCameraDollySettleGate
    {
        /// <summary>
        /// How long a released point defends its place. Comfortably longer than the author's
        /// keyframe interval, so a confirmation that had to wait for one still lands in time.
        /// </summary>
        public const float SettleSeconds = 2.5f;

        /// <summary>
        /// A millimetre, squared. The pose makes a float32 round trip through the packet and a
        /// transform, so a confirmation comes back all but identical; this is slack for the trip,
        /// not a tolerance for a different position.
        /// </summary>
        private const float SettleDistanceSqr = 1e-6f;

        private const float SettleAngleDegrees = 0.5f;

        private struct Settling
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float Expiry;
        }

        private readonly Dictionary<int, Settling> _settling = new Dictionary<int, Settling>();
        private readonly List<int> _dropped = new List<int>();

        public int Count => _settling.Count;

        public bool IsSettling(int slot) => _settling.ContainsKey(slot);

        /// <summary>Starts defending a slot at the place it was released in.</summary>
        public void Hold(int slot, Vector3 position, Quaternion rotation, float time)
        {
            _settling[slot] = new Settling
            {
                Position = position,
                Rotation = rotation,
                Expiry = time + SettleSeconds,
            };
        }

        /// <summary>
        /// Whether an arriving pose for this slot should be ignored. A pose that matches what was
        /// asked for is the author agreeing, which both ends the wait and is applied.
        /// </summary>
        public bool Blocks(int slot, Vector3 position, Quaternion rotation)
        {
            if (!_settling.TryGetValue(slot, out Settling settling))
            {
                return false;
            }

            if ((position - settling.Position).sqrMagnitude <= SettleDistanceSqr &&
                Quaternion.Angle(rotation, settling.Rotation) <= SettleAngleDegrees)
            {
                _settling.Remove(slot);
                return false;
            }
            return true;
        }

        /// <summary>Gives up on a slot, used when it is picked up again before it ever settled.</summary>
        public void Release(int slot) => _settling.Remove(slot);

        public void Expire(float time)
        {
            if (_settling.Count == 0)
            {
                return;
            }

            _dropped.Clear();
            foreach (KeyValuePair<int, Settling> pair in _settling)
            {
                if (time >= pair.Value.Expiry)
                {
                    _dropped.Add(pair.Key);
                }
            }
            RemoveDropped();
        }

        /// <summary>Forgets slots the track no longer has, so a shortened track leaves nothing behind.</summary>
        public void DropAtOrAbove(int count)
        {
            if (_settling.Count == 0)
            {
                return;
            }

            _dropped.Clear();
            foreach (KeyValuePair<int, Settling> pair in _settling)
            {
                if (pair.Key >= count)
                {
                    _dropped.Add(pair.Key);
                }
            }
            RemoveDropped();
        }

        public void Clear()
        {
            _settling.Clear();
            _dropped.Clear();
        }

        private void RemoveDropped()
        {
            for (int Index = 0; Index < _dropped.Count; Index++)
            {
                _settling.Remove(_dropped[Index]);
            }
        }
    }
}
