using System;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>One waypoint of a preset, in the preset's own frame rather than the world's.</summary>
    [Serializable]
    public struct BasisCameraDollyPresetPoint
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    /// <summary>
    /// A dolly track saved to be laid out again: the shape of the path, and the move that rides it.
    ///
    /// <para>The points are stored against an anchor — where the author stood, which way they
    /// faced, and how big they were — rather than in world coordinates. A track is a shape, and a
    /// shape is the part worth keeping: stored raw, a saved arc would come back in the corner of a
    /// world it was never built for, or at a tenth of its size on a smaller avatar. Keeping the
    /// anchor as well means the exact original placement is still available, so a preset can be
    /// dropped where you are standing or put back where it was made.</para>
    /// </summary>
    [Serializable]
    public class BasisCameraDollyPreset
    {
        /// <summary>Shared with saved modes, so one name field can serve both without a surprise.</summary>
        public const int MaxNameLength = BasisCameraUserMode.MaxNameLength;

        /// <summary>Matches the waypoint cap the panel and the wire format already share.</summary>
        public const int MaxPoints = BasisCameraDollyPacket.MaxPoints;

        /// <summary>
        /// Characters a preset name may not carry, because the name becomes a file name on export.
        /// The separators and the wildcards would break the path or make it name something else;
        /// the full stop is here so a name cannot dress itself up as a second extension.
        /// </summary>
        private const string FileNameReserved = "/\\:*?\"<>|.";

        public string name;

        public Vector3 anchorPosition;
        public float anchorYaw;

        /// <summary>Avatar scale the track was laid out at. Never zero — that would divide the shape away.</summary>
        public float anchorScale = 1f;

        public bool looped;
        public bool gridSnap;
        public float gridSize = 0.25f;

        /// <summary>
        /// The move itself. <c>playing</c> and <c>syncMode</c> are cleared on capture: a preset that
        /// started running the moment it loaded would fly the camera off, and one that arrived
        /// already shared would publish a track to the instance nobody asked to share.
        /// </summary>
        public BasisCameraDollySettings motion = BasisCameraDollySettings.Default;

        public List<BasisCameraDollyPresetPoint> points = new List<BasisCameraDollyPresetPoint>();

        public int Count => points?.Count ?? 0;

        /// <summary>The anchor's frame. Yaw only — a track tipped by the author's pitch is not a track.</summary>
        public Quaternion AnchorRotation => Quaternion.Euler(0f, anchorYaw, 0f);

        /// <summary>
        /// Takes the track down against an anchor. Positions arrive in world space, as the live
        /// markers hold them.
        /// </summary>
        public void Capture(IReadOnlyList<Vector3> positions, IReadOnlyList<Quaternion> rotations,
            Vector3 anchor, float yaw, float scale)
        {
            anchorPosition = anchor;
            anchorYaw = yaw;
            anchorScale = Mathf.Approximately(scale, 0f) ? 1f : scale;

            points ??= new List<BasisCameraDollyPresetPoint>();
            points.Clear();
            if (positions == null) return;

            Quaternion inverse = Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f));
            int count = Mathf.Min(positions.Count, MaxPoints);
            for (int Index = 0; Index < count; Index++)
            {
                Quaternion rotation = rotations != null && Index < rotations.Count
                    ? rotations[Index]
                    : Quaternion.identity;

                points.Add(new BasisCameraDollyPresetPoint
                {
                    position = inverse * (positions[Index] - anchor) / anchorScale,
                    rotation = inverse * rotation,
                });
            }
        }

        /// <summary>
        /// Where a stored point lands when the preset is placed at an anchor. Passing the preset's
        /// own <see cref="anchorPosition"/>, <see cref="anchorYaw"/> and <see cref="anchorScale"/>
        /// puts it back exactly where it was captured.
        /// </summary>
        public void Resolve(int index, Vector3 anchor, float yaw, float scale,
            out Vector3 position, out Quaternion rotation)
        {
            if (points == null || index < 0 || index >= points.Count)
            {
                position = anchor;
                rotation = Quaternion.identity;
                return;
            }

            Quaternion frame = Quaternion.Euler(0f, yaw, 0f);
            BasisCameraDollyPresetPoint point = points[index];

            position = anchor + frame * (point.position * (Mathf.Approximately(scale, 0f) ? 1f : scale));
            rotation = frame * point.rotation;
        }

        /// <summary>
        /// Trims a name to something that can be stored, listed and matched, and that is safe to
        /// use as a file name on export. Returns null for anything left with nothing in it.
        /// </summary>
        public static string SanitizeName(string raw)
        {
            string cleaned = BasisCameraUserMode.SanitizeName(raw);
            if (cleaned == null) return null;

            char[] characters = cleaned.ToCharArray();
            bool changed = false;
            for (int Index = 0; Index < characters.Length; Index++)
            {
                if (FileNameReserved.IndexOf(characters[Index]) < 0) continue;

                characters[Index] = ' ';
                changed = true;
            }

            return changed ? BasisCameraUserMode.SanitizeName(new string(characters)) : cleaned;
        }

        public static bool NamesMatch(string left, string right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
