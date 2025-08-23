using Basis.Scripts.BasisSdk;
using Basis.Scripts.Behaviour;
using System;
using UnityEngine;
namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/Internal/Avatar Comms")]
    public class HVRAvatarComms : BasisAvatarMonoBehaviour
    {
        private const int BytesPerGuid = 16;

        [HideInInspector] [SerializeField] private BasisAvatar avatar;
        [HideInInspector] [SerializeField] private FeatureNetworking featureNetworking;

        private void Awake()
        {
            if (avatar == null)
            {
                avatar = CommsUtil.GetAvatar(this);
            }
            if (featureNetworking == null)
            {
                featureNetworking = CommsUtil.FeatureNetworkingFromAvatar(avatar);
            }
            if (avatar == null || featureNetworking == null)
            {
                throw new InvalidOperationException("Broke assumption: Avatar and/or FeatureNetworking cannot be found.");
            }
        }

        internal static ArraySegment<byte> SubBuffer(byte[] unsafeBuffer)
        {
            return new ArraySegment<byte>(unsafeBuffer, 1, unsafeBuffer.Length - 1);
        }

        internal static void ProtocolError(string message) => BasisDebug.LogError(message, BasisDebug.LogTag.Avatar);
        internal static void ProtocolWarning(string message) => BasisDebug.LogWarning(message, BasisDebug.LogTag.Avatar);
        internal static void ProtocolAssetMismatch(string message) => BasisDebug.LogError(message, BasisDebug.LogTag.Avatar);
        internal static void ProtocolDebug(string message) => BasisDebug.Log(message, BasisDebug.LogTag.Avatar);
    }
}
