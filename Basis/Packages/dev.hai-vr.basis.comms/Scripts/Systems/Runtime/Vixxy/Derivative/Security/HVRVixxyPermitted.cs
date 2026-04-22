using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using System.Linq;
using UnityEngine.UI;

namespace HVR.Vixxy.Runtime
{
    public class HVRVixxyPermitted
    {
        private static readonly List<Type> PermittedTypes = new()
        {
            typeof(Transform),
            typeof(GameObject),
            // Renderers
            typeof(MeshRenderer),
            typeof(SkinnedMeshRenderer),
            typeof(TrailRenderer),
            typeof(ParticleSystemRenderer),
            // Particle System
            typeof(ParticleSystem),
            // Constraints
            typeof(ParentConstraint),
            typeof(PositionConstraint),
            typeof(RotationConstraint),
            typeof(ScaleConstraint),
            typeof(AimConstraint),
            typeof(LookAtConstraint),
            // Colliders
            typeof(SphereCollider),
            typeof(CapsuleCollider),
            typeof(BoxCollider),
            typeof(MeshCollider),
            // Physics
            typeof(Rigidbody),
            typeof(ConfigurableJoint),
            typeof(HingeJoint),
            // UI
            typeof(RectTransform),
            typeof(Canvas),
            typeof(Text),
            typeof(Image),
        };

        private static readonly List<string> PermittedTypeNames = new()
        {
            // Renderers
            "UnityEngine.Rendering.Universal.DecalProjector",
            // Jiggle
            "GatorDragonGames.JigglePhysics.JiggleRig",
            // UI
            "TMPro.TextMeshPro",
            "TMPro.TextMeshProUGUI",
        };

        private static readonly HashSet<string> RuntimePermittedTypeNames;

        static HVRVixxyPermitted()
        {
            RuntimePermittedTypeNames = PermittedTypes
                .Select(type => type.FullName)
                .Concat(PermittedTypeNames)
                .ToHashSet();
        }
        
        public static bool IsPermitted(string typeName) => RuntimePermittedTypeNames.Contains(typeName);

        public static bool IsTypeOfPropertyValuePermitted(HVRVixxyPropertyBase property)
        {
            return property is HVRVixxyPropertyFloat
                or HVRVixxyPropertyVector4
                or HVRVixxyPropertyVector3
                or HVRVixxyPropertyMaterial
                or HVRVixxyPropertyMesh
                or HVRVixxyPropertyQuaternion
                or HVRVixxyPropertyBool
                or HVRVixxyPropertyColor;
        }
    }
}