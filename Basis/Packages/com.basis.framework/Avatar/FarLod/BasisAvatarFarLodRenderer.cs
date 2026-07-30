using System.Collections.Generic;
using Basis.Scripts.Avatar;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds a far avatar as a REAL <see cref="BasisAvatar"/> from the
/// <see cref="BasisFarLodPayload"/> carried in the bee connector: a runtime skeleton, a
/// humanoid rig rebuilt with <see cref="AvatarBuilder"/> (the same way generic glTF avatars
/// are rebuilt), and a shared low-poly skinned mesh. The result installs through the exact
/// same factory/calibration/registration pipeline as every other avatar — loading avatar,
/// bundle avatar, glTF avatar and far avatar are all the same thing to the rest of the
/// system. Nothing is ever hidden or disabled; swapping representations swaps the avatar.
///
/// Mesh, texture, material and humanoid rig are shared per avatar version across every
/// player wearing it; only the ~20-transform skeleton is per player.
/// </summary>
public static class BasisFarAvatarBuilder
{
    public sealed class SharedAssets
    {
        public string UniqueVersion;
        public BasisFarLodPayload Payload;
        public Mesh Mesh;
        public Texture2D Texture;
        public Material Material;
        public Avatar HumanoidRig;
        public int HipsIndex;
        public int RefCount;
    }

    private static readonly Dictionary<string, SharedAssets> SharedByVersion = new Dictionary<string, SharedAssets>(8);
    private static readonly int BaseMapProperty = Shader.PropertyToID("_BaseMap");
    private static readonly int MinBrightnessProperty = Shader.PropertyToID("_MinBrightness");
    private static readonly int MaxBrightnessProperty = Shader.PropertyToID("_MaxBrightness");
    private static Shader sFarAvatarShader;

    /// <summary>
    /// Builds this player's far avatar and installs it as their current avatar through the
    /// normal factory path (old avatar deleted, remote calibration, bone-job registration).
    /// Returns false when no usable payload exists or the build fails — the payload is then
    /// marked unusable so the player stays on whatever avatar they have without retry spam.
    /// </summary>
    public static bool TryInstall(BasisRemotePlayer remote)
    {
        if (remote == null || remote.IsDestroyed)
        {
            return false;
        }

        string uniqueVersion;
        string payloadBase64;
        if (!string.IsNullOrEmpty(remote.FarLodOverridePayload))
        {
            // Captured off the original bundle's connector — the current AvatarMetaData may
            // already point at the loading avatar.
            uniqueVersion = remote.FarLodOverrideVersion;
            payloadBase64 = remote.FarLodOverridePayload;
        }
        else
        {
            BasisBundleConnector connector = remote.AvatarMetaData?.BasisBundleConnector;
            uniqueVersion = connector?.UniqueVersion;
            payloadBase64 = connector?.FarLodBase64;
        }
        if (string.IsNullOrEmpty(uniqueVersion) || string.IsNullOrEmpty(payloadBase64))
        {
            return false;
        }

        SharedAssets shared = AcquireShared(uniqueVersion, payloadBase64);
        if (shared == null)
        {
            remote.MarkFarLodPayloadUnusable();
            return false;
        }

        BasisAvatar avatar = BuildAvatar(shared, remote.DisplayName);
        if (avatar == null)
        {
            ReleaseShared(shared);
            remote.MarkFarLodPayloadUnusable();
            return false;
        }

        BasisAvatarFactory.SetupFarAvatar(remote, avatar);
        if (remote.BasisAvatar != avatar)
        {
            // Calibration failed and the factory recovered onto the fallback (the instance
            // component released the shared assets when it was destroyed) — latch the payload
            // so this doesn't retry every tick.
            remote.MarkFarLodPayloadUnusable();
            return false;
        }
        return true;
    }

