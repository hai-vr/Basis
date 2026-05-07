using UnityEngine;

namespace HVR.Basis.Comms
{
    public class HVRTestComponent : MonoBehaviour
    {
        public Material[] materials;
        public bool[] isSupported;
        public Transform nanify;

        private void OnEnable()
        {
            if (nanify != null) nanify.localScale = new Vector3(float.NaN, float.NaN, float.NaN);

            var sharedMaterials = GetComponent<Renderer>().sharedMaterials;

            materials = new Material[sharedMaterials.Length];
            isSupported = new bool[sharedMaterials.Length];
            for (var i = 0; i < sharedMaterials.Length; i++)
            {
                var shader = sharedMaterials[i].shader;

                materials[i] = sharedMaterials[i];
                isSupported[i] = shader.isSupported;

                BasisDebug.Log($"Shader: {shader.name} with pass count {shader.renderQueue}");

                var propertyCount = shader.GetPropertyCount();
                for (var propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
                {
                    BasisDebug.Log($"Property {propertyIndex}: {shader.GetPropertyDescription(propertyIndex)}");
                    var propertyAttributes = shader.GetPropertyAttributes(propertyIndex);
                    if (propertyAttributes != null)
                    {
                        foreach (var propertyAttribute in propertyAttributes)
                        {
                            BasisDebug.Log($"- Property {propertyIndex}: {propertyAttribute}");
                        }
                    }
                }
            }
        }
    }
}
