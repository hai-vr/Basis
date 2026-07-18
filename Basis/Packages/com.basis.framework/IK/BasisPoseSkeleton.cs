using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Jobs;

namespace UnityEngine.Animations.Rigging
{
    [BurstCompile]
    public struct BasisPoseGatherJob : IJobParallelForTransform
    {
        public NativeArray<float3> LocalPosition;
        public NativeArray<quaternion> LocalRotation;
        public NativeArray<float3> LocalScale;

        public void Execute(int index, TransformAccess transform)
        {
            transform.GetLocalPositionAndRotation(out Vector3 position, out Quaternion rotation);
            LocalPosition[index] = position;
            LocalRotation[index] = rotation;
            LocalScale[index] = transform.localScale;
        }
    }

    [BurstCompile]
    public struct BasisPoseScatterJob : IJobParallelForTransform
    {
        [ReadOnly] public NativeArray<float3> LocalPosition;
        [ReadOnly] public NativeArray<quaternion> LocalRotation;
        [ReadOnly] public NativeArray<int> WriteIndices;

        public void Execute(int index, TransformAccess transform)
        {
            int bone = WriteIndices[index];
            transform.SetLocalPositionAndRotation(LocalPosition[bone], LocalRotation[bone]);
        }
    }

    public sealed class BasisPoseSkeleton : IDisposable
    {
        public BasisPoseStream Stream;

        TransformAccessArray _access;
        TransformAccessArray _writeAccess;
        NativeArray<int> _writeIndices;
        Transform _anchor;
        Transform[] _ordered = Array.Empty<Transform>();
        readonly Dictionary<Transform, int> _lookup = new Dictionary<Transform, int>();
        bool _allocated;

        public bool IsCreated => _allocated;
        public int Count => _ordered.Length;
        public Transform Anchor => _anchor;

        public void Build(Transform root, IReadOnlyList<Transform> bones)
        {
            Dispose();

            var closure = new List<Transform>();
            var seen = new HashSet<Transform>();

            for (int i = 0; i < bones.Count; i++)
            {
                Transform bone = bones[i];
                if (bone == null)
                {
                    continue;
                }
                for (Transform walk = bone; walk != null; walk = walk.parent)
                {
                    if (!seen.Add(walk))
                    {
                        break;
                    }
                    closure.Add(walk);
                    if (walk == root)
                    {
                        break;
                    }
                }
            }

            if (closure.Count == 0)
            {
                return;
            }

            closure.Sort((a, b) => DepthOf(a, seen).CompareTo(DepthOf(b, seen)));

            _ordered = closure.ToArray();
            _lookup.Clear();
            for (int i = 0; i < _ordered.Length; i++)
            {
                _lookup[_ordered[i]] = i;
            }

            int count = _ordered.Length;
            var parent = new NativeArray<int>(count, Allocator.Persistent);
            for (int i = 0; i < count; i++)
            {
                Transform p = _ordered[i].parent;
                parent[i] = p != null && _lookup.TryGetValue(p, out int index) ? index : -1;
            }

            var bindLength = new NativeArray<float>(count, Allocator.Persistent);
            var translationFree = new NativeArray<byte>(count, Allocator.Persistent);
            for (int i = 0; i < count; i++)
            {
                bindLength[i] = _ordered[i].localPosition.magnitude;
                translationFree[i] = (byte)(parent[i] < 0 ? 1 : 0);
            }

            _anchor = _ordered[0].parent;

            Stream = new BasisPoseStream
            {
                LocalPosition = new NativeArray<float3>(count, Allocator.Persistent),
                LocalRotation = new NativeArray<quaternion>(count, Allocator.Persistent),
                LocalScale = new NativeArray<float3>(count, Allocator.Persistent),
                Parent = parent,
                BindLength = bindLength,
                TranslationFree = translationFree,
                AnchorPosition = float3.zero,
                AnchorRotation = quaternion.identity,
                AnchorScale = new float3(1f, 1f, 1f),
                Count = count,
            };

            _access = new TransformAccessArray(_ordered);

            var writable = new List<Transform>();
            var writableIndex = new List<int>();
            for (int i = 0; i < bones.Count; i++)
            {
                Transform bone = bones[i];
                if (bone != null && _lookup.TryGetValue(bone, out int index) && !writableIndex.Contains(index))
                {
                    writable.Add(bone);
                    writableIndex.Add(index);
                }
            }
            _writeAccess = new TransformAccessArray(writable.ToArray());
            _writeIndices = new NativeArray<int>(writableIndex.ToArray(), Allocator.Persistent);

            _allocated = true;
        }

