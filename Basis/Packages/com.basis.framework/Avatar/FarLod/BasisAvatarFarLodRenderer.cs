using System.Collections.Generic;
using System.Threading.Tasks;
using Basis.Scripts.Avatar;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using Unity.Profiling;
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

        /// <summary>
        /// The fully wired far avatar for this version, built once and kept inactive under
        /// <see cref="PrototypeHolder"/>. Every wearer after the first is a single
        /// <see cref="Object.Instantiate"/> of it instead of ~23 GameObject creations, four
        /// AddComponents and a 55-slot bone capture on the transmit tick.
        /// </summary>
        public GameObject Prototype;
        public GameObject PrototypeHolder;
    }

    private static readonly Dictionary<string, SharedAssets> SharedByVersion = new Dictionary<string, SharedAssets>(8);
    private static readonly int BaseMapProperty = Shader.PropertyToID("_BaseMap");
    private static readonly int MinBrightnessProperty = Shader.PropertyToID("_MinBrightness");
    private static readonly int MaxBrightnessProperty = Shader.PropertyToID("_MaxBrightness");
    private static Shader sFarAvatarShader;

    // Install phase markers. BasisAvatarFarLOD's FarLodInstall marker reports one number for the
    // whole swap; these split it so a spike attributes to the stage that owns it — first-wearer
    // asset construction, the per-player clone, or the factory swap and remote calibration.
    static readonly ProfilerMarker sMarkerShared = new ProfilerMarker("BasisDriver.Network.Transmit.FarLodShared");
    static readonly ProfilerMarker sMarkerBuild = new ProfilerMarker("BasisDriver.Network.Transmit.FarLodBuild");
    static readonly ProfilerMarker sMarkerFactory = new ProfilerMarker("BasisDriver.Network.Transmit.FarLodFactory");

    /// <summary>
    /// Builds this player's far avatar and installs it as their current avatar through the
    /// normal factory path (old avatar deleted, remote calibration, bone-job registration).
    /// Non-blocking: a version whose shared assets exist installs within this call; a first
    /// wearer kicks the payload parse to a worker thread and returns false — the caller keeps
    /// whatever is worn and retries once the parse lands. Returns false with the payload
    /// marked unusable when it refuses to build, so there is no retry spam.
    /// </summary>
    public static bool TryInstall(BasisRemotePlayer remote)
    {
        if (remote == null || remote.IsDestroyed)
        {
            return false;
        }
        if (!ResolvePayload(remote, out string uniqueVersion, out string payloadBase64))
        {
            return false;
        }
        if (WornFarVersion(remote) == uniqueVersion)
        {
            return true;
        }
        if (SharedByVersion.ContainsKey(uniqueVersion))
        {
            return InstallWithPayload(remote, uniqueVersion, null);
        }
        Task<BasisFarLodPayload> parse = StartOrGetParse(uniqueVersion, payloadBase64);
        if (!parse.IsCompleted)
        {
            return false;
        }
        return InstallWithPayload(remote, uniqueVersion, ConsumeParse(uniqueVersion, parse));
    }

    /// <summary>
    /// Starts the worker-thread payload parse for this player's resolved far avatar version
    /// WITHOUT installing anything. Installs are transmit-tick-only: CreateAvatar and the
    /// load error path run on IO/task continuations, where the remote-bone and jiggle
    /// pipelines can have scheduled-but-unjoined jobs — an install there mutates their
    /// TransformAccessArrays mid-flight (the flung-skeleton / Invalid-AABB class). Callers
    /// keep the current avatar worn; the tick's <see cref="TryInstall"/> swaps at the safe
    /// point once the parse lands.
    /// </summary>
    public static void PrewarmParse(BasisRemotePlayer remote)
    {
        if (remote == null || remote.IsDestroyed)
        {
            return;
        }
        if (!ResolvePayload(remote, out string uniqueVersion, out string payloadBase64))
        {
            return;
        }
        if (SharedByVersion.ContainsKey(uniqueVersion) || WornFarVersion(remote) == uniqueVersion)
        {
            return;
        }
        StartOrGetParse(uniqueVersion, payloadBase64);
    }

    /// <summary>
    /// The far avatar payload/version this player should be wearing: the override captured
    /// off the original bundle's connector when present (the current AvatarMetaData may
    /// already point at the loading avatar), else the current connector.
    /// </summary>
    private static bool ResolvePayload(BasisRemotePlayer remote, out string uniqueVersion, out string payloadBase64)
    {
        if (!string.IsNullOrEmpty(remote.FarLodOverridePayload))
        {
            uniqueVersion = remote.FarLodOverrideVersion;
            payloadBase64 = remote.FarLodOverridePayload;
        }
        else
        {
            BasisBundleConnector connector = remote.AvatarMetaData?.BasisBundleConnector;
            uniqueVersion = connector?.UniqueVersion;
            payloadBase64 = connector?.FarLodBase64;
        }
        return !string.IsNullOrEmpty(uniqueVersion) && !string.IsNullOrEmpty(payloadBase64);
    }

    /// <summary>Version of the far avatar this player currently wears, or null.</summary>
    public static string WornFarVersion(BasisRemotePlayer remote)
    {
        // Reads the mirror on BasisAvatar, not the BasisFarAvatarInstance component. The tick
        // asks this for every far-LOD wearer every pass, and a GetComponent per player per tick
        // is the single most expensive thing in that loop. Set together in BuildAvatar; the
        // component is still the release owner.
        BasisAvatar avatar = remote?.BasisAvatar;
        if (avatar != null && avatar.IsFarLodAvatar)
        {
            return avatar.FarLodSharedVersion;
        }
        return null;
    }

    /// <summary>
    /// True when the worn far avatar matches the version the payload resolves to right now —
    /// false for a far avatar left over from a previous avatar record (the tick then swaps
    /// it like any other stale representation).
    /// </summary>
    public static bool IsWearingResolvedVersion(BasisRemotePlayer remote)
    {
        return remote != null && ResolvePayload(remote, out string uniqueVersion, out _) &&
               WornFarVersion(remote) == uniqueVersion;
    }

    /// <summary>Payload parses in flight, keyed by avatar version. Main-thread access only.</summary>
    private static readonly Dictionary<string, Task<BasisFarLodPayload>> sParseInFlight = new Dictionary<string, Task<BasisFarLodPayload>>(4);

    /// <summary>
    /// Starts (or returns the running) worker-thread parse for a version. The parse itself is
    /// pure managed data work — base64 decode, defensive struct parse, and the full mesh
    /// decode to engine-ready arrays — which is exactly the part that used to hitch the main
    /// thread on a first wearer.
    /// </summary>
    private static readonly List<string> sParseSweepScratch = new List<string>(4);

    private static Task<BasisFarLodPayload> StartOrGetParse(string uniqueVersion, string payloadBase64)
    {
        if (sParseInFlight.TryGetValue(uniqueVersion, out Task<BasisFarLodPayload> parse))
        {
            return parse;
        }

        // A parse whose player left before any caller consumed it would pin its decoded
        // payload here forever; drop completed strays once a few stack up (re-parsing a
        // swept version later is correct, just costs the worker again).
        if (sParseInFlight.Count >= 8)
        {
            sParseSweepScratch.Clear();
            foreach (KeyValuePair<string, Task<BasisFarLodPayload>> entry in sParseInFlight)
            {
                if (entry.Value.IsCompleted)
                {
                    sParseSweepScratch.Add(entry.Key);
                }
            }
            for (int Index = 0; Index < sParseSweepScratch.Count; Index++)
            {
                sParseInFlight.Remove(sParseSweepScratch[Index]);
            }
        }

        parse = Task.Run(() =>
        {
            BasisFarLodPayload payload = BasisFarLodPayload.TryParseBase64(payloadBase64);
            payload?.PrepareDecodedMeshData();
            return payload;
        });
        sParseInFlight[uniqueVersion] = parse;
        return parse;
    }

    /// <summary>
    /// Retires a completed parse task and returns its payload (null when the payload was
    /// refused). TryParseBase64 catches its own failures, so a faulted task is exceptional;
    /// its exception is observed here either way.
    /// </summary>
    private static BasisFarLodPayload ConsumeParse(string uniqueVersion, Task<BasisFarLodPayload> parse)
    {
        if (sParseInFlight.TryGetValue(uniqueVersion, out Task<BasisFarLodPayload> current) && current == parse)
        {
            sParseInFlight.Remove(uniqueVersion);
        }
        if (parse.Status != TaskStatus.RanToCompletion)
        {
            BasisDebug.LogError($"Far avatar payload parse task failed for version {uniqueVersion}: {parse.Exception?.GetBaseException().Message ?? "unknown"}", BasisDebug.LogTag.Avatar);
            return null;
        }
        return parse.Result;
    }

    /// <summary>
    /// Main-thread tail of an install: acquire (or build from a parsed payload) the shared
    /// per-version assets, build the skeleton, and swap it in through the factory.
    /// </summary>
    private static bool InstallWithPayload(BasisRemotePlayer remote, string uniqueVersion, BasisFarLodPayload payload)
    {
        SharedAssets shared;
        using (sMarkerShared.Auto())
        {
            shared = AcquireShared(uniqueVersion, payload);
        }
        if (shared == null)
        {
            remote.MarkFarLodPayloadUnusable();
            return false;
        }

        BasisAvatar avatar;
        using (sMarkerBuild.Auto())
        {
            avatar = BuildAvatar(shared, remote.DisplayName);
        }
        if (avatar == null)
        {
            ReleaseShared(shared);
            remote.MarkFarLodPayloadUnusable();
            return false;
        }

        using (sMarkerFactory.Auto())
        {
            BasisAvatarFactory.SetupFarAvatar(remote, avatar);
        }
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
    /// This player's far avatar, ready for the factory. The first wearer of a version pays for
    /// the prototype build; everyone after that is one <see cref="Object.Instantiate"/> — the
    /// clone's serialized wiring (bone array, root bone, TransformStorage, renderer, animator
    /// rig) is remapped into the copy by Unity, so nothing has to be re-resolved per player.
    /// Returns null when the prototype refuses to build.
    /// </summary>
    private static BasisAvatar BuildAvatar(SharedAssets shared, string displayName)
    {
        if (shared.Prototype == null && !BuildPrototype(shared, displayName))
        {
            return null;
        }

        GameObject clone = Object.Instantiate(shared.Prototype);
        clone.SetActive(true);
        Transform cloneTransform = clone.transform;
        cloneTransform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        if (!clone.TryGetComponent(out BasisAvatar avatar))
        {
            BasisDebug.LogError($"Far avatar clone for {displayName} lost its BasisAvatar component.", BasisDebug.LogTag.Avatar);
            Object.Destroy(clone);
            return null;
        }

        // IsFarLodAvatar is [NonSerialized], so the clone starts false — every far LOD gate
        // (WornFarVersion, SeedAfterCalibration, the calibration TPose skip) reads it.
        avatar.IsFarLodAvatar = true;
        // The prototype deliberately carries no version: its OnDestroy must not release the
        // shared assets it belongs to. Only real wearers hold a reference.
        if (clone.TryGetComponent(out BasisFarAvatarInstance instance))
        {
            instance.SharedVersion = shared.UniqueVersion;
            // Mirrored onto the avatar in the same breath so WornFarVersion never has to look
            // the component up — it runs for every far-LOD wearer on every transmit tick.
            avatar.FarLodSharedVersion = shared.UniqueVersion;
        }
        // Cloned by value from a hierarchy whose references were remapped, but an avatar built
        // before the rig resolved would carry a null table — fall back rather than ship one.
        if (avatar.TransformStorage?.HumanoidBones == null)
        {
            avatar.TransformStorage = BasisAvatarTransformStorage.CaptureFrom(avatar.Animator);
        }
        return avatar;
    }

    /// <summary>
    /// Builds the per-version prototype: payload skeleton at its baked T-pose, shared skinned
    /// mesh, humanoid rig, and a wired <see cref="BasisAvatar"/> — a complete avatar the factory
    /// could install as-is. It is left inactive under its build holder and cloned from there for
    /// every wearer. Returns false (and destroys partial state) on failure.
    /// </summary>
    private static bool BuildPrototype(SharedAssets shared, string displayName)
    {
        BasisFarLodPayload payload = shared.Payload;
        int layer = BasisLayerMapper.RemoteAvatarLayer;

        // A prototype whose root died without its holder (or the reverse) leaves the survivor
        // orphaned; the fields are overwritten below, so drop it before it is unreachable.
        if (shared.PrototypeHolder != null)
        {
            Object.Destroy(shared.PrototypeHolder);
            shared.PrototypeHolder = null;
            shared.Prototype = null;
        }

        // The root name is part of the humanoid rig's skeleton description and the rig is
        // shared per version, so it must be deterministic — not player-named.
        GameObject root = new GameObject($"Far Avatar {shared.UniqueVersion}") { layer = layer };
        // Built under a holder parked far below the world (nothing visibly flashes) while the
        // root itself stays at LOCAL identity — AvatarBuilder validates the hierarchy against
        // the skeleton description, which declares the root at zero. Parking the root
        // directly would contradict its own description.
        GameObject buildHolder = new GameObject("Far Avatar Build");
        buildHolder.transform.position = new Vector3(0f, -4096f, 0f);
        try
        {
            Transform rootTransform = root.transform;
            rootTransform.SetParent(buildHolder.transform, false);
            rootTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
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
                    Object.Destroy(buildHolder);
                    return false;
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

            // SharedVersion is left null on the prototype: its OnDestroy runs like any other
            // instance's, and a version there would release the shared assets it belongs to.
            root.AddComponent<BasisFarAvatarInstance>();

            // Parked and inactive rather than unparented and shipped — the prototype is never
            // worn, only cloned. Inactive keeps its renderer out of culling and its animator
            // off; the holder keeps it out of the scene roots that get walked per frame.
            // It outlives scene changes for the same reason the shared mesh/material do: an
            // additive world switch would otherwise force a rebuild on the next swap.
            root.SetActive(false);
            Object.DontDestroyOnLoad(buildHolder);
            shared.Prototype = root;
            shared.PrototypeHolder = buildHolder;
            return true;
        }
        catch (System.Exception e)
        {
            BasisDebug.LogError($"Far avatar build failed for {displayName}: {e}", BasisDebug.LogTag.Avatar);
            Object.Destroy(root);
            Object.Destroy(buildHolder);
            return false;
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

    /// <summary>
    /// Acquires the per-version shared assets, building them from <paramref name="payload"/>
    /// when this is the first wearer. The payload arrives pre-parsed (and pre-decoded) from
    /// the worker thread; only texture/mesh construction and the humanoid rig remain here.
    /// </summary>
    private static SharedAssets AcquireShared(string uniqueVersion, BasisFarLodPayload payload)
    {
        if (SharedByVersion.TryGetValue(uniqueVersion, out SharedAssets existing))
        {
            existing.RefCount++;
            return existing;
        }

        if (payload == null)
        {
            return null;
        }

        Texture2D texture = payload.CreateTexture();
        if (texture == null)
        {
            BasisDebug.LogError($"Far avatar texture build failed for version {uniqueVersion}.", BasisDebug.LogTag.Avatar);
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
            BasisDebug.LogError($"Far avatar mesh build failed for version {uniqueVersion}.", BasisDebug.LogTag.Avatar);
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

        // Mesh and texture now exist as engine objects; the retained payload only feeds the
        // per-version prototype build, which reads none of the heavy arrays.
        payload.ReleaseMeshSourceData();

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
        // The prototype goes first: it holds the mesh/material/rig below and its own
        // BasisFarAvatarInstance is version-less, so destroying it releases nothing further.
        if (shared.PrototypeHolder != null)
        {
            Object.Destroy(shared.PrototypeHolder);
        }
        shared.PrototypeHolder = null;
        shared.Prototype = null;
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
