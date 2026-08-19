using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.IK
{
    [Flags]
    public enum BasisIKGizmoStage
    {
        None = 0,
        Targets = 1 << 0,
        Spine = 1 << 1,
        Shoulders = 1 << 2,
        Legs = 1 << 3,
        Arms = 1 << 4,
        Toes = 1 << 5,
        Overrides = 1 << 6,
        Skeleton = 1 << 7,
    }

    public enum BasisIKGizmoKind : byte
    {
        Line = 0,
        Sphere = 1,
    }

    public struct BasisIKGizmoDraw
    {
        public Vector3 A;
        public Vector3 B;
        public uint Color;
        public float Size;
        public byte Stage;
        public BasisIKGizmoKind Kind;
    }

    public struct BasisIKGizmoLabel
    {
        public Vector3 Position;
        public uint Color;
        public byte Stage;
        public FixedString64Bytes Text;
    }

    public static class BasisIKGizmoPalette
    {
        public const uint White = 0xFFFFFFFFu;
        public const uint Red = 0xFF0000FFu;
        public const uint Green = 0xFF00FF00u;
        public const uint Blue = 0xFFFF0000u;
        public const uint Yellow = 0xFF00FFFFu;
        public const uint Cyan = 0xFFFFFF00u;
        public const uint Magenta = 0xFFFF00FFu;
        public const uint Orange = 0xFF0080FFu;
        public const uint Grey = 0xFF808080u;

        public static uint Rgba(byte r, byte g, byte b, byte a)
        {
            return r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);
        }

        public static byte R(uint packed) => (byte)(packed & 0xFFu);
        public static byte G(uint packed) => (byte)((packed >> 8) & 0xFFu);
        public static byte B(uint packed) => (byte)((packed >> 16) & 0xFFu);
        public static byte A(uint packed) => (byte)((packed >> 24) & 0xFFu);

        public static uint WithAlpha(uint packed, byte alpha)
        {
            return (packed & 0x00FFFFFFu) | ((uint)alpha << 24);
        }
    }

    /// <summary>
    /// Burst-safe draw queue for the FBIK solve. The solve is a scheduled job, so nothing inside it
    /// can touch BasisGizmoManager; call sites append plain line/sphere/label records here instead
    /// and the main thread replays them into pooled gizmos once the solve is joined
    /// (BasisIKSolveGizmos.Drain, from BasisLocalRigDriver.CompleteIKSolve).
    /// <para>
    /// Adding a visualization is one call anywhere inside the job — no main-thread counterpart, no
    /// gizmo ids to hold, no lifetime to manage. Every entry point early-outs on the stage mask, so
    /// a draw left in the solve costs one branch while its stage is switched off.
    /// </para>
    /// </summary>
    public struct BasisIKGizmoRecorder
    {
        public const int StageCount = 8;
        public const int CircleSegments = 20;

        public const int OverflowDraws = 0;
        public const int OverflowLabels = 1;
        public const int OverflowCount = 2;

        const float k_MinMag = 1e-6f;
        const float k_SqrEpsilon = 1e-8f;

        public NativeList<BasisIKGizmoDraw> Draws;
        public NativeList<BasisIKGizmoLabel> Labels;
        public NativeArray<int> Overflow;

        public FixedList128Bytes<uint> StageColors;

        public int StageMask;
        public float LineWidth;
        public float PointSize;
        public float AxisLength;
        public bool WantLabels;

        public bool IsCreated => Draws.IsCreated;

        public bool Wants(BasisIKGizmoStage stage)
        {
            return Draws.IsCreated && (StageMask & (int)stage) != 0;
        }

        public static int StageIndex(BasisIKGizmoStage stage)
        {
            return math.tzcnt((uint)stage);
        }

        public uint StageColor(BasisIKGizmoStage stage)
        {
            int index = StageIndex(stage);
            return (uint)index < (uint)StageColors.Length ? StageColors[index] : BasisIKGizmoPalette.White;
        }

        public void Clear()
        {
            if (Draws.IsCreated)
            {
                Draws.Clear();
            }
            if (Labels.IsCreated)
            {
                Labels.Clear();
            }
            if (Overflow.IsCreated)
            {
                for (int i = 0; i < Overflow.Length; i++)
                {
                    Overflow[i] = 0;
                }
            }
        }

        void Push(BasisIKGizmoStage stage, BasisIKGizmoKind kind, Vector3 a, Vector3 b, float size, uint color)
        {
            if (Draws.Length >= Draws.Capacity)
            {
                if (Overflow.IsCreated)
                {
                    Overflow[OverflowDraws] = Overflow[OverflowDraws] + 1;
                }
                return;
            }
            Draws.AddNoResize(new BasisIKGizmoDraw
            {
                A = a,
                B = b,
                Color = color,
                Size = size,
                Stage = (byte)StageIndex(stage),
                Kind = kind,
            });
        }

        public void Line(BasisIKGizmoStage stage, Vector3 from, Vector3 to)
        {
            if (!Wants(stage)) return;
            Push(stage, BasisIKGizmoKind.Line, from, to, LineWidth, StageColor(stage));
        }

        public void Line(BasisIKGizmoStage stage, Vector3 from, Vector3 to, uint color)
        {
            if (!Wants(stage)) return;
            Push(stage, BasisIKGizmoKind.Line, from, to, LineWidth, color);
        }

        public void Line(BasisIKGizmoStage stage, Vector3 from, Vector3 to, uint color, float width)
        {
            if (!Wants(stage)) return;
            Push(stage, BasisIKGizmoKind.Line, from, to, width, color);
        }

        public void Point(BasisIKGizmoStage stage, Vector3 position)
        {
            if (!Wants(stage)) return;
            Push(stage, BasisIKGizmoKind.Sphere, position, position, PointSize, StageColor(stage));
        }

        public void Point(BasisIKGizmoStage stage, Vector3 position, uint color)
        {
            if (!Wants(stage)) return;
            Push(stage, BasisIKGizmoKind.Sphere, position, position, PointSize, color);
        }

        public void Point(BasisIKGizmoStage stage, Vector3 position, uint color, float size)
        {
            if (!Wants(stage)) return;
            Push(stage, BasisIKGizmoKind.Sphere, position, position, size, color);
        }

        public void Bone(BasisIKGizmoStage stage, Vector3 from, Vector3 to)
        {
            if (!Wants(stage)) return;
            Bone(stage, from, to, StageColor(stage));
        }

        public void Bone(BasisIKGizmoStage stage, Vector3 from, Vector3 to, uint color)
        {
            if (!Wants(stage)) return;
            Push(stage, BasisIKGizmoKind.Line, from, to, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Sphere, from, from, PointSize, color);
        }

        public void Ray(BasisIKGizmoStage stage, Vector3 origin, Vector3 direction)
        {
            if (!Wants(stage)) return;
            Ray(stage, origin, direction, StageColor(stage));
        }

        public void Ray(BasisIKGizmoStage stage, Vector3 origin, Vector3 direction, uint color)
        {
            if (!Wants(stage)) return;
            Vector3 tip = origin + direction;
            Push(stage, BasisIKGizmoKind.Line, origin, tip, LineWidth, color);

            float length = direction.magnitude;
            if (length <= k_MinMag)
            {
                return;
            }
            Vector3 dir = direction / length;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < k_SqrEpsilon)
            {
                side = Vector3.Cross(dir, Vector3.right);
            }
            side = side.normalized * (length * 0.15f);
            Vector3 back = tip - dir * (length * 0.25f);
            Push(stage, BasisIKGizmoKind.Line, tip, back + side, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, tip, back - side, LineWidth, color);
        }

        public void Direction(BasisIKGizmoStage stage, Vector3 origin, Vector3 unitDirection, float length, uint color)
        {
            if (!Wants(stage)) return;
            Ray(stage, origin, unitDirection * length, color);
        }

        public void Axes(BasisIKGizmoStage stage, Vector3 origin, Quaternion rotation)
        {
            if (!Wants(stage)) return;
            Axes(stage, origin, rotation, AxisLength);
        }

        public void Axes(BasisIKGizmoStage stage, Vector3 origin, Quaternion rotation, float length)
        {
            if (!Wants(stage)) return;
            Push(stage, BasisIKGizmoKind.Line, origin, origin + rotation * Vector3.right * length, LineWidth, BasisIKGizmoPalette.Red);
            Push(stage, BasisIKGizmoKind.Line, origin, origin + rotation * Vector3.up * length, LineWidth, BasisIKGizmoPalette.Green);
            Push(stage, BasisIKGizmoKind.Line, origin, origin + rotation * Vector3.forward * length, LineWidth, BasisIKGizmoPalette.Blue);
        }

        public void Cross(BasisIKGizmoStage stage, Vector3 position, float size, uint color)
        {
            if (!Wants(stage)) return;
            Push(stage, BasisIKGizmoKind.Line, position - Vector3.right * size, position + Vector3.right * size, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, position - Vector3.up * size, position + Vector3.up * size, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, position - Vector3.forward * size, position + Vector3.forward * size, LineWidth, color);
        }

        public void Circle(BasisIKGizmoStage stage, Vector3 centre, Vector3 normal, float radius, uint color)
        {
            if (!Wants(stage)) return;
            if (radius <= k_MinMag || normal.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }
            Vector3 axis = normal.normalized;
            Vector3 u = Vector3.Cross(axis, Vector3.up);
            if (u.sqrMagnitude < k_SqrEpsilon)
            {
                u = Vector3.Cross(axis, Vector3.right);
            }
            u = u.normalized;
            Vector3 v = Vector3.Cross(axis, u) * radius;
            u *= radius;

            float step = math.PI * 2f / CircleSegments;
            Vector3 previous = centre + u;
            for (int i = 1; i <= CircleSegments; i++)
            {
                float t = step * i;
                Vector3 next = centre + u * math.cos(t) + v * math.sin(t);
                Push(stage, BasisIKGizmoKind.Line, previous, next, LineWidth, color);
                previous = next;
            }
        }

        public void Chain(BasisIKGizmoStage stage, BasisPoseStream stream, BasisBoneHandle from, BasisBoneHandle to, uint color)
        {
            if (!Wants(stage) || !from.IsValid(stream) || !to.IsValid(stream))
            {
                return;
            }
            Bone(stage, from.GetPosition(stream), to.GetPosition(stream), color);
        }

        public void BoneAxes(BasisIKGizmoStage stage, BasisPoseStream stream, BasisBoneHandle handle, float length)
        {
            if (!Wants(stage) || !handle.IsValid(stream))
            {
                return;
            }
            handle.GetPositionAndRotation(stream, out Vector3 position, out Quaternion rotation);
            Axes(stage, position, rotation, length);
        }

        public void Label(BasisIKGizmoStage stage, Vector3 position, in FixedString64Bytes text)
        {
            if (!WantLabels || !Wants(stage)) return;
            Label(stage, position, text, StageColor(stage));
        }

        public void Label(BasisIKGizmoStage stage, Vector3 position, in FixedString64Bytes text, uint color)
        {
            if (!WantLabels || !Wants(stage) || !Labels.IsCreated)
            {
                return;
            }
            if (Labels.Length >= Labels.Capacity)
            {
                if (Overflow.IsCreated)
                {
                    Overflow[OverflowLabels] = Overflow[OverflowLabels] + 1;
                }
                return;
            }
            Labels.AddNoResize(new BasisIKGizmoLabel
            {
                Position = position,
                Color = color,
                Stage = (byte)StageIndex(stage),
                Text = text,
            });
        }

        public void Create(int drawCapacity, int labelCapacity)
        {
            Dispose();
            Draws = new NativeList<BasisIKGizmoDraw>(drawCapacity, Allocator.Persistent);
            Labels = new NativeList<BasisIKGizmoLabel>(labelCapacity, Allocator.Persistent);
            Overflow = new NativeArray<int>(OverflowCount, Allocator.Persistent);
        }

        public void Dispose()
        {
            if (Draws.IsCreated) Draws.Dispose();
            if (Labels.IsCreated) Labels.Dispose();
            if (Overflow.IsCreated) Overflow.Dispose();
            Draws = default;
            Labels = default;
            Overflow = default;
        }
    }
}