    /// <summary>
    /// Builds the far avatar GameObject: payload skeleton at its baked T-pose, shared skinned
    /// mesh, humanoid rig, and a wired <see cref="BasisAvatar"/> — a complete avatar the
    /// factory can install like any other. Returns null (and destroys partial state) on
    /// failure.
    /// </summary>
    private static BasisAvatar BuildAvatar(SharedAssets shared, string displayName)
    {
        BasisFarLodPayload payload = shared.Payload;
        int layer = BasisLayerMapper.RemoteAvatarLayer;

        // The root name is part of the humanoid rig's skeleton description and the rig is
        // shared per version, so it must be deterministic — not player-named.
        GameObject root = new GameObject($"Far Avatar {shared.UniqueVersion}") { layer = layer };
        try
        {
            Transform rootTransform = root.transform;
            // Built far below the world: AvatarBuilder needs an active hierarchy, and the
            // factory snaps the installed avatar onto the network pose during calibration.
            rootTransform.SetPositionAndRotation(new Vector3(0f, -4096f, 0f), Quaternion.identity);
            rootTransform.localScale = payload.AuthoredRootScale;

            int boneCount = payload.BoneCount;
            Transform[] bones = new Transform[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                GameObject boneObject = new GameObject(((HumanBodyBones)payload.BoneHumanBodyBone[i]).ToString()) { layer = layer };
                Transform bone = boneObject.transform;
                byte parent = payload.BoneParentIndex[i];
                bone.SetParent(parent == 0xFF ? rootTransform : bones[parent], false);
                bone.SetLocalPositionAndRotation(payload.BoneRestLocalPosition[i], payload.BoneRestLocalRotation[i]);
                bones[i] = bone;
            }
            Transform hips = shared.HipsIndex >= 0 ? bones[shared.HipsIndex] : bones[0];

            GameObject meshObject = new GameObject("Mesh") { layer = layer };
            meshObject.transform.SetParent(rootTransform, false);
            SkinnedMeshRenderer renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = shared.Mesh;
            renderer.sharedMaterial = shared.Material;
            renderer.bones = bones;
            renderer.rootBone = hips;
            renderer.localBounds = new Bounds(payload.LocalBoundsCenter, payload.LocalBoundsExtents * 2f);
            renderer.quality = SkinQuality.Bone2;
            renderer.updateWhenOffscreen = false;
            renderer.skinnedMotionVectors = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            Animator animator = root.AddComponent<Animator>();
            if (shared.HumanoidRig == null)
            {
                shared.HumanoidRig = BuildHumanoidRig(root, bones, payload);
                if (shared.HumanoidRig == null)
                {
                    Object.Destroy(root);
                    return null;
                }
            }
            animator.avatar = shared.HumanoidRig;

            BasisAvatar avatar = root.AddComponent<BasisAvatar>();
            avatar.IsFarLodAvatar = true;
            avatar.Animator = animator;
            avatar.AvatarEyePosition = payload.AvatarEyePosition;
            avatar.AvatarMouthPosition = payload.AvatarMouthPosition;
            // The body IS the face mesh: no visemes to drive, but face-visibility culling
            // then gates remote face/eye work exactly like on a normal avatar.
            avatar.FaceVisemeMesh = renderer;
            avatar.Renders = new Renderer[] { renderer };
            avatar.TransformStorage = BasisAvatarTransformStorage.CaptureFrom(animator);
            avatar.HumanScale = animator.humanScale;

            BasisFarAvatarInstance instance = root.AddComponent<BasisFarAvatarInstance>();
            instance.SharedVersion = shared.UniqueVersion;

            rootTransform.position = Vector3.zero;
            return avatar;
        }
        catch (System.Exception e)
        {
            BasisDebug.LogError($"Far avatar build failed for {displayName}: {e}", BasisDebug.LogTag.Avatar);
            Object.Destroy(root);
            return null;
        }
    }

