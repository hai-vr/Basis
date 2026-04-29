using System;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/Internal/Low Frequency Feature")]
    public class LowFrequencyFeature : MonoBehaviour
    {
        public IHVRTransmitter transmitter;
        public bool isWearer;
        public event DataChanged OnDataChanged;
        public delegate void DataChanged(int index, float value);

        public void OnPacketReceived(ArraySegment<byte> data)
        {
            throw new NotImplementedException();
        }

        public void OnResyncEveryoneRequested()
        {
            throw new NotImplementedException();
        }

        public void OnResyncRequested(ushort[] whoAsked)
        {
            throw new NotImplementedException();
        }

        public void InitializeNormalizedValues(float[] buildNeutralNormalizedValues)
        {
            throw new NotImplementedException();
        }
    }
}
