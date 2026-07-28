using System;
using System.Collections.Generic;
using Basis.Scripts.BasisSdk;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds a <see cref="BasisImposterPayload"/> for an avatar at bundle build time.
///
/// Pipeline: force the runtime T-pose → snapshot every active renderer into animator-root
/// space with skin weights collapsed onto the core humanoid bones → QEM-decimate to a small
/// triangle budget → unwrap → bake the avatar's real rendered appearance into a small atlas
/// via multi-view capture → serialize. The avatar's pose and position are restored afterwards,
/// so the build clone ships unchanged.
///
/// The captured skeleton uses the same T-pose the runtime bone system calibrates against
/// (Animated TPose.controller), which is what keeps the networked per-bone deltas
/// bit-compatible between the real avatar and the imposter.
/// </summary>
public static class BasisImposterGenerator
{
    public static int TargetTriangleCount = 1500;
    public static int AtlasSize = 256;
    public static int CaptureSize = 1024;

    /// <summary>Matches BasisPlayerFactory.TPose so build-time and runtime T-poses agree exactly.</summary>
    public const string TposeControllerPath = "Assets/Animator/Animated TPose.controller";

    /// <summary>
    /// Core humanoid bones the imposter keeps, ordered parents-first. Fingers, toes, eyes and
    /// jaw collapse into their nearest ancestor here — they are sub-pixel at imposter range and
    /// dropping them keeps the runtime skeleton around 20 transforms per player.
    /// </summary>
    private static readonly HumanBodyBones[] CoreBones =
    {
        HumanBodyBones.Hips,
        HumanBodyBones.Spine,
        HumanBodyBones.Chest,
        HumanBodyBones.UpperChest,
        HumanBodyBones.Neck,
        HumanBodyBones.Head,
        HumanBodyBones.LeftShoulder,
        HumanBodyBones.RightShoulder,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftHand,
        HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.RightLowerLeg,
        HumanBodyBones.LeftFoot,
        HumanBodyBones.RightFoot,
    };

    private static HumanBodyBones[] ParentChain(HumanBodyBones bone)
    {
        switch (bone)
        {
            case HumanBodyBones.Spine: return new[] { HumanBodyBones.Hips };
            case HumanBodyBones.Chest: return new[] { HumanBodyBones.Spine, HumanBodyBones.Hips };
            case HumanBodyBones.UpperChest: return new[] { HumanBodyBones.Chest, HumanBodyBones.Spine, HumanBodyBones.Hips };
            case HumanBodyBones.Neck:
            case HumanBodyBones.LeftShoulder:
            case HumanBodyBones.RightShoulder:
                return new[] { HumanBodyBones.UpperChest, HumanBodyBones.Chest, HumanBodyBones.Spine, HumanBodyBones.Hips };
            case HumanBodyBones.Head: return new[] { HumanBodyBones.Neck, HumanBodyBones.UpperChest, HumanBodyBones.Chest, HumanBodyBones.Spine, HumanBodyBones.Hips };
            case HumanBodyBones.LeftUpperArm: return new[] { HumanBodyBones.LeftShoulder, HumanBodyBones.UpperChest, HumanBodyBones.Chest, HumanBodyBones.Spine, HumanBodyBones.Hips };
            case HumanBodyBones.RightUpperArm: return new[] { HumanBodyBones.RightShoulder, HumanBodyBones.UpperChest, HumanBodyBones.Chest, HumanBodyBones.Spine, HumanBodyBones.Hips };
            case HumanBodyBones.LeftLowerArm: return new[] { HumanBodyBones.LeftUpperArm };
            case HumanBodyBones.RightLowerArm: return new[] { HumanBodyBones.RightUpperArm };
            case HumanBodyBones.LeftHand: return new[] { HumanBodyBones.LeftLowerArm };
            case HumanBodyBones.RightHand: return new[] { HumanBodyBones.RightLowerArm };
            case HumanBodyBones.LeftUpperLeg:
            case HumanBodyBones.RightUpperLeg:
                return new[] { HumanBodyBones.Hips };
            case HumanBodyBones.LeftLowerLeg: return new[] { HumanBodyBones.LeftUpperLeg };
            case HumanBodyBones.RightLowerLeg: return new[] { HumanBodyBones.RightUpperLeg };
            case HumanBodyBones.LeftFoot: return new[] { HumanBodyBones.LeftLowerLeg };
            case HumanBodyBones.RightFoot: return new[] { HumanBodyBones.RightLowerLeg };
            default: return Array.Empty<HumanBodyBones>();
        }
    }

