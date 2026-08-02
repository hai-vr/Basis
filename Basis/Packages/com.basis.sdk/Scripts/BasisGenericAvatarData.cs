using System.Collections.Generic;
using Basis.Scripts.BasisSdk;
using UnityEngine;

/// <summary>
/// Everything needed to reconstruct a functional <see cref="BasisAvatar"/> from the
/// platform-agnostic Generic (glTF binary) bundle section. glTF carries meshes, materials,
/// skinning and blendshapes, but has no concept of Unity's humanoid rig or Basis's avatar
/// wiring (visemes, eye/mouth positions), so that data is captured at SDK build time from the
/// authored avatar and rides as JSON on <see cref="BasisBundleGenerated.GenericAvatarDataJson"/>
/// inside the encrypted connector. At runtime, after the GLB is imported, this rebuilds the
/// humanoid Avatar via <c>AvatarBuilder.BuildHumanAvatar</c> and rewires the BasisAvatar.
/// Bone and node references are by transform name/path, which is why the exporter must make
/// transform names unique before capture.
/// </summary>
[System.Serializable]
public class BasisGenericAvatarData
{
    public int Version = 1;

    // BasisAvatar wiring, mirrored from the authored component.
    public Vector2 AvatarEyePosition;
    public Vector2 AvatarMouthPosition;
    public float EyeLiveliness = 0.5f;
    public float EyeAttentiveness = 0.5f;
    public int[] FaceVisemeMovement;
    public BasisVisemeProfile[] FaceVisemeProfiles;
    public BasisVisemeDriveConfig FaceVisemeDrive;
    public int[] BlinkViseme;
    public int LaughterBlendTarget = -1;
    public Vector3 AnimatorHumanScale = Vector3.one;

    // Hierarchy paths relative to the avatar root node (Transform.Find syntax). Empty when
    // the source avatar had no such mesh; the root itself is the empty path.
    public string FaceVisemeMeshPath;
    public string FaceBlinkMeshPath;

    // Blendshape names matching the index arrays above, so indices can be re-resolved on the
    // imported mesh — the sidecar-rebuilt shapes don't share the source mesh's ordering.
    // Null entries mean that slot was unused (-1) on the authored avatar.
    public string[] FaceVisemeShapeNames;
    public string[] BlinkShapeNames;
    public string LaughterShapeName;

    // Name of the avatar root node inside the exported glTF scene, used to locate the rig
    // root after import (glTFast wraps exported roots in a scene GameObject).
    public string RootNodeName;

    // glTF has no per-node active flag or per-renderer enabled flag — everything imports
    // active and enabled. These record what the authored avatar had switched off (hidden
    // outfit variants etc.) so the state can be restored after import.
    public string[] InactiveNodePaths;
    public string[] DisabledRendererPaths;

    // Replicated avatar components (jiggle rigs, face tracking, colliders, ...) captured by
    // BasisGenericComponentReplicator — hierarchy references stored as node paths. Null on
    // payloads built before component replication existed.
    public string ComponentsJson;

    // Humanoid rig description mirroring UnityEngine.HumanDescription. Skeleton bones are
    // captured from the live (build-time) hierarchy in depth-first order, root first, so the
    // authored pose acts as the T-pose reference — Basis avatars are authored in T-pose.
    public BasisHumanBoneEntry[] HumanBones;
    public BasisSkeletonBoneEntry[] SkeletonBones;
    public float ArmStretch = 0.05f;
    public float LegStretch = 0.05f;
    public float UpperArmTwist = 0.5f;
    public float LowerArmTwist = 0.5f;
    public float UpperLegTwist = 0.5f;
    public float LowerLegTwist = 0.5f;
    public float FeetSpacing;
    public bool HasTranslationDoF;

    [System.Serializable]
    public struct BasisHumanBoneEntry
    {
        public string HumanName;
        public string BoneName;
        public bool UseDefaultValues;
        public Vector3 LimitMin;
        public Vector3 LimitMax;
        public Vector3 LimitCenter;
        public float AxisLength;
    }

    [System.Serializable]
    public struct BasisSkeletonBoneEntry
    {
        public string Name;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
    }

    public string ToJson()
    {
        return JsonUtility.ToJson(this);
    }

