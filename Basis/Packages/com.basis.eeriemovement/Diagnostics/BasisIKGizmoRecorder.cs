using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
namespace Basis.IK
{
    public static class BasisIKGizmoPalette
    {
        public const uint White = 0xFFFFFFFFu, Red = 0xFF0000FFu, Green = 0xFF00FF00u, Blue = 0xFFFF0000u;
        public const uint Yellow = 0xFF00FFFFu, Cyan = 0xFFFFFF00u, Magenta = 0xFFFF00FFu, Orange = 0xFF0080FFu;
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
        public static uint From(Color color)
        {
            Color32 c = color;
            return Rgba(c.r, c.g, c.b, c.a);
        }
    }
    public struct BasisIKGizmoRecorder
    {
        public const int StageCount = 13, CircleSegments = 20, OverflowDraws = 0, OverflowLabels = 1, OverflowCount = 2;
        const float minMag = 1e-6f, sqrEpsilon = 1e-8f;
        public NativeList<BasisIKGizmoDraw> Draws;
        public NativeList<BasisIKGizmoLabel> Labels;
        public NativeArray<int> Overflow;
        public FixedList128Bytes<uint> StageColors;
        public int StageMask;
        public float LineWidth, PointSize, AxisLength;
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
            if (length <= minMag)
            {
                return;
            }
            Vector3 dir = direction / length, side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < sqrEpsilon)
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
            if (radius <= minMag || normal.sqrMagnitude < sqrEpsilon)
            {
                return;
            }
            Vector3 axis = normal.normalized, u = Vector3.Cross(axis, Vector3.up);
            if (u.sqrMagnitude < sqrEpsilon)
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
        public void Chain(BasisIKGizmoStage stage, ref BasisPoseStream stream, BasisBoneHandle from, BasisBoneHandle to, uint color)
        {
            if (!Wants(stage) || !stream.IsValid(from) || !stream.IsValid(to))
            {
                return;
            }
            Bone(stage, stream.GetPosition(from), stream.GetPosition(to), color);
        }
        public void BoneAxes(BasisIKGizmoStage stage, ref BasisPoseStream stream, BasisBoneHandle handle, float length)
        {
            if (!Wants(stage) || !stream.IsValid(handle))
            {
                return;
            }
            stream.GetPositionAndRotation(handle, out Vector3 position, out Quaternion rotation);
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
        public void Arc(BasisIKGizmoStage stage, Vector3 centre, Vector3 fromDirection, Vector3 toDirection, float radius, uint color)
        {
            if (!Wants(stage) || radius <= minMag) return;

            Vector3 a = fromDirection.normalized, b = toDirection.normalized;
            if (a.sqrMagnitude < 0.5f || b.sqrMagnitude < 0.5f) return;

            float sweep = Vector3.Angle(a, b);
            if (sweep <= 0.01f) return;

            Vector3 axis = Vector3.Cross(a, b);
            if (axis.sqrMagnitude < sqrEpsilon)
            {
                axis = Vector3.Cross(a, Vector3.up);
                if (axis.sqrMagnitude < sqrEpsilon) axis = Vector3.Cross(a, Vector3.right);
            }
            axis = axis.normalized;

            int steps = (int)math.clamp(sweep / 8f, 2f, 24f);
            Vector3 previous = centre + a * radius;
            for (int i = 1; i <= steps; i++)
            {
                Vector3 dir = Quaternion.AngleAxis(sweep * i / steps, axis) * a, next = centre + dir * radius;
                Push(stage, BasisIKGizmoKind.Line, previous, next, LineWidth, color);
                previous = next;
            }
        }
        public void Angle(BasisIKGizmoStage stage, Vector3 centre, Vector3 fromDirection, Vector3 toDirection, float radius, uint color)
        {
            if (!Wants(stage)) return;
            Arc(stage, centre, fromDirection, toDirection, radius, color);
            Line(stage, centre, centre + fromDirection.normalized * radius, color);
            Line(stage, centre, centre + toDirection.normalized * radius, color);

            if (!WantLabels) return;
            float sweep = Vector3.Angle(fromDirection, toDirection);
            FixedString64Bytes text = default;
            text.Append(sweep);
            Vector3 mid = fromDirection.normalized + toDirection.normalized;
            if (mid.sqrMagnitude < sqrEpsilon) mid = fromDirection;
            Label(stage, centre + mid.normalized * radius, text, color);
        }
        public void SwingCone(BasisIKGizmoStage stage, Vector3 apex, Vector3 axis, Vector3 u, Vector3 w, float limitU, float limitWPositive, float limitWNegative, float length, uint color)
        {
            if (!Wants(stage) || length <= minMag) return;

            Vector3 n = axis.normalized;
            if (n.sqrMagnitude < 0.5f) return;

            float lu = math.max(limitU, 0.01f), lwp = math.max(limitWPositive, 0.01f);
            float lwn = math.max(limitWNegative, 0.01f), step = math.PI * 2f / CircleSegments;
            Vector3 first = default, previous = default;
            for (int i = 0; i <= CircleSegments; i++)
            {
                float t = step * i, cu = math.cos(t), cw = math.sin(t), lw = cw >= 0f ? lwp : lwn, su = cu / lu;
                float sw = cw / lw, halfAngle = 1f / math.max(math.sqrt(su * su + sw * sw), 1e-4f);
                halfAngle = math.min(halfAngle, 179f) * math.TORADIANS;

                Vector3 d = u * cu + w * cw, rim = apex + (n * math.cos(halfAngle) + d * math.sin(halfAngle)) * length;

                if (i == 0)
                {
                    first = rim;
                    previous = rim;
                    Push(stage, BasisIKGizmoKind.Line, apex, rim, LineWidth, color);
                    continue;
                }
                Push(stage, BasisIKGizmoKind.Line, previous, rim, LineWidth, color);
                if ((i & 3) == 0)
                {
                    Push(stage, BasisIKGizmoKind.Line, apex, rim, LineWidth, color);
                }
                previous = rim;
            }
            Push(stage, BasisIKGizmoKind.Line, previous, first, LineWidth, color);
        }
        public void Cone(BasisIKGizmoStage stage, Vector3 apex, Vector3 axis, float halfAngleDeg, float length, uint color)
        {
            if (!Wants(stage)) return;
            Vector3 n = axis.normalized;
            if (n.sqrMagnitude < 0.5f) return;
            Basis(n, out Vector3 u, out Vector3 w);
            SwingCone(stage, apex, n, u, w, halfAngleDeg, halfAngleDeg, halfAngleDeg, length, color);
        }
        public void Sphere(BasisIKGizmoStage stage, Vector3 centre, float radius, uint color)
        {
            if (!Wants(stage)) return;
            Circle(stage, centre, Vector3.up, radius, color);
            Circle(stage, centre, Vector3.right, radius, color);
            Circle(stage, centre, Vector3.forward, radius, color);
        }
        public void Capsule(BasisIKGizmoStage stage, Vector3 a, Vector3 b, float radius, uint color)
        {
            if (!Wants(stage) || radius <= minMag) return;

            Vector3 axis = b - a;
            float height = axis.magnitude;
            Vector3 dir = height > minMag ? axis / height : Vector3.up;
            Basis(dir, out Vector3 u, out Vector3 w);

            Push(stage, BasisIKGizmoKind.Line, a + u * radius, b + u * radius, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, a - u * radius, b - u * radius, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, a + w * radius, b + w * radius, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, a - w * radius, b - w * radius, LineWidth, color);
            Circle(stage, a, dir, radius, color);
            Circle(stage, b, dir, radius, color);
        }
        public void Plane(BasisIKGizmoStage stage, Vector3 centre, Vector3 normal, float halfSize, uint color)
        {
            if (!Wants(stage) || halfSize <= minMag) return;
            Vector3 n = normal.normalized;
            if (n.sqrMagnitude < 0.5f) return;
            Basis(n, out Vector3 u, out Vector3 w);

            Vector3 c0 = centre + (u + w) * halfSize, c1 = centre + (u - w) * halfSize;
            Vector3 c2 = centre - (u + w) * halfSize, c3 = centre - (u - w) * halfSize;
            Push(stage, BasisIKGizmoKind.Line, c0, c1, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, c1, c2, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, c2, c3, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, c3, c0, LineWidth, color);
            Ray(stage, centre, n * halfSize, color);
        }
        public void Normal(BasisIKGizmoStage stage, Vector3 centre, Vector3 normal, float radius, uint color)
        {
            if (!Wants(stage) || radius <= minMag) return;
            Vector3 n = normal.normalized;
            if (n.sqrMagnitude < 0.5f) return;
            Circle(stage, centre, n, radius, color);
            Push(stage, BasisIKGizmoKind.Line, centre - n * (radius * 0.5f), centre + n * (radius * 0.5f), LineWidth, color);
        }
        public void Box(BasisIKGizmoStage stage, Vector3 centre, Quaternion rotation, Vector3 halfExtents, uint color)
        {
            if (!Wants(stage)) return;
            Vector3 x = rotation * Vector3.right * halfExtents.x, y = rotation * Vector3.up * halfExtents.y;
            Vector3 z = rotation * Vector3.forward * halfExtents.z, p000 = centre - x - y - z;
            Vector3 p100 = centre + x - y - z, p110 = centre + x + y - z, p010 = centre - x + y - z;
            Vector3 p001 = centre - x - y + z, p101 = centre + x - y + z, p111 = centre + x + y + z;
            Vector3 p011 = centre - x + y + z;

            Push(stage, BasisIKGizmoKind.Line, p000, p100, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, p100, p110, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, p110, p010, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, p010, p000, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, p001, p101, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, p101, p111, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, p111, p011, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, p011, p001, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, p000, p001, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, p100, p101, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, p110, p111, LineWidth, color);
            Push(stage, BasisIKGizmoKind.Line, p010, p011, LineWidth, color);
        }
        public static void Basis(Vector3 dir, out Vector3 u, out Vector3 w)
        {
            u = Vector3.Cross(dir, Vector3.up);
            if (u.sqrMagnitude < sqrEpsilon)
            {
                u = Vector3.Cross(dir, Vector3.right);
            }
            u = u.normalized;
            w = Vector3.Cross(dir, u).normalized;
        }
        public void Vector(Vector3 origin, Vector3 delta)
        {
            Ray(BasisIKGizmoStage.Scratch, origin, delta, StageColor(BasisIKGizmoStage.Scratch));
        }
        public void Vector(Vector3 origin, Vector3 delta, uint color)
        {
            Ray(BasisIKGizmoStage.Scratch, origin, delta, color);
        }
        public void Vector(Vector3 origin, Vector3 delta, Color color)
        {
            Ray(BasisIKGizmoStage.Scratch, origin, delta, BasisIKGizmoPalette.From(color));
        }
        public void Segment(Vector3 from, Vector3 to)
        {
            Line(BasisIKGizmoStage.Scratch, from, to, StageColor(BasisIKGizmoStage.Scratch));
        }
        public void Segment(Vector3 from, Vector3 to, uint color)
        {
            Line(BasisIKGizmoStage.Scratch, from, to, color);
        }
        public void Segment(Vector3 from, Vector3 to, Color color)
        {
            Line(BasisIKGizmoStage.Scratch, from, to, BasisIKGizmoPalette.From(color));
        }
        public void Marker(Vector3 position)
        {
            Point(BasisIKGizmoStage.Scratch, position, StageColor(BasisIKGizmoStage.Scratch));
        }
        public void Marker(Vector3 position, uint color)
        {
            Point(BasisIKGizmoStage.Scratch, position, color);
        }
        public void Marker(Vector3 position, Color color)
        {
            Point(BasisIKGizmoStage.Scratch, position, BasisIKGizmoPalette.From(color));
        }
        public void Frame(Vector3 origin, Quaternion rotation)
        {
            Axes(BasisIKGizmoStage.Scratch, origin, rotation, AxisLength);
        }
        public void Frame(Vector3 origin, Quaternion rotation, float length)
        {
            Axes(BasisIKGizmoStage.Scratch, origin, rotation, length);
        }
        public void Note(Vector3 position, in FixedString64Bytes text)
        {
            Label(BasisIKGizmoStage.Scratch, position, text, StageColor(BasisIKGizmoStage.Scratch));
        }
        public void Note(Vector3 position, in FixedString64Bytes text, Color color)
        {
            Label(BasisIKGizmoStage.Scratch, position, text, BasisIKGizmoPalette.From(color));
        }
        public void Note(Vector3 position, in FixedString64Bytes text, float value)
        {
            if (!WantLabels || !Wants(BasisIKGizmoStage.Scratch))
            {
                return;
            }
            FixedString64Bytes line = text;
            line.Append(' ');
            line.Append(value);
            Label(BasisIKGizmoStage.Scratch, position, line, StageColor(BasisIKGizmoStage.Scratch));
        }
        public void Compare(Vector3 origin, Vector3 a, Vector3 b)
        {
            Compare(origin, a, b, BasisIKGizmoPalette.Green, BasisIKGizmoPalette.Magenta);
        }
        public void Compare(Vector3 origin, Vector3 a, Vector3 b, uint colorA, uint colorB)
        {
            if (!Wants(BasisIKGizmoStage.Scratch))
            {
                return;
            }
            Ray(BasisIKGizmoStage.Scratch, origin, a, colorA);
            Ray(BasisIKGizmoStage.Scratch, origin, b, colorB);

            if (!WantLabels)
            {
                return;
            }
            FixedString64Bytes line = "angle ";
            line.Append(Vector3.Angle(a, b));
            Label(BasisIKGizmoStage.Scratch, origin + (a + b) * 0.5f, line, BasisIKGizmoPalette.White);
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
