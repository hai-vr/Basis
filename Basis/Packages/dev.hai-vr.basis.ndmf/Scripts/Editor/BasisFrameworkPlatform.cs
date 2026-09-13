using System;
using System.Collections.Generic;
using System.Linq;
using Basis.Scripts.BasisSdk;
using nadena.dev.ndmf.platform;
using UnityEditor;
using UnityEngine;
#if BASISNDMF_JIGGLERIG_IS_INSTALLED
using GatorDragonGames.JigglePhysics;
#endif
using nadena.dev.ndmf.multiplatform.components;
using Unity.Mathematics;

namespace HVR.Basis.NDMF
{
    /// The class is called BasisFrameworkPlatform to follow NDMF's naming convention (INDMFPlatformProvider). Yes, we know Basis is not a platform.
    [NDMFPlatformProvider]
    public class BasisFrameworkPlatform : INDMFPlatformProvider
    {
        public static readonly INDMFPlatformProvider Instance = new BasisFrameworkPlatform();

        private static readonly string[] OurVisemesIndexedByMovement = {
            CommonAvatarInfo.Viseme_Silence, CommonAvatarInfo.Viseme_PP, CommonAvatarInfo.Viseme_FF, CommonAvatarInfo.Viseme_TH,
            CommonAvatarInfo.Viseme_DD, CommonAvatarInfo.Viseme_kk, CommonAvatarInfo.Viseme_CH, CommonAvatarInfo.Viseme_SS,
            CommonAvatarInfo.Viseme_nn, CommonAvatarInfo.Viseme_RR, CommonAvatarInfo.Viseme_aa, CommonAvatarInfo.Viseme_E,
            CommonAvatarInfo.Viseme_ih, CommonAvatarInfo.Viseme_oh, CommonAvatarInfo.Viseme_ou
        };

        public string QualifiedName => "org.basisvr.basis-framework";
        public string DisplayName => "Basis Framework";
        public Texture2D? Icon => null;
        public Type AvatarRootComponentType => typeof(BasisAvatar);
        public bool HasNativeConfigData => true;

        public BuildUIElement? CreateBuildUI()
        {
            return new BasisFrameworkBuildUI();
        }

        public CommonAvatarInfo ExtractCommonAvatarInfo(GameObject avatarRoot)
        {
            var basisAvatar = avatarRoot.GetComponent<BasisAvatar>();

            var cai = new CommonAvatarInfo();
            // The following is based on nadena.dev.ndmf.vrchat.VRChatPlatform
            cai.EyePosition = avatarRoot.transform.InverseTransformVector(new Vector3(0, basisAvatar.AvatarEyePosition.x, basisAvatar.AvatarEyePosition.y));
            if (basisAvatar.FaceVisemeMesh != null && basisAvatar.FaceVisemeMesh.sharedMesh != null)
            {
                cai.VisemeRenderer = basisAvatar.FaceVisemeMesh;

                var sharedMesh = basisAvatar.FaceVisemeMesh.sharedMesh;
                for (var movement = 0; movement < OurVisemesIndexedByMovement.Length; movement++)
                {
                    var ourViseme = OurVisemesIndexedByMovement[movement];
                    if (TryGet(sharedMesh, basisAvatar.FaceVisemeMovement[movement], out var blendShape)) cai.VisemeBlendshapes[ourViseme] = blendShape;
                }
            }

            return cai;
        }

