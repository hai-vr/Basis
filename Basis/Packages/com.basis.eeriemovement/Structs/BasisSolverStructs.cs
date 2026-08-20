using System.Runtime.CompilerServices;
using Basis.Scripts.Common;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;
using System;
using Unity.Mathematics;
namespace Basis.IK
{
    public struct BasisArmSlotState
    {
        public Vector3 HintBend, HintAxis, HintDrag;
        public Quaternion HintBodyRot;
        public float HintReach;
        public bool HintSeeded;
        public Vector3 PoleDir;
        public Quaternion PoleRot;
        public bool PoleValid;
        public int Collided, GuardSide;
    }
    public struct BasisLegSlotState
    {
        public BasisSwivelFilterState Swivel;
        public bool SwivelSeeded;
    }
    public struct BasisChestSpringState
    {
        public Vector3 Pos, Vel;
        public bool Seeded;
    }
    public struct BasisIKGizmoDraw
    {
        public Vector3 A, B;
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
}