    public sealed class ImposterSkeleton
    {
        public readonly List<HumanBodyBones> Bones = new List<HumanBodyBones>();
        public readonly List<int> ParentIndex = new List<int>();
        public readonly List<Transform> Transforms = new List<Transform>();
        public readonly List<Vector3> RootSpacePosition = new List<Vector3>();
        public readonly List<Quaternion> RootSpaceRotation = new List<Quaternion>();
        public readonly List<Vector3> RestLocalPosition = new List<Vector3>();
        public readonly List<Quaternion> RestLocalRotation = new List<Quaternion>();
        public readonly Dictionary<Transform, int> TransformToBone = new Dictionary<Transform, int>();
        public int Count => Bones.Count;
    }

    public static string GenerateBase64(BasisAvatar avatar)
    {
        BasisImposterPayload payload = Generate(avatar);
        return payload?.SerializeToBase64();
    }

    public static BasisImposterPayload Generate(BasisAvatar avatar)
    {
        if (avatar == null || avatar.Animator == null || avatar.Animator.avatar == null || !avatar.Animator.avatar.isHuman)
        {
            Debug.LogWarning("Imposter generation skipped: avatar is not humanoid.");
            return null;
        }

        Animator animator = avatar.Animator;
        Transform root = animator.transform;
        double startTime = EditorApplication.timeSinceStartup;

        TransformPoseSnapshot poseSnapshot = TransformPoseSnapshot.Capture(root);
        RuntimeAnimatorController savedController = animator.runtimeAnimatorController;
        try
        {
            // Park the clone on an isolated island so multi-view captures see nothing else.
            root.position = new Vector3(4096f, 4096f, 4096f);

            ApplyTPose(animator);

            ImposterSkeleton skeleton = CaptureSkeleton(animator, root);
            if (skeleton.Count == 0)
            {
                return null;
            }

            SnapshotSoup soup = SnapshotGeometry(animator, root, skeleton);
            if (soup.Indices.Count < 3)
            {
                Debug.LogWarning("Imposter generation skipped: no triangle geometry found.");
                return null;
            }

            BasisImposterMeshSimplifier.Simplify(soup.Positions, soup.BoneA, soup.BoneB, soup.WeightA, soup.Indices, TargetTriangleCount);

            Mesh unwrapped = BuildUnwrappedMesh(soup, out byte[] boneA, out byte[] boneB, out byte[] weightA);
            try
            {
                Vector3[] positions = unwrapped.vertices;
                Vector3[] normals = unwrapped.normals;
                Vector2[] uv = unwrapped.uv;
                int[] indices = unwrapped.triangles;
                if (positions.Length == 0 || positions.Length > BasisImposterPayload.MaxVertices || indices.Length == 0)
                {
                    Debug.LogWarning($"Imposter generation skipped: decimated mesh out of range ({positions.Length} verts).");
                    return null;
                }

                BasisImposterPayload.ImposterTexture[] textures = BasisImposterAtlasBaker.Bake(
                    root, unwrapped, positions, normals, uv, indices, AtlasSize, CaptureSize);
                if (textures == null || textures.Length == 0)
                {
                    Debug.LogWarning("Imposter generation skipped: atlas bake failed.");
                    return null;
                }

                BasisImposterPayload payload = AssemblePayload(avatar, root, skeleton, positions, normals, uv, indices, boneA, boneB, weightA, textures);
                double elapsed = EditorApplication.timeSinceStartup - startTime;
                Debug.Log($"Imposter generated: {indices.Length / 3} triangles, {positions.Length} vertices, {skeleton.Count} bones, {AtlasSize}px atlas, {elapsed:0.00}s.");
                return payload;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(unwrapped);
            }
        }
        finally
        {
            animator.runtimeAnimatorController = savedController;
            poseSnapshot.Restore();
        }
    }