    public static BasisGenericAvatarData FromJson(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }
        try
        {
            return JsonUtility.FromJson<BasisGenericAvatarData>(json);
        }
        catch (System.Exception ex)
        {
            BasisDebug.LogError($"Failed to parse BasisGenericAvatarData: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Captures rig + wiring from a live avatar hierarchy. The caller is expected to have
    /// already made transform names unique and settled the root transform, because skeleton
    /// bones and mesh paths are recorded against the hierarchy exactly as passed in.
    /// Returns null when the avatar has no humanoid Animator — the generic section cannot
    /// function without one.
    /// </summary>
    public static BasisGenericAvatarData Capture(BasisAvatar avatar)
    {
        if (avatar == null)
        {
            return null;
        }
        Animator animator = avatar.Animator != null ? avatar.Animator : avatar.GetComponent<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            BasisDebug.LogError("Generic avatar capture requires a humanoid Animator.");
            return null;
        }

        Transform root = avatar.transform;

        List<BasisHumanBoneEntry> humanBones = new List<BasisHumanBoneEntry>();
        for (int boneIndex = 0; boneIndex < (int)HumanBodyBones.LastBone; boneIndex++)
        {
            Transform boneTransform = animator.GetBoneTransform((HumanBodyBones)boneIndex);
            if (boneTransform == null)
            {
                continue;
            }
            humanBones.Add(new BasisHumanBoneEntry
            {
                HumanName = HumanTrait.BoneName[boneIndex],
                BoneName = boneTransform.name,
                UseDefaultValues = true,
            });
        }

        List<BasisSkeletonBoneEntry> skeletonBones = new List<BasisSkeletonBoneEntry>();
        List<string> inactiveNodePaths = new List<string>();
        CaptureSkeletonRecursive(root, root, skeletonBones, inactiveNodePaths);

        List<string> disabledRendererPaths = new List<string>();
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int Index = 0; Index < renderers.Length; Index++)
        {
            Renderer renderer = renderers[Index];
            if (renderer != null && !renderer.enabled)
            {
                disabledRendererPaths.Add(GetPathRelativeTo(root, renderer.transform));
            }
        }

        Mesh visemeMesh = avatar.FaceVisemeMesh != null ? avatar.FaceVisemeMesh.sharedMesh : null;
        Mesh blinkMesh = avatar.FaceBlinkMesh != null ? avatar.FaceBlinkMesh.sharedMesh : null;

        return new BasisGenericAvatarData
        {
            AvatarEyePosition = avatar.AvatarEyePosition,
            AvatarMouthPosition = avatar.AvatarMouthPosition,
            EyeLiveliness = avatar.EyeLiveliness,
            EyeAttentiveness = avatar.EyeAttentiveness,
            FaceVisemeMovement = avatar.FaceVisemeMovement != null ? (int[])avatar.FaceVisemeMovement.Clone() : null,
            FaceVisemeProfiles = avatar.FaceVisemeProfiles != null ? (BasisVisemeProfile[])avatar.FaceVisemeProfiles.Clone() : null,
            FaceVisemeDrive = avatar.FaceVisemeDrive,
            BlinkViseme = avatar.BlinkViseme != null ? (int[])avatar.BlinkViseme.Clone() : null,
            LaughterBlendTarget = avatar.laughterBlendTarget,
            AnimatorHumanScale = avatar.AnimatorHumanScale,
            FaceVisemeMeshPath = avatar.FaceVisemeMesh != null ? GetPathRelativeTo(root, avatar.FaceVisemeMesh.transform) : null,
            FaceBlinkMeshPath = avatar.FaceBlinkMesh != null ? GetPathRelativeTo(root, avatar.FaceBlinkMesh.transform) : null,
            FaceVisemeShapeNames = CaptureShapeNames(visemeMesh, avatar.FaceVisemeMovement),
            BlinkShapeNames = CaptureShapeNames(blinkMesh, avatar.BlinkViseme),
            LaughterShapeName = ShapeName(visemeMesh, avatar.laughterBlendTarget),
            RootNodeName = root.name,
            InactiveNodePaths = inactiveNodePaths.ToArray(),
            DisabledRendererPaths = disabledRendererPaths.ToArray(),
            HumanBones = humanBones.ToArray(),
            SkeletonBones = skeletonBones.ToArray(),
        };
    }

