using HVR.Basis.Comms;
using UnityEngine;
#if HVR_VIXXY_IS_IN_BASIS
using Basis.Scripts.BasisSdk;
using HVR.Basis.Comms;
using HVR.Basis.Vixxy.Runtime;
#endif

namespace HVR.Vixxy
{
    public class VixxySetup
    {
        public static HVRVixxyOrchestrator EnsureInitialized(Component comp)
        {
#if HVR_VIXXY_IS_IN_BASIS
            var avatar = HVRCommsUtil.GetAvatar(comp);
            if (avatar == null)
            {
                return EnsureSceneHasNonAvatarOrchestrator();
            }

            var existingOrchestrator = avatar.GetComponentInChildren<HVRVixxyOrchestrator>(true);
            if (existingOrchestrator != null) return existingOrchestrator;

            var orchestrator = CreateOrchestrator(avatar.transform, "Generated__VixxyAvatar", avatar.transform);
            return orchestrator;
#else
            return EnsureSceneHasNonAvatarOrchestrator();
#endif
        }

        private static HVRVixxyOrchestrator EnsureSceneHasNonAvatarOrchestrator()
        {
            var existingOrchestrators = Object.FindObjectsByType<HVRVixxyOrchestrator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var existingOrchestrator in existingOrchestrators)
            {
#if HVR_VIXXY_IS_IN_BASIS
                var isNotInsideAvatar = HVRCommsUtil.GetAvatar(existingOrchestrator) == null;
                if (isNotInsideAvatar)
                {
                    return existingOrchestrator;
                }
#else
                    return existingOrchestrator;
#endif
            }

            var sceneOrchestrator = CreateOrchestrator(null, "Generated__VixxyScene", null);
            return sceneOrchestrator;
        }

        private static HVRVixxyOrchestrator CreateOrchestrator(Transform contextNullable, string name, Transform parentNullable)
        {
            var go = new GameObject(name);
            if (parentNullable != null)
            {
                go.transform.SetParent(parentNullable);
            }
            go.SetActive(false);
            var gadgetRepository = go.AddComponent<HVRGadgetRepository>();
            var sceneOrchestrator = go.AddComponent<HVRVixxyOrchestrator>();
            sceneOrchestrator.acquisitionService = AcquisitionService.SceneInstance;
            sceneOrchestrator.gadgetRepository = gadgetRepository;
            sceneOrchestrator.context = contextNullable;
            if (contextNullable != null)
            {
#if HVR_VIXXY_IS_IN_BASIS
                var networking = go.AddComponent<HVRVixxyBasisAvatarNetworking>();
                networking.orchestrator = sceneOrchestrator;
                networking.avatar = contextNullable.GetComponent<BasisAvatar>();
                sceneOrchestrator.networking = networking;
#endif
            }
            go.SetActive(true);
            return sceneOrchestrator;
        }
    }
}
