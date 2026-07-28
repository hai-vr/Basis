using System.Collections.Generic;
using Basis.Network.Core.Compression;
using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Runtime distance imposter for one remote player: a low-poly skinned proxy built from the
/// <see cref="BasisImposterPayload"/> carried in the avatar's bundle connector, skinned to a
/// rebuilt copy of the avatar's humanoid skeleton and registered with
/// <see cref="RemoteBoneJobSystem"/> under the same player id — so the existing networked
/// per-bone deltas drive it with no retargeting and no new job code.
///
/// Mesh, texture and material are shared per avatar version across every player wearing it;
/// only the ~20-transform skeleton is per player. The imposter root is its own scene root
/// (like the real remote avatar) so bone jobs keep parallelizing across Transform roots.
/// </summary>
public class BasisAvatarImposter
{
    public sealed class SharedAssets
    {
        public string UniqueVersion;
        public BasisImposterPayload Payload;
        public Mesh Mesh;
        public Texture2D Texture;
        public Material Material;
        public Vector3[] BoneRootSpacePosition;
        public Quaternion[] BoneRootSpaceRotation;
        public int HipsIndex;
        public int HeadIndex;
        public int RefCount;
    }

    private static readonly Dictionary<string, SharedAssets> SharedByVersion = new Dictionary<string, SharedAssets>(8);
    private static Shader sImposterShader;

    public GameObject Root;
    public Transform RootTransform;
    public Transform[] BoneTransforms;
    public Transform Hips;
    public Transform Head;
    public SkinnedMeshRenderer Renderer;
    public bool IsActive;

    private SharedAssets _shared;
    private Transform[] _slotTransforms;
    private NativeArray<quaternion> _slotTpose;

    public static BasisAvatarImposter TryCreate(BasisRemotePlayer remote)
    {
        BasisBundleConnector connector = remote?.AvatarMetaData?.BasisBundleConnector;
        if (connector == null || string.IsNullOrEmpty(connector.UniqueVersion) || string.IsNullOrEmpty(connector.ImposterBase64))
        {
            return null;
        }
        SharedAssets shared = AcquireShared(connector.UniqueVersion, connector.ImposterBase64);
        if (shared == null)
        {
            return null;
        }

        BasisAvatarImposter imposter = new BasisAvatarImposter { _shared = shared };
        imposter.BuildInstance(remote);
        return imposter;
    }

    private void BuildInstance(BasisRemotePlayer remote)
    {
        BasisImposterPayload payload = _shared.Payload;
        int layer = BasisLayerMapper.RemoteAvatarLayer;

        Root = new GameObject($"Imposter {remote.DisplayName}") { layer = layer };
        UnityEngine.Object.DontDestroyOnLoad(Root);
        RootTransform = Root.transform;

        int boneCount = payload.BoneCount;
        BoneTransforms = new Transform[boneCount];
        for (int i = 0; i < boneCount; i++)
        {
            GameObject boneObject = new GameObject(((HumanBodyBones)payload.BoneHumanBodyBone[i]).ToString()) { layer = layer };
            Transform bone = boneObject.transform;
            byte parent = payload.BoneParentIndex[i];
            bone.SetParent(parent == 0xFF ? RootTransform : BoneTransforms[parent], false);
            bone.SetLocalPositionAndRotation(payload.BoneRestLocalPosition[i], payload.BoneRestLocalRotation[i]);
            BoneTransforms[i] = bone;
        }
        Hips = _shared.HipsIndex >= 0 ? BoneTransforms[_shared.HipsIndex] : BoneTransforms[0];
        Head = _shared.HeadIndex >= 0 ? BoneTransforms[_shared.HeadIndex] : Hips;

        GameObject meshObject = new GameObject("Mesh") { layer = layer };
        meshObject.transform.SetParent(RootTransform, false);
        Renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
        Renderer.sharedMesh = _shared.Mesh;
        Renderer.sharedMaterial = _shared.Material;
        Renderer.bones = BoneTransforms;
        Renderer.rootBone = Hips;
        Renderer.localBounds = new Bounds(payload.LocalBoundsCenter, payload.LocalBoundsExtents * 2f);
        Renderer.quality = SkinQuality.Bone2;
        Renderer.updateWhenOffscreen = false;
        Renderer.skinnedMotionVectors = false;
        Renderer.shadowCastingMode = ShadowCastingMode.Off;
        Renderer.receiveShadows = false;
        Renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        // BONE_WRITE_ORDER slot arrays for the bone job system. Missing slots stay null —
        // the job system dummies them out. Hips has no slot (world-applied separately).
        int slotCount = BasisBoneRotationCompression.SyncBoneCount;
        _slotTransforms = new Transform[slotCount];
        _slotTpose = new NativeArray<quaternion>(slotCount, Allocator.Persistent);
        for (int slot = 0; slot < slotCount; slot++)
        {
            _slotTpose[slot] = quaternion.identity;
        }
        for (int i = 0; i < boneCount; i++)
        {
            int slot = BasisBoneRotationCompression.BONE_TO_SLOT[payload.BoneHumanBodyBone[i]];
            if (slot >= 0)
            {
                _slotTransforms[slot] = BoneTransforms[i];
                _slotTpose[slot] = payload.BoneRestLocalRotation[i];
            }
        }

        Root.SetActive(false);
    }