    private static string[] CaptureShapeNames(Mesh mesh, int[] shapeIndices)
    {
        if (mesh == null || shapeIndices == null || shapeIndices.Length == 0)
        {
            return null;
        }
        string[] names = new string[shapeIndices.Length];
        for (int Index = 0; Index < shapeIndices.Length; Index++)
        {
            names[Index] = ShapeName(mesh, shapeIndices[Index]);
        }
        return names;
    }

    private static string ShapeName(Mesh mesh, int shapeIndex)
    {
        return mesh != null && shapeIndex >= 0 && shapeIndex < mesh.blendShapeCount ? mesh.GetBlendShapeName(shapeIndex) : null;
    }

    /// <summary>
    /// Re-resolves the viseme/blink/laughter blendshape indices against the imported meshes by
    /// name. The sidecar rebuild adds only the driver shapes, so raw source-mesh indices are
    /// meaningless after import; a missing name resolves to -1, which every driver treats as
    /// "unused". Payloads without names (no shapes on the source) keep their raw values.
    /// </summary>
    public void RemapShapeIndicesByName(BasisAvatar avatar)
    {
        Mesh visemeMesh = avatar.FaceVisemeMesh != null ? avatar.FaceVisemeMesh.sharedMesh : null;
        if (visemeMesh != null && FaceVisemeShapeNames != null && avatar.FaceVisemeMovement != null)
        {
            int count = Mathf.Min(FaceVisemeShapeNames.Length, avatar.FaceVisemeMovement.Length);
            for (int Index = 0; Index < count; Index++)
            {
                avatar.FaceVisemeMovement[Index] = string.IsNullOrEmpty(FaceVisemeShapeNames[Index]) ? -1 : visemeMesh.GetBlendShapeIndex(FaceVisemeShapeNames[Index]);
            }
        }
        if (visemeMesh != null)
        {
            avatar.laughterBlendTarget = string.IsNullOrEmpty(LaughterShapeName) ? -1 : visemeMesh.GetBlendShapeIndex(LaughterShapeName);
        }
        Mesh blinkMesh = avatar.FaceBlinkMesh != null ? avatar.FaceBlinkMesh.sharedMesh : null;
        if (blinkMesh != null && BlinkShapeNames != null && avatar.BlinkViseme != null)
        {
            int count = Mathf.Min(BlinkShapeNames.Length, avatar.BlinkViseme.Length);
            for (int Index = 0; Index < count; Index++)
            {
                avatar.BlinkViseme[Index] = string.IsNullOrEmpty(BlinkShapeNames[Index]) ? -1 : blinkMesh.GetBlendShapeIndex(BlinkShapeNames[Index]);
            }
        }
    }

    private static void CaptureSkeletonRecursive(Transform root, Transform current, List<BasisSkeletonBoneEntry> skeletonBones, List<string> inactiveNodePaths)
    {
        skeletonBones.Add(new BasisSkeletonBoneEntry
        {
            Name = current.name,
            Position = current.localPosition,
            Rotation = current.localRotation,
            Scale = current.localScale,
        });
        if (!current.gameObject.activeSelf && current != root)
        {
            inactiveNodePaths.Add(GetPathRelativeTo(root, current));
        }
        int childCount = current.childCount;
        for (int Index = 0; Index < childCount; Index++)
        {
            CaptureSkeletonRecursive(root, current.GetChild(Index), skeletonBones, inactiveNodePaths);
        }
    }

    public HumanDescription ToHumanDescription()
    {
        int humanLength = HumanBones != null ? HumanBones.Length : 0;
        HumanBone[] human = new HumanBone[humanLength];
        for (int Index = 0; Index < humanLength; Index++)
        {
            BasisHumanBoneEntry entry = HumanBones[Index];
            human[Index] = new HumanBone
            {
                humanName = entry.HumanName,
                boneName = entry.BoneName,
                limit = new HumanLimit
                {
                    useDefaultValues = entry.UseDefaultValues,
                    min = entry.LimitMin,
                    max = entry.LimitMax,
                    center = entry.LimitCenter,
                    axisLength = entry.AxisLength,
                },
            };
        }

        int skeletonLength = SkeletonBones != null ? SkeletonBones.Length : 0;
        SkeletonBone[] skeleton = new SkeletonBone[skeletonLength];
        for (int Index = 0; Index < skeletonLength; Index++)
        {
            BasisSkeletonBoneEntry entry = SkeletonBones[Index];
            skeleton[Index] = new SkeletonBone
            {
                name = entry.Name,
                position = entry.Position,
                rotation = entry.Rotation,
                scale = entry.Scale,
            };
        }

        return new HumanDescription
        {
            human = human,
            skeleton = skeleton,
            armStretch = ArmStretch,
            legStretch = LegStretch,
            upperArmTwist = UpperArmTwist,
            lowerArmTwist = LowerArmTwist,
            upperLegTwist = UpperLegTwist,
            lowerLegTwist = LowerLegTwist,
            feetSpacing = FeetSpacing,
            hasTranslationDoF = HasTranslationDoF,
        };
    }

