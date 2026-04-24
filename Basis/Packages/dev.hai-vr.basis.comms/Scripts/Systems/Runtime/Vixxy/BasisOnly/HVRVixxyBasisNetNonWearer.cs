#if HVR_VIXXY_IS_IN_BASIS
using Basis.Network.Core;
using HVR.Basis.Comms.HVRUtility;

namespace HVR.Basis.Comms.Vixxy
{
    internal class HVRNonWearer : IHVRVixxyBasisNet
    {
        private readonly HVRVixxyBasisAvatarNetworking _vixxyNet;
        private readonly byte[] Buffer_RequestState_NW_to_W = { IHVRVixxyBasisNet.RequestState_NW_to_W };

        public HVRNonWearer(HVRVixxyBasisAvatarNetworking vixxyNet)
        {
            _vixxyNet = vixxyNet;
        }

        public void OnNetworkInitialized()
        {
            _vixxyNet.SubmitReliable(Buffer_RequestState_NW_to_W);
        }

        public void OnNetworkMessageReceived(HVRAvatarContextualUser RemoteUser, byte[] unsafeBuffer, DeliveryMethod DeliveryMethod)
        {
            if (RemoteUser.IsWearer)
            {
                ProceedWithDecoding(unsafeBuffer);
            }
            else
            {
                HVRLogging.ProtocolError("Non-wearers cannot receive this message from other non-wearers.");
            }
        }

        public void OnNetworkMessageServerReductionSystem(byte[] unsafeBuffer)
        {
            ProceedWithDecoding(unsafeBuffer);
        }

        private void ProceedWithDecoding(byte[] unsafeBuffer)
        {
            if (unsafeBuffer.Length == 0)
            {
                HVRLogging.ProtocolError("Buffer cannot be empty.");
                return;
            }

            byte packetId = unsafeBuffer[0];
            switch (packetId)
            {
                case IHVRVixxyBasisNet.SubmitFullSnapshot_W_to_NW:
                {
                    var TODO_DERIVE_BUFFER_LENGTH = 12345;
                    if (unsafeBuffer.Length != TODO_DERIVE_BUFFER_LENGTH)
                    {
                        // HVRLogging.ProtocolError_IncorrectFixedBufferLength(unsafeBuffer, TODO_DERIVE_BUFFER_LENGTH);
                        return;
                    }

                    // TODO: We should decode the buffer right there. Otherwise we have to check the buffer size validity down there.
                    // var arraySegment = HVRLogging.SubBuffer(unsafeBuffer);
                    object snapshot = null; // TODO: Decode
                    _vixxyNet.NonWearer_ProcessFullSnapshot(snapshot);
                    break;
                }
                default:
                    HVRLogging.ProtocolError("Unknown packet ID.");
                    break;
            }
        }
    }
}
#endif