        static int DepthOf(Transform transform, HashSet<Transform> closure)
        {
            int depth = 0;
            for (Transform walk = transform.parent; walk != null && closure.Contains(walk); walk = walk.parent)
            {
                depth++;
            }
            return depth;
        }

        public void SetTranslationFree(Transform bone)
        {
            if (_allocated && bone != null && _lookup.TryGetValue(bone, out int index))
            {
                Stream.TranslationFree[index] = 1;
            }
        }

        public BasisBoneHandle Bind(Transform bone)
        {
            if (bone != null && _lookup.TryGetValue(bone, out int index))
            {
                return BasisBoneHandle.FromIndex(index);
            }
            return BasisBoneHandle.Unbound;
        }

        public void SyncAnchor()
        {
            if (_anchor == null)
            {
                Stream.AnchorPosition = float3.zero;
                Stream.AnchorRotation = quaternion.identity;
                Stream.AnchorScale = new float3(1f, 1f, 1f);
                return;
            }
            _anchor.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
            Stream.AnchorPosition = position;
            Stream.AnchorRotation = rotation;
            Stream.AnchorScale = _anchor.lossyScale;
        }

        public JobHandle ScheduleGather(JobHandle dependency = default)
        {
            if (!_allocated)
            {
                return dependency;
            }
            SyncAnchor();
            return new BasisPoseGatherJob
            {
                LocalPosition = Stream.LocalPosition,
                LocalRotation = Stream.LocalRotation,
                LocalScale = Stream.LocalScale,
            }.ScheduleReadOnly(_access, 16, dependency);
        }

        public JobHandle ScheduleScatter(JobHandle dependency = default)
        {
            if (!_allocated)
            {
                return dependency;
            }
            return new BasisPoseScatterJob
            {
                LocalPosition = Stream.LocalPosition,
                LocalRotation = Stream.LocalRotation,
                WriteIndices = _writeIndices,
            }.Schedule(_writeAccess, dependency);
        }

        public Transform[] DebugNodes => _ordered;

        public bool IsWritable(int index)
        {
            for (int i = 0; i < _writeIndices.Length; i++)
            {
                if (_writeIndices[i] == index)
                {
                    return true;
                }
            }
            return false;
        }

        public string ValidateAgainstTransforms()
        {
            if (!_allocated)
            {
                return "skeleton not built";
            }

            float worstPosition = 0f;
            float worstAngle = 0f;
            string worstPositionBone = "none";
            string worstAngleBone = "none";

            for (int i = 0; i < _ordered.Length; i++)
            {
                Stream.GetWorldPositionAndRotation(i, out Vector3 position, out Quaternion rotation);
                _ordered[i].GetPositionAndRotation(out Vector3 actualPosition, out Quaternion actualRotation);

                float positionError = Vector3.Distance(position, actualPosition);
                if (positionError > worstPosition)
                {
                    worstPosition = positionError;
                    worstPositionBone = _ordered[i].name;
                }

                float angleError = Quaternion.Angle(rotation, actualRotation);
                if (angleError > worstAngle)
                {
                    worstAngle = angleError;
                    worstAngleBone = _ordered[i].name;
                }
            }

            return $"nodes={_ordered.Length} writable={_writeIndices.Length} anchor={(_anchor != null ? _anchor.name : "<none>")} " +
                   $"worstPos={worstPosition * 1000f:F3}mm ({worstPositionBone}) worstRot={worstAngle:F3}deg ({worstAngleBone})";
        }

        public void Dispose()
        {
            if (!_allocated)
            {
                return;
            }
            if (_access.isCreated)
            {
                _access.Dispose();
            }
            if (_writeAccess.isCreated)
            {
                _writeAccess.Dispose();
            }
            if (_writeIndices.IsCreated)
            {
                _writeIndices.Dispose();
            }
            if (Stream.LocalPosition.IsCreated)
            {
                Stream.LocalPosition.Dispose();
            }
            if (Stream.LocalRotation.IsCreated)
            {
                Stream.LocalRotation.Dispose();
            }
            if (Stream.LocalScale.IsCreated)
            {
                Stream.LocalScale.Dispose();
            }
            if (Stream.Parent.IsCreated)
            {
                Stream.Parent.Dispose();
            }
            if (Stream.BindLength.IsCreated)
            {
                Stream.BindLength.Dispose();
            }
            if (Stream.TranslationFree.IsCreated)
            {
                Stream.TranslationFree.Dispose();
            }
            Stream = default;
            _ordered = Array.Empty<Transform>();
            _lookup.Clear();
            _anchor = null;
            _allocated = false;
        }
    }
}