    /// <summary>
    /// Swaps this player's bone-job registration from the real avatar to the imposter skeleton
    /// and hides the avatar. Forces one job sync (RemoveRemotePlayer) — callers budget swaps
    /// per tick the same way avatar reloads are budgeted.
    /// </summary>
    public bool Activate(BasisRemotePlayer remote)
    {
        if (IsActive || Root == null)
        {
            return IsActive;
        }
        var receiver = remote.NetworkReceiver;
        var driver = remote.RemoteAvatarDriver;
        if (receiver == null || driver == null)
        {
            return false;
        }
        BasisImposterPayload payload = _shared.Payload;

        receiver.GetLatestNetworkPose(out float3 hipsWorldPos, out quaternion hipsWorldRot, out float3 networkScale);
        quaternion tposeHipsRot = payload.TposeHipsFromRootRotation;
        quaternion rootRot = math.mul(hipsWorldRot, math.conjugate(tposeHipsRot));
        float3 scaledLocal = networkScale * (float3)payload.TposeHipsFromRootPosition;
        float3 rootPos = hipsWorldPos - math.mul(rootRot, scaledLocal);
        RootTransform.SetPositionAndRotation(rootPos, rootRot);
        RootTransform.localScale = networkScale;
        Hips.SetPositionAndRotation(hipsWorldPos, hipsWorldRot);

        BasisRemoteNetworkDriver.EnsureSlotInitialized(receiver.playerId);
        RemoteBoneJobSystem.RemoveRemotePlayer(receiver.playerId);
        RemoteBoneJobSystem.AddRemotePlayer(
            key: receiver.playerId,
            remotePlayerRoot: RootTransform,
            head: Head,
            hips: Hips,
            tposeHead: new BasisCalibratedCoords { position = payload.TposeHeadFromRootPosition, rotation = payload.TposeHeadFromRootRotation },
            tposeHips: new BasisCalibratedCoords { position = payload.TposeHipsFromRootPosition, rotation = payload.TposeHipsFromRootRotation },
            tposeHipsLocalPos: payload.TposeHipsFromRootPosition,
            tposeHipsLocalRot: payload.TposeHipsFromRootRotation,
            authoredCenterEyeWorld: BasisHelpers.ConvertFromLocalSpace(BasisHelpers.AvatarPositionConversion(payload.AvatarEyePosition), (Vector3)rootPos),
            authoredMouthWorld: BasisHelpers.ConvertFromLocalSpace(BasisHelpers.AvatarPositionConversion(payload.AvatarMouthPosition), (Vector3)rootPos),
            NamePlate: remote.NamePlateTransformProvider?.Invoke(),
            AvatarScale: RootTransform,
            MouthTransform: remote.MouthTransform,
            TposedScale: payload.AuthoredRootScale,
            boneTPoseLocal: _slotTpose,
            boneTransforms: _slotTransforms);

        Root.SetActive(true);
        if (remote.BasisAvatar != null && remote.BasisAvatar.gameObject.activeSelf)
        {
            remote.BasisAvatar.gameObject.SetActive(false);
        }
        IsActive = true;
        return true;
    }

