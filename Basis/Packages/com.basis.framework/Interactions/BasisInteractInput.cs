using Basis.Scripts.Device_Management.Devices;
using UnityEngine;
namespace Basis.Scripts.BasisSdk.Interactions
{
    [Tooltip("Both of the above are relative to object transforms, objects with larger colliders may have issues")]
    [System.Serializable]
    public struct BasisInteractInput
    {
        [HideInInspector]
        [field: System.NonSerialized]
        [SerializeReference]
        public BasisInput input;
        [SerializeReference]
        public BasisInteractableObject lastTarget;
        [SerializeField]
        public bool HasvalidRay;

        /// <summary>
        /// Tracks time since grip was last confirmed held. When detection is lost and grip
        /// appears released, the object is only dropped after this exceeds the grace threshold.
        /// Prevents false drops during fast VR hand movement.
        /// </summary>
        [System.NonSerialized]
        public float timeSinceGripConfirmed;

        public bool IsInput(BasisInput IsInputInput)
        {
            return input.UniqueDeviceIdentifier == IsInputInput.UniqueDeviceIdentifier;
        }
    }
}