    /// <summary>
    /// Restores authored node active / renderer enabled state that glTF cannot represent.
    /// Missing paths are skipped — a mesh the exporter dropped is already invisible.
    /// </summary>
    public void ApplyNodeState(Transform root)
    {
        if (InactiveNodePaths != null)
        {
            for (int Index = 0; Index < InactiveNodePaths.Length; Index++)
            {
                Transform node = ResolveByPath(root, InactiveNodePaths[Index]);
                if (node != null)
                {
                    node.gameObject.SetActive(false);
                }
            }
        }
        if (DisabledRendererPaths != null)
        {
            for (int Index = 0; Index < DisabledRendererPaths.Length; Index++)
            {
                Transform node = ResolveByPath(root, DisabledRendererPaths[Index]);
                if (node != null && node.TryGetComponent(out Renderer renderer))
                {
                    renderer.enabled = false;
                }
            }
        }
    }

    /// <summary>
    /// Copies the captured wiring onto a freshly created BasisAvatar whose hierarchy came
    /// from the imported GLB. The Animator/humanoid Avatar are the caller's responsibility.
    /// </summary>
    public void ApplyWiring(BasisAvatar avatar, Transform root)
    {
        avatar.AvatarEyePosition = AvatarEyePosition;
        avatar.AvatarMouthPosition = AvatarMouthPosition;
        avatar.EyeLiveliness = EyeLiveliness;
        avatar.EyeAttentiveness = EyeAttentiveness;
        if (FaceVisemeMovement != null && FaceVisemeMovement.Length > 0)
        {
            avatar.FaceVisemeMovement = (int[])FaceVisemeMovement.Clone();
        }
        if (FaceVisemeProfiles != null && FaceVisemeProfiles.Length > 0)
        {
            avatar.FaceVisemeProfiles = (BasisVisemeProfile[])FaceVisemeProfiles.Clone();
        }
        // Only an authored config replaces the component's own defaults. A payload built before
        // these fields existed deserializes to an all-zero instance, and assigning that hands the
        // lip-sync backend "no smoothing" as though the creator had asked for it.
        if (FaceVisemeDrive != null && !FaceVisemeDrive.IsUnset)
        {
            avatar.FaceVisemeDrive = FaceVisemeDrive;
        }
        if (BlinkViseme != null && BlinkViseme.Length > 0)
        {
            avatar.BlinkViseme = (int[])BlinkViseme.Clone();
        }
        avatar.laughterBlendTarget = LaughterBlendTarget;
        avatar.AnimatorHumanScale = AnimatorHumanScale;

        Transform visemeNode = ResolveByPath(root, FaceVisemeMeshPath);
        if (visemeNode != null && visemeNode.TryGetComponent(out SkinnedMeshRenderer visemeMesh))
        {
            avatar.FaceVisemeMesh = visemeMesh;
        }
        Transform blinkNode = ResolveByPath(root, FaceBlinkMeshPath);
        if (blinkNode != null && blinkNode.TryGetComponent(out SkinnedMeshRenderer blinkMesh))
        {
            avatar.FaceBlinkMesh = blinkMesh;
        }
    }

    public static Transform ResolveByPath(Transform root, string path)
    {
        if (root == null || path == null)
        {
            return null;
        }
        if (path.Length == 0)
        {
            return root;
        }
        return root.Find(path);
    }

    public static string GetPathRelativeTo(Transform root, Transform target)
    {
        if (root == null || target == null)
        {
            return null;
        }
        if (target == root)
        {
            return string.Empty;
        }
        string path = target.name;
        Transform current = target.parent;
        while (current != null && current != root)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }
        // target was not actually under root
        if (current == null)
        {
            return null;
        }
        return path;
    }
}