    /// <summary>
    /// Swaps the registration back to the real avatar, reactivates it and snaps it onto the
    /// latest network pose so it doesn't reappear at a stale location.
    /// </summary>
    public bool Deactivate(BasisRemotePlayer remote)
    {
        if (!IsActive)
        {
            return false;
        }
        IsActive = false;
        var receiver = remote.NetworkReceiver;
        if (receiver != null)
        {
            RemoteBoneJobSystem.RemoveRemotePlayer(receiver.playerId);
        }
        if (Root != null)
        {
            Root.SetActive(false);
        }

        var avatar = remote.BasisAvatar;
        if (avatar != null && !remote.IsEffectivelyBlocked)
        {
            if (!avatar.gameObject.activeSelf)
            {
                avatar.gameObject.SetActive(true);
            }
            remote.RemoteAvatarDriver?.RegisterAvatarWithBoneJobSystem(remote, snapToNetworkPose: true);
        }
        return true;
    }

    /// <summary>
    /// Destroys the per-player skeleton and releases the shared assets. Must run while this
    /// player's registration has already been removed from the bone job system.
    /// </summary>
    public void DestroyInstance()
    {
        if (Root != null)
        {
            UnityEngine.Object.Destroy(Root);
            Root = null;
        }
        if (_slotTpose.IsCreated)
        {
            _slotTpose.Dispose();
        }
        _slotTransforms = null;
        BoneTransforms = null;
        Renderer = null;
        IsActive = false;
        if (_shared != null)
        {
            ReleaseShared(_shared);
            _shared = null;
        }
    }

