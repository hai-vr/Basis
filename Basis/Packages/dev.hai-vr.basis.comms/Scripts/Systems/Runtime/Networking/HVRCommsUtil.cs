using System;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
#if HVR_HAS_BASIS_SDK
using Basis.Scripts.BasisSdk;
#endif
#if HVR_HAS_HVR_INTEGRATION
using HVR.Integration.UGC;
#endif

namespace HVR.Basis.Comms
{
    public class HVRCommsUtil
    {
        public static T GetOrCreateSceneInstance<T>(ref T instance) where T : Component
        {
            if (instance != null) return instance;

            var go = new GameObject($"HVR.{typeof(T).Name}");
            Object.DontDestroyOnLoad(go);
            instance = go.AddComponent<T>();

            return instance;
        }

        public static Component GetAvatar(Component component)
        {
#if HVR_HAS_BASIS_SDK
            return component.GetComponentInParent<BasisAvatar>(true);
#elif HVR_HAS_HVR_INTEGRATION
            return component.GetComponentInParent<HVRUGCAvatar>(true);
#else
            throw new NotImplementedException("TODO: GetAvatar from HVR");
#endif
        }

        /// Semantically used to sanitize a serializable field of objects provided by an End User.<br/>
        /// Given a nullable array of Unity Objects that may contain null-Destroy Objects,
        /// return a non-null array of Unity Objects that does not contain null-Destroy Objects.
        public static T[] SlowSanitizeEndUserProvidedObjectArray<T>(T[] objectsNullable) where T : Object
        {
            if (objectsNullable == null) return Array.Empty<T>();

            return objectsNullable.Where(t => t).ToArray();
        }

        /// Semantically used to sanitize a serializable field of structs provided by an End User.<br/>
        /// Returns itself, or an empty array if the parameter is null.
        public static T[] SlowSanitizeEndUserProvidedStructArray<T>(T[] structuresNullable) where T : struct
        {
            if (structuresNullable == null) return Array.Empty<T>();

            return structuresNullable;
        }

        public static object HookAvatarReady(Component component, Action<bool> onAvatarReady)
        {
            var avatar = HVRCommsUtil.GetAvatar(component);
#if HVR_HAS_BASIS_SDK
            BasisAvatar.OnReady avatarReady = b => onAvatarReady(b);
            (avatar as BasisAvatar).OnAvatarReady += avatarReady;
            return avatarReady;
#else
            return null; // TODO
#endif
        }

        public static void UnhookAvatarReady(Component component, object objNullable)
        {
            if (objNullable == null) return;
            
            var avatar = HVRCommsUtil.GetAvatar(component);
#if HVR_HAS_BASIS_SDK
            (avatar as BasisAvatar).OnAvatarReady -= objNullable as BasisAvatar.OnReady;
#else
            // TODO
#endif
        }

        public static ushort LinkedAvatarIdOf(Component avatar)
        {
#if HVR_HAS_BASIS_SDK
            return (avatar as BasisAvatar).LinkedPlayerID;
#else
            throw new NotImplementedException("TODO: LinkedAvatarIdOf");
#endif
        }
    }
}