    /// <summary>
    /// Rebuilds a humanoid rig from the payload skeleton with <see cref="AvatarBuilder"/> —
    /// the same runtime path generic glTF avatars use. The hierarchy is active and parked far
    /// below the world while the synchronous build runs.
    /// </summary>
    private static Avatar BuildHumanoidRig(GameObject root, Transform[] bones, BasisFarLodPayload payload)
    {
        int boneCount = bones.Length;
        HumanBone[] human = new HumanBone[boneCount];
        SkeletonBone[] skeleton = new SkeletonBone[boneCount + 1];
        skeleton[0] = new SkeletonBone
        {
            name = root.name,
            position = Vector3.zero,
            rotation = Quaternion.identity,
            scale = payload.AuthoredRootScale,
        };
        for (int i = 0; i < boneCount; i++)
        {
            HumanBodyBones humanBone = (HumanBodyBones)payload.BoneHumanBodyBone[i];
            human[i] = new HumanBone
            {
                humanName = HumanTrait.BoneName[(int)humanBone],
                boneName = bones[i].name,
                limit = new HumanLimit { useDefaultValues = true },
            };
            skeleton[i + 1] = new SkeletonBone
            {
                name = bones[i].name,
                position = payload.BoneRestLocalPosition[i],
                rotation = payload.BoneRestLocalRotation[i],
                scale = Vector3.one,
            };
        }

        HumanDescription description = new HumanDescription
        {
            human = human,
            skeleton = skeleton,
            armStretch = 0.05f,
            legStretch = 0.05f,
            upperArmTwist = 0.5f,
            lowerArmTwist = 0.5f,
            upperLegTwist = 0.5f,
            lowerLegTwist = 0.5f,
            feetSpacing = 0f,
            hasTranslationDoF = false,
        };

        try
        {
            Avatar built = AvatarBuilder.BuildHumanAvatar(root, description);
            if (built == null || !built.isValid || !built.isHuman)
            {
                BasisDebug.LogError("Far avatar humanoid rig rebuild produced an invalid rig.", BasisDebug.LogTag.Avatar);
                if (built != null)
                {
                    Object.Destroy(built);
                }
                return null;
            }
            built.name = root.name;
            return built;
        }
        catch (System.Exception e)
        {
            BasisDebug.LogError($"Far avatar AvatarBuilder threw: {e.Message}", BasisDebug.LogTag.Avatar);
            return null;
        }
    }

    private static SharedAssets AcquireShared(string uniqueVersion, string base64)
    {
        if (SharedByVersion.TryGetValue(uniqueVersion, out SharedAssets existing))
        {
            existing.RefCount++;
            return existing;
        }

        BasisFarLodPayload payload = BasisFarLodPayload.TryParseBase64(base64);
        if (payload == null)
        {
            return null;
        }

        Texture2D texture = payload.CreateTexture();
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
            HipsIndex = payload.FindBone(HumanBodyBones.Hips),
        };

        shared.Mesh = payload.CreateMesh();
        if (shared.Mesh == null)
        {
            Object.Destroy(texture);
            return null;
        }

        if (sFarAvatarShader == null)
        {
            sFarAvatarShader = Shader.Find("Basis/AvatarFarLod");
        }
        if (sFarAvatarShader == null)
        {
            BasisDebug.LogError("Basis/AvatarFarLod shader missing from build — far avatars disabled.", BasisDebug.LogTag.Avatar);
            Object.Destroy(texture);
            Object.Destroy(shared.Mesh);
            return null;
        }
        shared.Material = new Material(sFarAvatarShader) { enableInstancing = true };
        shared.Material.SetTexture(BaseMapProperty, texture);
        shared.Material.SetFloat(MinBrightnessProperty, payload.MinBrightness);
        shared.Material.SetFloat(MaxBrightnessProperty, payload.MaxBrightness);

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
            Object.Destroy(shared.Material);
        }
        if (shared.Texture != null)
        {
            Object.Destroy(shared.Texture);
        }
        if (shared.Mesh != null)
        {
            Object.Destroy(shared.Mesh);
        }
        if (shared.HumanoidRig != null)
        {
            Object.Destroy(shared.HumanoidRig);
        }
    }

    /// <summary>Release hook for <see cref="BasisFarAvatarInstance"/> — keyed release survives every teardown path.</summary>
    public static void ReleaseSharedByVersion(string uniqueVersion)
    {
        if (!string.IsNullOrEmpty(uniqueVersion) && SharedByVersion.TryGetValue(uniqueVersion, out SharedAssets shared))
        {
            ReleaseShared(shared);
        }
    }
}

/// <summary>
/// Rides on every far avatar root so the shared per-version assets are released no matter
/// which path destroys the avatar (normal swap, disconnect, world change).
/// </summary>
public class BasisFarAvatarInstance : MonoBehaviour
{
    public string SharedVersion;

    private void OnDestroy()
    {
        BasisFarAvatarBuilder.ReleaseSharedByVersion(SharedVersion);
        SharedVersion = null;
    }
}