        public void InitFromCommonAvatarInfo(GameObject avatarRoot, CommonAvatarInfo cai)
        {
            if (!avatarRoot.TryGetComponent<BasisAvatar>(out var basisAvatar))
            {
                basisAvatar = avatarRoot.AddComponent<BasisAvatar>();
                // The following is based on nadena.dev.ndmf.vrchat.VRChatPlatform:
                // Initialize array SerializeFields with empty array instances
                EditorUtility.CopySerialized(basisAvatar, basisAvatar);
            }

            if (cai.EyePosition != null)
            {
                var transformVector = avatarRoot.transform.TransformVector(cai.EyePosition.Value);
                basisAvatar.AvatarEyePosition = new Vector2(transformVector.y, transformVector.z);
            }

            if (cai.VisemeRenderer != null && cai.VisemeRenderer.sharedMesh != null)
            {
                basisAvatar.FaceVisemeMesh = cai.VisemeRenderer;

                var sharedMesh = cai.VisemeRenderer.sharedMesh;
                var blendShapeNames = Enumerable.Range(0, basisAvatar.FaceVisemeMesh.sharedMesh.blendShapeCount)
                    .Select(i => sharedMesh.GetBlendShapeName(i))
                    .ToList();
                for (var visemeMovementIndex = 0; visemeMovementIndex < OurVisemesIndexedByMovement.Length; visemeMovementIndex++)
                {
                    var caiViseme = OurVisemesIndexedByMovement[visemeMovementIndex];
                    if (cai.VisemeBlendshapes.TryGetValue(caiViseme, out var caiBlendShapeName))
                    {
                        var blendShapeIndex = blendShapeNames.IndexOf(caiBlendShapeName);
                        if (blendShapeIndex >= 0)
                        {
                            basisAvatar.FaceVisemeMovement[visemeMovementIndex] = blendShapeIndex;
                        }
                    }
                }
            }
        }

        public bool CanInitFromCommonAvatarInfo(GameObject avatarRoot, CommonAvatarInfo info)
        {
            return true;
        }

        private bool TryGet(Mesh mesh, int blendShapeIndex, out string name)
        {
            if (blendShapeIndex < 0 || blendShapeIndex >= mesh.blendShapeCount)
            {
                name = null;
                return false;
            }

            name = mesh.GetBlendShapeName(blendShapeIndex);
            return true;
        }

#if BASISNDMF_JIGGLERIG_IS_INSTALLED
        public void GeneratePortableComponents(GameObject root, bool useUndo)
        {
            Dictionary<Transform, PortableDynamicBone> explicitDynBones = new();
            Dictionary<ColliderEqualityCheck, PortableDynamicBoneCollider> equalityCheckToPortableCollider = new();

            foreach (var pdb in root.GetComponentsInChildren<PortableDynamicBone>(true))
            {
                if (pdb.Root != null) explicitDynBones[pdb.Root] = pdb;
            }
            
            var allJiggleRig = root.GetComponentsInChildren<JiggleRig>(true);
            foreach (var jiggleRig in allJiggleRig)
            {
                foreach (var colliderSer in jiggleRig.GetJiggleRigData().jiggleColliders)
                {
                    var collider = colliderSer.collider;

                    if (colliderSer.transform != null)
                    {
                        var key = ToEqualityCheck(colliderSer.transform, collider);
                        if (!equalityCheckToPortableCollider.ContainsKey(key))
                        {
                            var portable = jiggleRig.gameObject.AddComponent<PortableDynamicBoneCollider>();
                            equalityCheckToPortableCollider[key] = portable;
                            portable.ColliderType = collider.type switch
                            {
                                JiggleCollider.JiggleColliderType.Sphere => PortableDynamicColliderType.Sphere,
                                JiggleCollider.JiggleColliderType.Capsule => PortableDynamicColliderType.Capsule,
                                _ => PortableDynamicColliderType.Sphere
                            };
                            portable.Radius = collider.worldRadius;
                            portable.Height = collider.worldHeight;
                            portable.PositionOffset = collider.localOffset;
                            if (collider.type == JiggleCollider.JiggleColliderType.Capsule && collider.capsuleAxis != JiggleCollider.CapsuleAxis.Y)
                            {
                                portable.RotationOffset = collider.capsuleAxis switch
                                {
                                    JiggleCollider.CapsuleAxis.X => Quaternion.Euler(0f, 0f, 90f),
                                    JiggleCollider.CapsuleAxis.Z => Quaternion.Euler(90f, 0f, 0f),
                                    _ => Quaternion.identity
                                };
                            }
                            else
                            {
                                portable.RotationOffset = Quaternion.identity;
                            }

                            portable.InsideBounds = false;
                        }
                    }
                }
            }

            foreach (var jr in allJiggleRig)
            {
                var jrData = jr.GetJiggleRigData();
                var jrCollisionRadius = jrData.jiggleTreeInputParameters.collisionRadius;
                
                var rootBone = jrData.rootBone ?? jr.transform;
                var portable = explicitDynBones.GetValueOrDefault(rootBone) ??
                               jr.gameObject.AddComponent<PortableDynamicBone>();

                portable.enabled = jr.enabled;
                portable.BaseRadius.WeakSet(jrCollisionRadius.value);
                portable.IsGrabbable.WeakSet(!jrData.lockFromGrabbing);
                portable.IgnoreSelf.WeakSet(false);
                portable.IgnoreTransforms.WeakSet(jrData.excludedTransforms.Where(transform => transform != null).ToList());
                
                portable.Root = rootBone;
                portable.Colliders.WeakSet(jrData.jiggleColliders
                    .Where(cSer => cSer.transform != null)
                    .Select(cSer => equalityCheckToPortableCollider.GetValueOrDefault(ToEqualityCheck(cSer.transform, cSer.collider)))
                    .Where(cCheck => cCheck != null)
                    .ToList());
                portable.IgnoreMultiChild.WeakSet(jrData.excludeRoot); // TODO: Is this the correct thing?

                if (jrCollisionRadius.curveEnabled)
                {
                    portable.RadiusCurve.WeakSet(jrCollisionRadius.curve);
                }
            }
        }
        
