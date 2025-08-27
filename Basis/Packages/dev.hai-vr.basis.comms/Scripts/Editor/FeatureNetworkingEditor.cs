using UnityEditor;

namespace HVR.Basis.Comms.Editor
{
    [CustomEditor(typeof(FeatureNetworking))]
    public class FeatureNetworkingEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
        }
    }
}
