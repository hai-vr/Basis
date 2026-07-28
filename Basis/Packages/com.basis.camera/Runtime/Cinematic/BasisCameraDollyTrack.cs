using System.Collections.Generic;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// The ordered chain of placed waypoints a Dolly shot rides. Spawning a point appends it to the
    /// end of the queue; its slot can be changed afterwards, which reshapes the path without moving
    /// anything in the world.
    /// </summary>
    public class BasisCameraDollyTrack
    {
        private const int SamplesPerSegment = 12;
        private const float LineWidth = 0.012f;
        private const string GizmoName = "CameraDollyTrack";

        private readonly List<BasisCameraDollyWaypoint> waypoints = new List<BasisCameraDollyWaypoint>();
        private readonly List<Vector3> points = new List<Vector3>();
        private readonly List<int> labelIds = new List<int>();

        private Vector3[] lineBuffer = System.Array.Empty<Vector3>();
        private int lineId = -1;
        private bool lineCreated;
        private bool lineVisible;

        public bool Looped;
        public bool Visible = true;
        public bool GridSnap;
        public float GridSize = 0.25f;

        public IReadOnlyList<BasisCameraDollyWaypoint> Waypoints => waypoints;

        /// <summary>World positions of the queue, rebuilt by <see cref="Refresh"/>.</summary>
        public IReadOnlyList<Vector3> Points => points;

        public int Count => waypoints.Count;

        /// <summary>A Dolly shot needs at least two points before there is a path to ride.</summary>
        public bool HasPath => waypoints.Count >= 2;

        /// <summary>Spawns a waypoint and appends it to the end of the queue.</summary>
        public BasisCameraDollyWaypoint AddWaypoint(Vector3 position, Quaternion rotation, float scale)
        {
            BasisCameraDollyWaypoint waypoint = BasisCameraDollyWaypoint.Spawn(position, rotation, scale, GridSnap, GridSize);
            waypoints.Add(waypoint);
            RebuildIndices();
            return waypoint;
        }

        public bool RemoveWaypointAt(int index)
        {
            if (index < 0 || index >= waypoints.Count)
            {
                return false;
            }

            BasisCameraDollyWaypoint waypoint = waypoints[index];
            waypoints.RemoveAt(index);
            if (waypoint != null)
            {
                Object.Destroy(waypoint.gameObject);
            }

            ReleaseLabel(waypoints.Count);
            RebuildIndices();
            return true;
        }

        /// <summary>
        /// Changes a waypoint's slot in the queue. This is the reorder behind the panel's position
        /// field: the marker stays exactly where it was placed, only its place in the path changes.
        /// </summary>
        public bool MoveWaypoint(int fromIndex, int toIndex)
        {
            if (!MoveInList(waypoints, fromIndex, toIndex))
            {
                return false;
            }
            RebuildIndices();
            return true;
        }

        /// <summary>
        /// Pure list reorder shared by the track and the shot list. Clamps the destination rather
        /// than rejecting it, so "move to slot 99" lands at the end instead of doing nothing.
        /// </summary>
        public static bool MoveInList<T>(IList<T> list, int fromIndex, int toIndex)
        {
            if (list == null || list.Count == 0)
            {
                return false;
            }
            if (fromIndex < 0 || fromIndex >= list.Count)
            {
                return false;
            }

            toIndex = Mathf.Clamp(toIndex, 0, list.Count - 1);
            if (toIndex == fromIndex)
            {
                return false;
            }

            T item = list[fromIndex];
            list.RemoveAt(fromIndex);
            list.Insert(toIndex, item);
            return true;
        }

        public void Clear()
        {
            for (int Index = 0; Index < waypoints.Count; Index++)
            {
                if (waypoints[Index] != null)
                {
                    Object.Destroy(waypoints[Index].gameObject);
                }
            }
            waypoints.Clear();
            points.Clear();
            ReleaseAllLabels();
            HideLine();
        }

        public int IndexOf(BasisCameraDollyWaypoint waypoint) => waypoints.IndexOf(waypoint);

        public BasisCameraDollyWaypoint GetWaypoint(int index)
            => index >= 0 && index < waypoints.Count ? waypoints[index] : null;

        /// <summary>
        /// Rebuilds the point cache from the live marker transforms and redraws the path. Called
        /// every frame because a grabbed waypoint moves the path under the shot as it is carried.
        /// </summary>
        public void Refresh(float scale, Quaternion labelFacing)
        {
            PruneDestroyed();

            points.Clear();
            for (int Index = 0; Index < waypoints.Count; Index++)
            {
                points.Add(waypoints[Index].Position);
            }

            if (!Visible)
            {
                HideVisuals();
                return;
            }

            for (int Index = 0; Index < waypoints.Count; Index++)
            {
                BasisCameraDollyWaypoint waypoint = waypoints[Index];
                waypoint.SetVisible(true);
                waypoint.SetScale(scale);
                waypoint.SetColor(ColorForIndex(Index, waypoints.Count));
            }

            UpdateLine();
            UpdateLabels(scale, labelFacing);
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
            if (!visible)
            {
                HideVisuals();
            }
        }

        public void SetGridSnap(bool enabled, float size)
        {
            GridSnap = enabled;
            GridSize = size;
            for (int Index = 0; Index < waypoints.Count; Index++)
            {
                if (waypoints[Index] != null)
                {
                    waypoints[Index].SetGridSnap(enabled, size);
                }
            }
        }

        /// <summary>Direction of travel, green at the head of the queue through red at the tail.</summary>
        public static Color ColorForIndex(int index, int count)
        {
            if (count <= 1)
            {
                return new Color(0.3f, 1f, 0.45f);
            }
            float t = index / (float)(count - 1);
            return Color.Lerp(new Color(0.3f, 1f, 0.45f), new Color(1f, 0.35f, 0.3f), t);
        }

        private void PruneDestroyed()
        {
            bool removed = false;
            for (int Index = waypoints.Count - 1; Index >= 0; Index--)
            {
                if (waypoints[Index] == null)
                {
                    waypoints.RemoveAt(Index);
                    removed = true;
                }
            }

            if (removed)
            {
                while (labelIds.Count > waypoints.Count)
                {
                    ReleaseLabel(labelIds.Count - 1);
                }
                RebuildIndices();
            }
        }

        private void RebuildIndices()
        {
            for (int Index = 0; Index < waypoints.Count; Index++)
            {
                if (waypoints[Index] != null)
                {
                    waypoints[Index].DisplayIndex = Index + 1;
                }
            }
        }

        private void UpdateLine()
        {
            if (!HasPath)
            {
                HideLine();
                return;
            }

            float max = BasisCameraSpline.MaxPosition(points.Count, Looped);
            int samples = Mathf.Max(2, Mathf.CeilToInt(max * SamplesPerSegment)) + 1;

            if (lineBuffer.Length != samples)
            {
                lineBuffer = new Vector3[samples];
            }

            float step = max / (samples - 1);
            for (int Index = 0; Index < samples; Index++)
            {
                lineBuffer[Index] = BasisCameraSpline.Evaluate(points, Index * step, Looped);
            }

            if (!lineCreated)
            {
                BasisGizmoManager.CreateLineGizmo(GizmoName, out lineId, lineBuffer, LineWidth, new Color(0.4f, 0.85f, 1f), Looped);
                lineCreated = true;
                lineVisible = true;
                return;
            }

            if (!lineVisible)
            {
                BasisGizmoManager.SetGizmoActive(lineId, true);
                lineVisible = true;
            }
            BasisGizmoManager.UpdateLineGizmo(lineId, lineBuffer);
        }

        private void UpdateLabels(float scale, Quaternion facing)
        {
            float labelScale = Mathf.Max(0.02f, 0.05f * scale);
            Vector3 lift = Vector3.up * (0.12f * scale);

            for (int Index = 0; Index < waypoints.Count; Index++)
            {
                Vector3 position = waypoints[Index].Position + lift;
                string text = (Index + 1).ToString();
                Color color = ColorForIndex(Index, waypoints.Count);

                if (Index >= labelIds.Count)
                {
                    BasisGizmoManager.CreateTextGizmo(GizmoName, out int newId, position, text, color);
                    labelIds.Add(newId);
                    continue;
                }

                BasisGizmoManager.SetGizmoActive(labelIds[Index], true);
                BasisGizmoManager.UpdateTextGizmo(labelIds[Index], position, facing, labelScale, text, color);
            }

            for (int Index = waypoints.Count; Index < labelIds.Count; Index++)
            {
                BasisGizmoManager.SetGizmoActive(labelIds[Index], false);
            }
        }

        private void HideVisuals()
        {
            for (int Index = 0; Index < waypoints.Count; Index++)
            {
                if (waypoints[Index] != null)
                {
                    waypoints[Index].SetVisible(false);
                }
            }
            HideLine();
            for (int Index = 0; Index < labelIds.Count; Index++)
            {
                BasisGizmoManager.SetGizmoActive(labelIds[Index], false);
            }
        }

        private void HideLine()
        {
            if (lineCreated && lineVisible)
            {
                BasisGizmoManager.SetGizmoActive(lineId, false);
                lineVisible = false;
            }
        }

        private void ReleaseLabel(int index)
        {
            if (index < 0 || index >= labelIds.Count)
            {
                return;
            }
            BasisGizmoManager.DestroyGizmo(labelIds[index]);
            labelIds.RemoveAt(index);
        }

        private void ReleaseAllLabels()
        {
            for (int Index = 0; Index < labelIds.Count; Index++)
            {
                BasisGizmoManager.DestroyGizmo(labelIds[Index]);
            }
            labelIds.Clear();
        }

        /// <summary>Destroys every marker and every gizmo the track owns. Called from camera teardown.</summary>
        public void Dispose()
        {
            Clear();
            if (lineCreated)
            {
                BasisGizmoManager.DestroyGizmo(lineId);
                lineCreated = false;
            }
        }
    }
}