        private struct ColliderEqualityCheck : IEquatable<ColliderEqualityCheck>
        {
            public Transform root;
            
            public bool enabled;
            public JiggleCollider.JiggleColliderType type;
            public float radius;
            public float worldRadius;
            public float height;
            public float worldHeight;
            public JiggleCollider.CapsuleAxis capsuleAxis;
            public float3 localOffset;

            public bool Equals(ColliderEqualityCheck other)
            {
                return Equals(root, other.root) && enabled == other.enabled && type == other.type && radius.Equals(other.radius) && worldRadius.Equals(other.worldRadius) && height.Equals(other.height) && worldHeight.Equals(other.worldHeight) && capsuleAxis == other.capsuleAxis && localOffset.Equals(other.localOffset);
            }

            public override bool Equals(object obj)
            {
                return obj is ColliderEqualityCheck other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (root != null ? root.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ enabled.GetHashCode();
                    hashCode = (hashCode * 397) ^ (int)type;
                    hashCode = (hashCode * 397) ^ radius.GetHashCode();
                    hashCode = (hashCode * 397) ^ worldRadius.GetHashCode();
                    hashCode = (hashCode * 397) ^ height.GetHashCode();
                    hashCode = (hashCode * 397) ^ worldHeight.GetHashCode();
                    hashCode = (hashCode * 397) ^ (int)capsuleAxis;
                    hashCode = (hashCode * 397) ^ localOffset.GetHashCode();
                    return hashCode;
                }
            }
        }

        private static ColliderEqualityCheck ToEqualityCheck(Transform root, JiggleCollider collider)
        {
            return new ColliderEqualityCheck
            {
                root = root,
                enabled = collider.enabled,
                type = collider.type,
                radius = collider.radius,
                worldRadius = collider.worldRadius,
                height = collider.height,
                worldHeight = collider.worldHeight,
                capsuleAxis = collider.capsuleAxis,
                localOffset = collider.localOffset
            };
        }
#endif
    }

    public class BasisFrameworkBuildUI : BuildUIElement
    {
    }
}