    private static void ApplyTPose(Animator animator)
    {
        RuntimeAnimatorController tpose = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(TposeControllerPath);
        if (tpose != null)
        {
            animator.runtimeAnimatorController = tpose;
            animator.Update(0f);
            animator.Update(0.02f);
            return;
        }

        // No controller asset in this project — muscle-space zero is the same canonical T-pose.
        HumanPoseHandler handler = new HumanPoseHandler(animator.avatar, animator.transform);
        try
        {
            HumanPose pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            for (int i = 0; i < pose.muscles.Length; i++)
            {
                pose.muscles[i] = 0f;
            }
            handler.SetHumanPose(ref pose);
        }
        finally
        {
            handler.Dispose();
        }
    }

    private static ImposterSkeleton CaptureSkeleton(Animator animator, Transform root)
    {
        ImposterSkeleton skeleton = new ImposterSkeleton();
        Matrix4x4 rootWorldToLocal = root.worldToLocalMatrix;
        Quaternion rootRotationInverse = Quaternion.Inverse(root.rotation);

        Dictionary<HumanBodyBones, int> boneToIndex = new Dictionary<HumanBodyBones, int>();
        for (int i = 0; i < CoreBones.Length; i++)
        {
            HumanBodyBones bone = CoreBones[i];
            Transform boneTransform = animator.GetBoneTransform(bone);
            if (boneTransform == null)
            {
                continue;
            }

            int parentIndex = -1;
            HumanBodyBones[] chain = ParentChain(bone);
            for (int c = 0; c < chain.Length; c++)
            {
                if (boneToIndex.TryGetValue(chain[c], out int found))
                {
                    parentIndex = found;
                    break;
                }
            }
            if (parentIndex < 0 && bone != HumanBodyBones.Hips)
            {
                parentIndex = 0;
            }

            Vector3 rootPos = rootWorldToLocal.MultiplyPoint3x4(boneTransform.position);
            Quaternion rootRot = rootRotationInverse * boneTransform.rotation;

            int index = skeleton.Count;
            skeleton.Bones.Add(bone);
            skeleton.ParentIndex.Add(parentIndex);
            skeleton.Transforms.Add(boneTransform);
            skeleton.RootSpacePosition.Add(rootPos);
            skeleton.RootSpaceRotation.Add(rootRot);
            if (parentIndex < 0)
            {
                skeleton.RestLocalPosition.Add(rootPos);
                skeleton.RestLocalRotation.Add(rootRot);
            }
            else
            {
                Quaternion parentInverse = Quaternion.Inverse(skeleton.RootSpaceRotation[parentIndex]);
                skeleton.RestLocalPosition.Add(parentInverse * (rootPos - skeleton.RootSpacePosition[parentIndex]));
                skeleton.RestLocalRotation.Add(parentInverse * rootRot);
            }
            boneToIndex[bone] = index;
            skeleton.TransformToBone[boneTransform] = index;
        }
        return skeleton;
    }

    public sealed class SnapshotSoup
    {
        public readonly List<Vector3> Positions = new List<Vector3>(65536);
        public readonly List<byte> BoneA = new List<byte>(65536);
        public readonly List<byte> BoneB = new List<byte>(65536);
        public readonly List<byte> WeightA = new List<byte>(65536);
        public readonly List<int> Indices = new List<int>(196608);
    }

