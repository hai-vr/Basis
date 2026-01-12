// ReSharper disable once RedundantUsingDirective
using UnityEngine;

namespace HVR.Basis.Comms.HVRUtility
{
    public static class HVRLogging
    {
#if HVR_HAS_BASIS_SDK
        public static void ProtocolError(string message) => BasisDebug.LogError(message, BasisDebug.LogTag.Avatar);
        public static void ProtocolWarning(string message) => BasisDebug.LogWarning(message, BasisDebug.LogTag.Avatar);
        public static void ProtocolAssetMismatch(string message) => BasisDebug.LogError(message, BasisDebug.LogTag.Avatar);
        public static void ProtocolDebug(string message) => BasisDebug.Log(message, BasisDebug.LogTag.Avatar);

        // Added by Vixxy
        public static void ProtocolAccident(string message) => BasisDebug.LogError(message, BasisDebug.LogTag.Avatar);
        public static void StateError(string message) => BasisDebug.LogError(message, BasisDebug.LogTag.Avatar);
        
#elif HVR_HAS_HVR_INTEGRATION
        public static void ProtocolError(string message) => HVR.Shared.HVRLogging.LogError(typeof(HVRLogging), message);
        public static void ProtocolWarning(string message) => HVR.Shared.HVRLogging.Log(typeof(HVRLogging), "[WARNING] " + message);
        public static void ProtocolAssetMismatch(string message) => HVR.Shared.HVRLogging.LogError(typeof(HVRLogging), message);
        public static void ProtocolDebug(string message) => HVR.Shared.HVRLogging.Log(typeof(HVRLogging), message);

        // Added by Vixxy
        public static void ProtocolAccident(string message) => HVR.Shared.HVRLogging.LogError(typeof(HVRLogging), message);
        public static void StateError(string message) => HVR.Shared.HVRLogging.LogError(typeof(HVRLogging), message);
        
#else 
        public static void ProtocolError(string message) => Debug.LogError(message);
        public static void ProtocolWarning(string message) => Debug.LogWarning(message);
        public static void ProtocolAssetMismatch(string message) => Debug.LogError(message);
        public static void ProtocolDebug(string message) => Debug.Log(message);

        // Added by Vixxy
        public static void ProtocolAccident(string message) => Debug.LogError(message);
        public static void StateError(string message) => Debug.LogError(message);
#endif
    }
}
