using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Jobs;
using UnityEngine;
namespace Basis.IK
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

    public sealed class BasisPoseSkeleton : IDisposable
    {
        public BasisPoseStream Stream;

        TransformAccessArray _access;
        NativeArray<int> _writeIndices;
        // The writable bones, parallel to _writeIndices, for ScatterNow's inline write loop.
        Transform[] _writableTransforms = Array.Empty<Transform>();
        NativeArray<float3> _authoredLocalPosition;
        NativeArray<float> _fitScale;
        Transform _anchor;
        Transform[] _ordered = Array.Empty<Transform>();
        readonly Dictionary<Transform, int> _lookup = new Dictionary<Transform, int>();
        bool _allocated;
        bool _fitActive;

        public bool IsCreated => _allocated;
        public int Count => _ordered.Length;
        public Transform Anchor => _anchor;
        public int NonFiniteRestCaptureCount { get; private set; }
        public string FirstNonFiniteRestBone { get; private set; }

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
            _authoredLocalPosition = new NativeArray<float3>(count, Allocator.Persistent);
            _fitScale = new NativeArray<float>(count, Allocator.Persistent);
            NonFiniteRestCaptureCount = 0;
            FirstNonFiniteRestBone = null;
            for (int i = 0; i < count; i++)
            {
                float3 authored = _ordered[i].localPosition;
                if (!IsFinite(authored))
                {
                    NonFiniteRestCaptureCount++;
                    FirstNonFiniteRestBone ??= _ordered[i].name;
                    authored = float3.zero;
                }
                _authoredLocalPosition[i] = authored;
                _fitScale[i] = 1f;
                bindLength[i] = math.length(authored);
                translationFree[i] = (byte)(parent[i] < 0 ? 1 : 0);
            }
            _fitActive = false;

            _anchor = _ordered[0].parent;

            Stream = new BasisPoseStream
            {
                LocalPosition = new NativeArray<float3>(count, Allocator.Persistent),
                LocalRotation = new NativeArray<quaternion>(count, Allocator.Persistent),
                LocalScale = new NativeArray<float3>(count, Allocator.Persistent),
                Parent = parent,
                BindLength = bindLength,
                TranslationFree = translationFree,
                WorldPositionCache = new NativeArray<float3>(count, Allocator.Persistent),
                WorldRotationCache = new NativeArray<quaternion>(count, Allocator.Persistent),
                WorldScaleCache = new NativeArray<float3>(count, Allocator.Persistent),
                WorldCacheStamp = new NativeArray<int>(count + 1, Allocator.Persistent),
                AnchorPosition = float3.zero,
                AnchorRotation = quaternion.identity,
                AnchorScale = new float3(1f, 1f, 1f),
                Count = count,
            };
            Stream.WorldCacheStamp[count] = 1;

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
            _writableTransforms = writable.ToArray();
            _writeIndices = new NativeArray<int>(writableIndex.ToArray(), Allocator.Persistent);

            _allocated = true;
            SeedStreamFromTransforms();
        }

        void SeedStreamFromTransforms()
        {
            SyncAnchor();
            for (int i = 0; i < _ordered.Length; i++)
            {
                _ordered[i].GetLocalPositionAndRotation(out Vector3 position, out Quaternion rotation);
                float3 localPosition = position;
                quaternion localRotation = rotation;
                float3 localScale = _ordered[i].localScale;
                Stream.LocalPosition[i] = IsFinite(localPosition) ? localPosition : _authoredLocalPosition[i];
                Stream.LocalRotation[i] = IsSaneRotation(localRotation) ? localRotation : quaternion.identity;
                Stream.LocalScale[i] = IsFinite(localScale) ? localScale : new float3(1f, 1f, 1f);
            }
            Stream.InvalidateWorldCache();
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

        public bool FitActive => _fitActive;

        public void SetFitScale(Transform bone, float scale)
        {
            if (!_allocated || bone == null || !_lookup.TryGetValue(bone, out int index))
            {
                return;
            }
            if (Stream.TranslationFree[index] != 0)
            {
                return;
            }

            float safe = scale > 0f && !float.IsNaN(scale) && !float.IsInfinity(scale) ? scale : 1f;
            _fitScale[index] = safe;
            Stream.BindLength[index] = math.length(_authoredLocalPosition[index]) * safe;

            if (!Mathf.Approximately(safe, 1f))
            {
                _fitActive = true;
            }
        }

        public void ResetFit()
        {
            if (!_allocated)
            {
                return;
            }
            for (int i = 0; i < _fitScale.Length; i++)
            {
                _fitScale[i] = 1f;
                Stream.BindLength[i] = math.length(_authoredLocalPosition[i]);
            }
            _fitActive = false;
        }

        static bool IsFinite(float3 value) => math.all(math.isfinite(value));

        static bool IsSaneRotation(quaternion value)
        {
            float lengthSq = math.lengthsq(value.value);
            return math.isfinite(lengthSq) && lengthSq > 1e-8f;
        }

        float3 FittedLocalPosition(int index)
        {
            float3 fitted = _authoredLocalPosition[index] * _fitScale[index];
            return IsFinite(fitted) ? fitted : float3.zero;
        }

        public void ApplyFit()
        {
            if (!_allocated || !_fitActive)
            {
                return;
            }
            for (int i = 0; i < _fitScale.Length; i++)
            {
                if (Mathf.Approximately(_fitScale[i], 1f))
                {
                    continue;
                }
                Stream.LocalPosition[i] = FittedLocalPosition(i);
            }
            Stream.InvalidateWorldCache();
        }

        public void WriteFittedLocalPositions()
        {
            if (!_allocated)
            {
                return;
            }
            for (int i = 0; i < _authoredLocalPosition.Length; i++)
            {
                Stream.LocalPosition[i] = FittedLocalPosition(i);
            }
            for (int i = 0; i < _writeIndices.Length; i++)
            {
                int bone = _writeIndices[i];
                Transform target = _ordered[bone];
                if (target != null)
                {
                    target.localPosition = FittedLocalPosition(bone);
                }
            }
            Stream.InvalidateWorldCache();
        }

        public float FitScaleOf(int index) => _fitScale.IsCreated && index >= 0 && index < _fitScale.Length ? _fitScale[index] : 1f;

        /// <summary>
        /// Fills target with the fitted rest local positions (authored × fit scale) for every node —
        /// what GatherNow would read for bones whose translation is never animated.
        /// </summary>
        public void CopyRestPositionsInto(NativeArray<float3> target)
        {
            if (!_allocated)
            {
                return;
            }
            for (int i = 0; i < _authoredLocalPosition.Length; i++)
            {
                target[i] = FittedLocalPosition(i);
            }
        }

        /// <summary>
        /// Re-reads the root node's live local pose into the stream. The root (the animator transform)
        /// is moved and scaled by systems outside the pose data — when the stream is filled from baked
        /// samples instead of GatherNow, this keeps that one node current.
        /// </summary>
        public void RefreshRootFromTransform()
        {
            if (!_allocated || _ordered.Length == 0 || _ordered[0] == null)
            {
                return;
            }
            Transform root = _ordered[0];
            root.GetLocalPositionAndRotation(out Vector3 position, out Quaternion rotation);
            Stream.LocalPosition[0] = position;
            Stream.LocalRotation[0] = rotation;
            Stream.LocalScale[0] = root.localScale;
            Stream.InvalidateWorldCache();
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
                Stream.InvalidateWorldCache();
                return;
            }
            _anchor.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
            Stream.AnchorPosition = position;
            Stream.AnchorRotation = rotation;
            Stream.AnchorScale = _anchor.lossyScale;
            Stream.InvalidateWorldCache();
        }

        /// <summary>
        /// Gathers the skeleton's local pose into the stream on the calling thread. The gather's
        /// only consumer runs the IK job inline immediately after, so a parallel dispatch here
        /// bought nothing — its fence was paid on the very next line.
        /// </summary>
        public void GatherNow()
        {
            if (!_allocated)
            {
                return;
            }
            SyncAnchor();
            // A destroyed bone silently compacts the access array (same hazard ScatterNow
            // documents) — the inline job would then write shifted rows into the stream.
            // Gather on the main thread by stable index until the next Build.
            if (!_access.isCreated || _access.length != _ordered.Length)
            {
                for (int i = 0; i < _ordered.Length; i++)
                {
                    Transform bone = _ordered[i];
                    if (bone == null)
                    {
                        continue;
                    }
                    bone.GetLocalPositionAndRotation(out Vector3 position, out Quaternion rotation);
                    Stream.LocalPosition[i] = position;
                    Stream.LocalRotation[i] = rotation;
                    Stream.LocalScale[i] = bone.localScale;
                }
                Stream.InvalidateWorldCache();
                return;
            }
            new BasisPoseGatherJob
            {
                LocalPosition = Stream.LocalPosition,
                LocalRotation = Stream.LocalRotation,
                LocalScale = Stream.LocalScale,
            }.RunReadOnly(_access);
            Stream.InvalidateWorldCache();
        }

        /// <summary>
        /// Writes the solved local pose back to the writable bones on the calling thread. Same
        /// zero-window story as <see cref="GatherNow"/> — and transform writes have no inline job
        /// path at all, so this is a plain loop: a dozen SetLocalPositionAndRotation calls cost
        /// less than the write job's dispatch and fence did. Indexing directly also survives a
        /// destroyed bone, where the TransformAccessArray silently compacted and shifted every
        /// later write onto the wrong transform.
        /// </summary>
        public void ScatterNow()
        {
            if (!_allocated)
            {
                return;
            }
            for (int i = 0; i < _writableTransforms.Length; i++)
            {
                Transform bone = _writableTransforms[i];
                if (bone == null)
                {
                    continue;
                }
                int index = _writeIndices[i];
                bone.SetLocalPositionAndRotation(Stream.LocalPosition[index], Stream.LocalRotation[index]);
            }
        }

        public Transform[] DebugNodes => _ordered;

        public float BindLengthOf(int index) => Stream.BindLength.IsCreated ? Stream.BindLength[index] : 0f;

        public bool TranslationFreeOf(int index) => Stream.TranslationFree.IsCreated && Stream.TranslationFree[index] != 0;

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
            if (_writeIndices.IsCreated)
            {
                _writeIndices.Dispose();
            }
            if (_authoredLocalPosition.IsCreated)
            {
                _authoredLocalPosition.Dispose();
            }
            if (_fitScale.IsCreated)
            {
                _fitScale.Dispose();
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
            if (Stream.WorldPositionCache.IsCreated)
            {
                Stream.WorldPositionCache.Dispose();
            }
            if (Stream.WorldRotationCache.IsCreated)
            {
                Stream.WorldRotationCache.Dispose();
            }
            if (Stream.WorldScaleCache.IsCreated)
            {
                Stream.WorldScaleCache.Dispose();
            }
            if (Stream.WorldCacheStamp.IsCreated)
            {
                Stream.WorldCacheStamp.Dispose();
            }
            Stream = default;
            _ordered = Array.Empty<Transform>();
            _lookup.Clear();
            _anchor = null;
            _allocated = false;
            _fitActive = false;
        }
    }
}