    private static SnapshotSoup SnapshotGeometry(Animator animator, Transform root, ImposterSkeleton skeleton)
    {
        SnapshotSoup soup = new SnapshotSoup();
        Matrix4x4 rootWorldToLocal = root.worldToLocalMatrix;
        Dictionary<Transform, int> ancestorCache = new Dictionary<Transform, int>();
        float[] weightScratch = new float[skeleton.Count];
        int[] touchedScratch = new int[8];

        Renderer[] renderers = animator.GetComponentsInChildren<Renderer>(false);
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            if (renderer == null || !renderer.enabled || renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
            {
                continue;
            }

            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                AppendSkinnedMesh(soup, skinned, root, rootWorldToLocal, skeleton, ancestorCache, weightScratch, touchedScratch);
            }
            else if (renderer is MeshRenderer meshRenderer)
            {
                MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    AppendRigidMesh(soup, filter.sharedMesh, meshRenderer.transform, root, rootWorldToLocal, skeleton, ancestorCache);
                }
            }
        }
        return soup;
    }

    private static int ResolveAncestorBone(Transform transform, Transform root, ImposterSkeleton skeleton, Dictionary<Transform, int> cache)
    {
        if (transform == null)
        {
            return 0;
        }
        if (cache.TryGetValue(transform, out int cached))
        {
            return cached;
        }
        int result = 0;
        Transform current = transform;
        while (current != null)
        {
            if (skeleton.TransformToBone.TryGetValue(current, out int boneIndex))
            {
                result = boneIndex;
                break;
            }
            if (current == root)
            {
                break;
            }
            current = current.parent;
        }
        cache[transform] = result;
        return result;
    }

    private static void AppendSkinnedMesh(SnapshotSoup soup, SkinnedMeshRenderer skinned, Transform root, Matrix4x4 rootWorldToLocal,
        ImposterSkeleton skeleton, Dictionary<Transform, int> ancestorCache, float[] weightScratch, int[] touchedScratch)
    {
        Mesh mesh = skinned.sharedMesh;
        Transform[] bones = skinned.bones;
        if (bones == null || bones.Length == 0)
        {
            AppendRigidMesh(soup, mesh, skinned.rootBone != null ? skinned.rootBone : skinned.transform, root, rootWorldToLocal, skeleton, ancestorCache);
            return;
        }

        Vector3[] vertices = mesh.vertices;
        ApplyActiveBlendShapes(skinned, mesh, vertices);

        Matrix4x4[] bindposes = mesh.bindposes;
        int boneCount = Mathf.Min(bones.Length, bindposes.Length);
        Matrix4x4[] skinMatrices = new Matrix4x4[boneCount];
        int[] boneToImposter = new int[boneCount];
        for (int b = 0; b < boneCount; b++)
        {
            skinMatrices[b] = bones[b] != null ? bones[b].localToWorldMatrix * bindposes[b] : Matrix4x4.identity;
            boneToImposter[b] = ResolveAncestorBone(bones[b] != null ? bones[b] : skinned.transform, root, skeleton, ancestorCache);
        }

        var bonesPerVertex = mesh.GetBonesPerVertex();
        var allWeights = mesh.GetAllBoneWeights();
        if (bonesPerVertex.Length != vertices.Length)
        {
            AppendRigidMesh(soup, mesh, skinned.transform, root, rootWorldToLocal, skeleton, ancestorCache);
            return;
        }

        int vertexBase = soup.Positions.Count;
        int weightCursor = 0;
        for (int v = 0; v < vertices.Length; v++)
        {
            int influenceCount = bonesPerVertex[v];
            Vector3 world = Vector3.zero;
            int touchedCount = 0;
            float totalWeight = 0f;
            for (int i = 0; i < influenceCount; i++)
            {
                BoneWeight1 weight = allWeights[weightCursor++];
                if (weight.boneIndex < 0 || weight.boneIndex >= boneCount || weight.weight <= 0f)
                {
                    continue;
                }
                world += skinMatrices[weight.boneIndex].MultiplyPoint3x4(vertices[v]) * weight.weight;
                totalWeight += weight.weight;

                int imposterBone = boneToImposter[weight.boneIndex];
                if (weightScratch[imposterBone] == 0f && touchedCount < touchedScratch.Length)
                {
                    touchedScratch[touchedCount++] = imposterBone;
                }
                weightScratch[imposterBone] += weight.weight;
            }

            if (totalWeight <= 1e-6f)
            {
                world = skinned.transform.localToWorldMatrix.MultiplyPoint3x4(vertices[v]);
                weightScratch[boneToImposter.Length > 0 ? boneToImposter[0] : 0] = 1f;
                if (touchedCount == 0 && touchedScratch.Length > 0)
                {
                    touchedScratch[touchedCount++] = boneToImposter.Length > 0 ? boneToImposter[0] : 0;
                }
            }
            else if (totalWeight < 0.999f)
            {
                world /= totalWeight;
            }

            // Keep the two heaviest collapsed influences.
            int bestBone = 0, secondBone = 0;
            float bestWeight = -1f, secondWeight = -1f;
            for (int t = 0; t < touchedCount; t++)
            {
                int bone = touchedScratch[t];
                float w = weightScratch[bone];
                if (w > bestWeight)
                {
                    secondBone = bestBone; secondWeight = bestWeight;
                    bestBone = bone; bestWeight = w;
                }
                else if (w > secondWeight)
                {
                    secondBone = bone; secondWeight = w;
                }
                weightScratch[bone] = 0f;
            }
            if (bestWeight <= 0f)
            {
                bestBone = 0; bestWeight = 1f; secondBone = 0; secondWeight = 0f;
            }
            if (secondWeight < 0f)
            {
                secondBone = bestBone; secondWeight = 0f;
            }

            float normalized = bestWeight / (bestWeight + secondWeight);
            soup.Positions.Add(rootWorldToLocal.MultiplyPoint3x4(world));
            soup.BoneA.Add((byte)bestBone);
            soup.BoneB.Add((byte)secondBone);
            soup.WeightA.Add((byte)Mathf.Clamp(Mathf.RoundToInt(normalized * 255f), 0, 255));
        }

        AppendTriangles(soup, mesh, vertexBase);
    }

    private static void ApplyActiveBlendShapes(SkinnedMeshRenderer skinned, Mesh mesh, Vector3[] vertices)
    {
        int shapeCount = mesh.blendShapeCount;
        if (shapeCount == 0)
        {
            return;
        }
        Vector3[] deltaScratch = null;
        for (int s = 0; s < shapeCount; s++)
        {
            float weight = skinned.GetBlendShapeWeight(s);
            if (Mathf.Abs(weight) < 0.001f)
            {
                continue;
            }
            deltaScratch ??= new Vector3[vertices.Length];
            int frame = mesh.GetBlendShapeFrameCount(s) - 1;
            mesh.GetBlendShapeFrameVertices(s, frame, deltaScratch, null, null);
            float frameWeight = mesh.GetBlendShapeFrameWeight(s, frame);
            float amount = frameWeight > 0f ? weight / frameWeight : weight * 0.01f;
            for (int v = 0; v < vertices.Length; v++)
            {
                vertices[v] += deltaScratch[v] * amount;
            }
        }
    }

    private static void AppendRigidMesh(SnapshotSoup soup, Mesh mesh, Transform meshTransform, Transform root, Matrix4x4 rootWorldToLocal,
        ImposterSkeleton skeleton, Dictionary<Transform, int> ancestorCache)
    {
        int bone = ResolveAncestorBone(meshTransform, root, skeleton, ancestorCache);
        Matrix4x4 toRoot = rootWorldToLocal * meshTransform.localToWorldMatrix;
        Vector3[] vertices = mesh.vertices;
        int vertexBase = soup.Positions.Count;
        for (int v = 0; v < vertices.Length; v++)
        {
            soup.Positions.Add(toRoot.MultiplyPoint3x4(vertices[v]));
            soup.BoneA.Add((byte)bone);
            soup.BoneB.Add((byte)bone);
            soup.WeightA.Add(255);
        }
        AppendTriangles(soup, mesh, vertexBase);
    }

    private static void AppendTriangles(SnapshotSoup soup, Mesh mesh, int vertexBase)
    {
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            if (mesh.GetTopology(sub) != MeshTopology.Triangles)
            {
                continue;
            }
            int[] indices = mesh.GetTriangles(sub);
            for (int i = 0; i < indices.Length; i++)
            {
                soup.Indices.Add(vertexBase + indices[i]);
            }
        }
    }

    private static Mesh BuildUnwrappedMesh(SnapshotSoup soup, out byte[] boneA, out byte[] boneB, out byte[] weightA)
    {
        Mesh mesh = new Mesh
        {
            hideFlags = HideFlags.HideAndDontSave,
            indexFormat = soup.Positions.Count > 65534 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16,
        };
        mesh.SetVertices(soup.Positions);
        mesh.SetTriangles(soup.Indices, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // The unwrap can split vertices along UV seams. Positions were welded during
        // simplification, so an exact-position lookup maps every split copy back to its
        // source vertex for the bone attributes.
        Dictionary<Vector3, int> positionToSource = new Dictionary<Vector3, int>(soup.Positions.Count);
        for (int i = 0; i < soup.Positions.Count; i++)
        {
            positionToSource[soup.Positions[i]] = i;
        }

        UnwrapParam.SetDefaults(out UnwrapParam unwrapParam);
        unwrapParam.packMargin = 4f / AtlasSize;
        Unwrapping.GenerateSecondaryUVSet(mesh, unwrapParam);

        Vector3[] vertices = mesh.vertices;
        Vector2[] uv2 = mesh.uv2;
        boneA = new byte[vertices.Length];
        boneB = new byte[vertices.Length];
        weightA = new byte[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            int source = positionToSource.TryGetValue(vertices[i], out int index) ? index : Mathf.Min(i, soup.Positions.Count - 1);
            boneA[i] = soup.BoneA[source];
            boneB[i] = soup.BoneB[source];
            weightA[i] = soup.WeightA[source];
        }

        mesh.uv = uv2;
        mesh.RecalculateNormals();
        return mesh;
    }

    private static BasisImposterPayload AssemblePayload(BasisAvatar avatar, Transform root, ImposterSkeleton skeleton,
        Vector3[] positions, Vector3[] normals, Vector2[] uv, int[] indices,
        byte[] boneA, byte[] boneB, byte[] weightA, BasisImposterPayload.ImposterTexture[] textures)
    {
        BasisImposterPayload payload = new BasisImposterPayload
        {
            AvatarEyePosition = avatar.AvatarEyePosition,
            AvatarMouthPosition = avatar.AvatarMouthPosition,
            AuthoredRootScale = root.localScale,
            Textures = textures,
        };

        int boneCount = skeleton.Count;
        payload.BoneHumanBodyBone = new byte[boneCount];
        payload.BoneParentIndex = new byte[boneCount];
        payload.BoneRestLocalPosition = new Vector3[boneCount];
        payload.BoneRestLocalRotation = new Quaternion[boneCount];
        for (int i = 0; i < boneCount; i++)
        {
            payload.BoneHumanBodyBone[i] = (byte)skeleton.Bones[i];
            payload.BoneParentIndex[i] = skeleton.ParentIndex[i] < 0 ? (byte)0xFF : (byte)skeleton.ParentIndex[i];
            payload.BoneRestLocalPosition[i] = skeleton.RestLocalPosition[i];
            payload.BoneRestLocalRotation[i] = skeleton.RestLocalRotation[i];
        }

        int headIndex = skeleton.Bones.IndexOf(HumanBodyBones.Head);
        int hipsIndex = skeleton.Bones.IndexOf(HumanBodyBones.Hips);
        if (headIndex >= 0)
        {
            payload.TposeHeadFromRootPosition = skeleton.RootSpacePosition[headIndex];
            payload.TposeHeadFromRootRotation = skeleton.RootSpaceRotation[headIndex];
        }
        if (hipsIndex >= 0)
        {
            payload.TposeHipsFromRootPosition = skeleton.RootSpacePosition[hipsIndex];
            payload.TposeHipsFromRootRotation = skeleton.RootSpaceRotation[hipsIndex];
        }

        Vector3 boundsMin = positions[0];
        Vector3 boundsMax = positions[0];
        for (int i = 1; i < positions.Length; i++)
        {
            boundsMin = Vector3.Min(boundsMin, positions[i]);
            boundsMax = Vector3.Max(boundsMax, positions[i]);
        }
        Vector3 range = boundsMax - boundsMin;
        if (range.x < 1e-4f) { boundsMax.x += 1e-4f; range.x = 1e-4f; }
        if (range.y < 1e-4f) { boundsMax.y += 1e-4f; range.y = 1e-4f; }
        if (range.z < 1e-4f) { boundsMax.z += 1e-4f; range.z = 1e-4f; }
        payload.PositionBoundsMin = boundsMin;
        payload.PositionBoundsMax = boundsMax;

        // Renderer bounds in hips space, padded for posing (rootBone = hips at runtime).
        int hips = Mathf.Max(hipsIndex, 0);
        Quaternion hipsInverse = Quaternion.Inverse(skeleton.RootSpaceRotation[hips]);
        Vector3 hipsPos = skeleton.RootSpacePosition[hips];
        Vector3 localMin = Vector3.positiveInfinity;
        Vector3 localMax = Vector3.negativeInfinity;
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 local = hipsInverse * (positions[i] - hipsPos);
            localMin = Vector3.Min(localMin, local);
            localMax = Vector3.Max(localMax, local);
        }
        payload.LocalBoundsCenter = (localMin + localMax) * 0.5f;
        payload.LocalBoundsExtents = (localMax - localMin) * 0.5f * 1.5f;

        int vertexCount = positions.Length;
        payload.VertexCount = vertexCount;
        payload.PositionsQ = new ushort[vertexCount * 3];
        payload.NormalsOct = new ushort[vertexCount];
        payload.UvQ = new ushort[vertexCount * 2];
        payload.BoneIndexA = boneA;
        payload.BoneIndexB = boneB;
        payload.BoneWeightA = weightA;
        for (int i = 0; i < vertexCount; i++)
        {
            Vector3 p = positions[i];
            payload.PositionsQ[i * 3] = BasisImposterPayload.QuantizeUnorm((p.x - boundsMin.x) / range.x);
            payload.PositionsQ[i * 3 + 1] = BasisImposterPayload.QuantizeUnorm((p.y - boundsMin.y) / range.y);
            payload.PositionsQ[i * 3 + 2] = BasisImposterPayload.QuantizeUnorm((p.z - boundsMin.z) / range.z);
            payload.NormalsOct[i] = BasisImposterPayload.OctEncodeNormal(i < normals.Length ? normals[i] : Vector3.up);
            Vector2 texcoord = i < uv.Length ? uv[i] : Vector2.zero;
            payload.UvQ[i * 2] = BasisImposterPayload.QuantizeUnorm(texcoord.x);
            payload.UvQ[i * 2 + 1] = BasisImposterPayload.QuantizeUnorm(texcoord.y);
        }

        payload.Indices = new ushort[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            payload.Indices[i] = (ushort)indices[i];
        }
        return payload;
    }

    /// <summary>Records local TRS for a whole hierarchy and restores it after generation.</summary>
    private sealed class TransformPoseSnapshot
    {
        private Transform[] _transforms;
        private Vector3[] _localPositions;
        private Quaternion[] _localRotations;
        private Vector3[] _localScales;

        public static TransformPoseSnapshot Capture(Transform root)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            TransformPoseSnapshot snapshot = new TransformPoseSnapshot
            {
                _transforms = transforms,
                _localPositions = new Vector3[transforms.Length],
                _localRotations = new Quaternion[transforms.Length],
                _localScales = new Vector3[transforms.Length],
            };
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].GetLocalPositionAndRotation(out snapshot._localPositions[i], out snapshot._localRotations[i]);
                snapshot._localScales[i] = transforms[i].localScale;
            }
            return snapshot;
        }

        public void Restore()
        {
            for (int i = 0; i < _transforms.Length; i++)
            {
                Transform transform = _transforms[i];
                if (transform == null)
                {
                    continue;
                }
                transform.SetLocalPositionAndRotation(_localPositions[i], _localRotations[i]);
                transform.localScale = _localScales[i];
            }
        }
    }
}
