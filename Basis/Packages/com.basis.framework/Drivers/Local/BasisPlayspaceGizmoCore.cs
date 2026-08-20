using System.Collections.Generic;
using UnityEngine;

namespace Basis.Scripts.Drivers
{

    /// <summary>
    /// The mover's per-frame hand state, handed to the play-space gizmos so they draw the grab the
    /// solve actually saw (device present, which input is held, where the hand was measured from)
    /// rather than polling the devices a second time.
    /// </summary>
    public struct BasisPlayspaceMoverSample
    {
        public BasisPlayspaceMoverState State;
        public bool LeftPresent;
        public bool RightPresent;
        public bool LeftHeld;
        public bool RightHeld;
        public bool LeftRotate;
        public bool RightRotate;
        /// <summary>Hand position in player-local (bone) space, the frame the mover read it.</summary>
        public Vector3 LeftLocal;
        public Vector3 RightLocal;
        /// <summary>Hand separation captured when the two-hand scale gesture began, in unscaled metres.</summary>
        public float SpanBase;
        /// <summary>Live hand separation while scaling, in unscaled metres.</summary>
        public float SpanCurrent;
    }

    /// <summary>Extents of a play-area outline, in whatever space its points were given in.</summary>
    public struct BasisPlayspaceBoundsMetrics
    {
        public bool Valid;
        public Vector3 Center;
        public float SizeX;
        public float SizeZ;
        public float Area;
        public float Perimeter;
    }

    /// <summary>
    /// The pure geometry behind the play-space gizmos: the tracking-space to player-local transform
    /// a device pose takes, and the extents of a play-area outline.
    /// </summary>
    public static class BasisPlayspaceGizmoCore
    {
        /// <summary>
        /// Total vertical tracking-space shift currently injected into every device pose. Mirrors
        /// <see cref="Device_Management.Devices.BasisInput.ComputeUnscaledDeviceCoord"/>: the mover's
        /// space drag, seated mode's missing-height delta, and the height-mode grounding lift, all of
        /// which move the whole tracking space rather than any one device.
        /// </summary>
        public static float TrackingLift(bool isVR, float verticalOffset, bool seated, float seatedHeightDelta, float groundingOffset)
        {
            if (isVR == false)
            {
                return 0f;
            }

            float lift = verticalOffset;
            if (seated)
            {
                lift += seatedHeightDelta;
            }
            return lift + groundingOffset;
        }

        /// <summary>
        /// Where a point in raw tracking space (the origin the runtime reports boundary and device
        /// poses in) lands in player-local space, following the same lift then scale a device pose
        /// takes. Multiply by <see cref="BasisSdk.Players.BasisLocalPlayer.localToWorldMatrix"/> for
        /// the world position, which also carries the play-space flip.
        /// </summary>
        public static Vector3 TrackingToPlayerLocal(Vector3 trackingPoint, float lift, float deviceScale, Vector3 offsetPosition, Quaternion offsetRotation)
        {
            trackingPoint.y += lift;
            BasisCalibrationMath.ScaleDeviceCoord(trackingPoint, Quaternion.identity, deviceScale, offsetPosition, offsetRotation, out Vector3 scaled, out _);
            return scaled;
        }

        /// <summary>
        /// Centre, axis-aligned size, floor area and perimeter of a closed play-area outline. Area is
        /// the polygon's own (shoelace), not the bounding box's, so an L-shaped or clipped guardian
        /// reports the space it really has. Needs three points to be a shape at all.
        /// </summary>
        public static bool TryComputeBounds(IReadOnlyList<Vector3> points, out BasisPlayspaceBoundsMetrics metrics)
        {
            metrics = default;
            if (points == null || points.Count < 3)
            {
                return false;
            }

            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;
            float twiceArea = 0f;
            float perimeter = 0f;

            for (int Index = 0; Index < points.Count; Index++)
            {
                Vector3 current = points[Index];
                Vector3 next = points[(Index + 1) % points.Count];

                if (current.x < minX) minX = current.x;
                if (current.x > maxX) maxX = current.x;
                if (current.z < minZ) minZ = current.z;
                if (current.z > maxZ) maxZ = current.z;

                twiceArea += (current.x * next.z) - (next.x * current.z);
                perimeter += new Vector2(next.x - current.x, next.z - current.z).magnitude;
            }

            metrics.Valid = true;
            metrics.Center = new Vector3((minX + maxX) * 0.5f, points[0].y, (minZ + maxZ) * 0.5f);
            metrics.SizeX = maxX - minX;
            metrics.SizeZ = maxZ - minZ;
            metrics.Area = Mathf.Abs(twiceArea) * 0.5f;
            metrics.Perimeter = perimeter;
            return true;
        }

        /// <summary>Short readout token for a mover state.</summary>
        public static string StateLabel(BasisPlayspaceMoverState state)
        {
            switch (state)
            {
                case BasisPlayspaceMoverState.NotVR: return "desktop (VR only)";
                case BasisPlayspaceMoverState.Seated: return "seated";
                case BasisPlayspaceMoverState.AdminLocked: return "locked by admin";
                case BasisPlayspaceMoverState.MovementLocked: return "movement locked";
                case BasisPlayspaceMoverState.PointingAtUI: return "aiming at UI";
                case BasisPlayspaceMoverState.Idle: return "ready";
                case BasisPlayspaceMoverState.Dragging: return "dragging";
                case BasisPlayspaceMoverState.Rotating: return "rotating";
                case BasisPlayspaceMoverState.Scaling: return "scaling";
                default: return "off";
            }
        }
    }
}
