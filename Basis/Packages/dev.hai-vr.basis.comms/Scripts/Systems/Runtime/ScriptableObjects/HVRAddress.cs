using UnityEngine;

namespace HVR.Basis.Comms
{
    [CreateAssetMenu(menuName = "HVR.Basis/Comms", fileName = "HVRAddress")]
    public class HVRAddress : ScriptableObject
    {
        public string path;

        public string AsPath()
        {
            return !string.IsNullOrWhiteSpace(path) ? path : name;
        }
    }
}