    private static SharedAssets AcquireShared(string uniqueVersion, string base64)
    {
        if (SharedByVersion.TryGetValue(uniqueVersion, out SharedAssets existing))
        {
            existing.RefCount++;
            return existing;
        }

        BasisImposterPayload payload = BasisImposterPayload.TryParseBase64(base64);
        if (payload == null)
        {
            return null;
        }

        Texture2D texture = BuildTexture(payload);
        if (texture == null)
        {
            return null;
        }

        SharedAssets shared = new SharedAssets
        {
            UniqueVersion = uniqueVersion,
            Payload = payload,
            Texture = texture,
            RefCount = 1,
            HipsIndex = -1,
            HeadIndex = -1,
        };
        for (int i = 0; i < payload.BoneCount; i++)
        {
            if (payload.BoneHumanBodyBone[i] == (byte)HumanBodyBones.Hips)
            {
                shared.HipsIndex = i;
            }
            else if (payload.BoneHumanBodyBone[i] == (byte)HumanBodyBones.Head)
            {
                shared.HeadIndex = i;
            }
        }

        shared.BoneRootSpacePosition = new Vector3[payload.BoneCount];
        shared.BoneRootSpaceRotation = new Quaternion[payload.BoneCount];
        for (int i = 0; i < payload.BoneCount; i++)
        {
            byte parent = payload.BoneParentIndex[i];
            if (parent == 0xFF)
            {
                shared.BoneRootSpacePosition[i] = payload.BoneRestLocalPosition[i];
                shared.BoneRootSpaceRotation[i] = payload.BoneRestLocalRotation[i];
            }
            else
            {
                shared.BoneRootSpacePosition[i] = shared.BoneRootSpacePosition[parent] + shared.BoneRootSpaceRotation[parent] * payload.BoneRestLocalPosition[i];
                shared.BoneRootSpaceRotation[i] = shared.BoneRootSpaceRotation[parent] * payload.BoneRestLocalRotation[i];
            }
        }

        shared.Mesh = BuildMesh(payload, shared);
        if (shared.Mesh == null)
        {
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        if (sImposterShader == null)
        {
            sImposterShader = Shader.Find("Basis/AvatarImposter");
        }
        if (sImposterShader == null)
        {
            BasisDebug.LogError("Basis/AvatarImposter shader missing from build — imposters disabled.", BasisDebug.LogTag.Avatar);
            UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(shared.Mesh);
            return null;
        }
        shared.Material = new Material(sImposterShader) { mainTexture = texture };

        SharedByVersion[uniqueVersion] = shared;
        return shared;
    }

    private static void ReleaseShared(SharedAssets shared)
    {
        shared.RefCount--;
        if (shared.RefCount > 0)
        {
            return;
        }
        SharedByVersion.Remove(shared.UniqueVersion);
        if (shared.Material != null)
        {
            UnityEngine.Object.Destroy(shared.Material);
        }
        if (shared.Texture != null)
        {
            UnityEngine.Object.Destroy(shared.Texture);
        }
        if (shared.Mesh != null)
        {
            UnityEngine.Object.Destroy(shared.Mesh);
        }
    }

    private static Texture2D BuildTexture(BasisImposterPayload payload)
    {
        for (int i = 0; i < payload.Textures.Length; i++)
        {
            BasisImposterPayload.ImposterTexture texturePayload = payload.Textures[i];
            TextureFormat format;
            switch (texturePayload.Format)
            {
                case BasisImposterPayload.ImposterTextureFormat.BC1: format = TextureFormat.DXT1; break;
                case BasisImposterPayload.ImposterTextureFormat.ASTC6x6: format = TextureFormat.ASTC_6x6; break;
                case BasisImposterPayload.ImposterTextureFormat.RGBA32: format = TextureFormat.RGBA32; break;
                default: continue;
            }
            if (!SystemInfo.SupportsTextureFormat(format))
            {
                continue;
            }
            try
            {
                Texture2D texture = new Texture2D(texturePayload.Width, texturePayload.Height, format, texturePayload.MipCount > 1, false)
                {
                    name = "BasisImposterAtlas",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 1,
                };
                texture.LoadRawTextureData(texturePayload.Data);
                texture.Apply(false, true);
                return texture;
            }
            catch (System.Exception e)
            {
                BasisDebug.LogError($"Imposter texture rejected: {e.Message}", BasisDebug.LogTag.Avatar);
            }
        }
        return null;
    }

    private static Mesh BuildMesh(BasisImposterPayload payload, SharedAssets shared)
    {
        try
        {
            int vertexCount = payload.VertexCount;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uv = new Vector2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = payload.DequantizePosition(i);
                normals[i] = BasisImposterPayload.OctDecodeNormal(payload.NormalsOct[i]);
                uv[i] = payload.DequantizeUv(i);
            }
            int[] triangles = new int[payload.Indices.Length];
            for (int i = 0; i < triangles.Length; i++)
            {
                triangles[i] = payload.Indices[i];
            }

            Mesh mesh = new Mesh { name = "BasisImposterMesh" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = triangles;

            int influenceCount = 0;
            for (int i = 0; i < vertexCount; i++)
            {
                influenceCount += payload.BoneIndexB[i] != payload.BoneIndexA[i] && payload.BoneWeightA[i] < 255 ? 2 : 1;
            }
            using (NativeArray<byte> bonesPerVertex = new NativeArray<byte>(vertexCount, Allocator.Temp))
            using (NativeArray<BoneWeight1> weights = new NativeArray<BoneWeight1>(influenceCount, Allocator.Temp))
            {
                var bonesPerVertexSpan = bonesPerVertex;
                var weightSpan = weights;
                int cursor = 0;
                for (int i = 0; i < vertexCount; i++)
                {
                    float weightA = payload.BoneWeightA[i] * (1f / 255f);
                    bool twoInfluences = payload.BoneIndexB[i] != payload.BoneIndexA[i] && payload.BoneWeightA[i] < 255;
                    bonesPerVertexSpan[i] = (byte)(twoInfluences ? 2 : 1);
                    if (twoInfluences)
                    {
                        weightSpan[cursor++] = new BoneWeight1 { boneIndex = payload.BoneIndexA[i], weight = weightA };
                        weightSpan[cursor++] = new BoneWeight1 { boneIndex = payload.BoneIndexB[i], weight = 1f - weightA };
                    }
                    else
                    {
                        weightSpan[cursor++] = new BoneWeight1 { boneIndex = payload.BoneIndexA[i], weight = 1f };
                    }
                }
                mesh.SetBoneWeights(bonesPerVertex, weights);
            }

            Matrix4x4[] bindposes = new Matrix4x4[payload.BoneCount];
            for (int i = 0; i < payload.BoneCount; i++)
            {
                bindposes[i] = Matrix4x4.TRS(shared.BoneRootSpacePosition[i], shared.BoneRootSpaceRotation[i], Vector3.one).inverse;
            }
            mesh.bindposes = bindposes;
            mesh.bounds = new Bounds(
                (payload.PositionBoundsMin + payload.PositionBoundsMax) * 0.5f,
                payload.PositionBoundsMax - payload.PositionBoundsMin);
            mesh.UploadMeshData(true);
            return mesh;
        }
        catch (System.Exception e)
        {
            BasisDebug.LogError($"Imposter mesh rejected: {e.Message}", BasisDebug.LogTag.Avatar);
            return null;
        }
    }
}
