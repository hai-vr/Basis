using UnityEngine;

namespace HVR.Basis.Comms
{
    [CreateAssetMenu(fileName = "HVRAddress", menuName = "HVR.Basis/Comms")]
    public class HVRAddress : ScriptableObject
    {
        public string path;

        public string AsPath()
        {
            return !string.IsNullOrWhiteSpace(path) ? path : name;
        }
    }
}
