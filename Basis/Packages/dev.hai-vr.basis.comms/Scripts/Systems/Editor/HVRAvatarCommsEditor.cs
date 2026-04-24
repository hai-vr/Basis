using Basis.Scripts.BasisSdk;
using UnityEditor;
using UnityEngine;

namespace HVR.Basis.Comms.Editor
{
    [CustomEditor(typeof(HVRAvatarComms))]
    public class HVRAvatarCommsEditor : UnityEditor.Editor
    {
        private const string HVRNetworkingPrefabGuid = "630d3429b35a4c844b56751eb1d77d90";

        public override void OnInspectorGUI()
        {
            var my = (HVRAvatarComms)target;

            EditorGUILayout.HelpBox("This prefab was added automatically because your avatar contains a component that depends on the HVR Avatar Communication module.", MessageType.Info);
            if (Application.isPlaying)
            {
                if (my._streamedLateInit != null)
                {
                    EditorGUILayout.LabelField($"Mutualized / lower / upper / current ({my._ranges.Count} items)", EditorStyles.boldLabel);
                    for (var index = 0; index < my._ranges.Count; index++)
                    {
                        var range = my._ranges[index];
                        var current = my._streamedLateInit.current[index];

                        var address = HVRAddressRegistry.ResolveKnownAddressFromId(range.address);
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"[#{index}]", GUILayout.Width(50));
                        EditorGUILayout.TextArea(address);
                        EditorGUILayout.FloatField(range.lower, GUILayout.Width(50));
                        EditorGUILayout.FloatField(range.upper, GUILayout.Width(50));
                        EditorGUILayout.LabelField($"=", GUILayout.Width(10));
                        EditorGUILayout.FloatField(Mathf.Lerp(range.lower, range.upper, current), GUILayout.Width(50));
                        EditorGUILayout.EndHorizontal();
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Network not ready; host or connect to a server first.", MessageType.Info);
                }
            }
        }

        public static void EnsureAvatarHasPrefab(Transform myTransform)
        {
            var avi = myTransform.GetComponentInParent<BasisAvatar>(true);
            if (avi == null) return;

            var comms = avi.GetComponentInChildren<HVRAvatarComms>(true);
            var carrier = avi.GetComponentInChildren<HVRNetworkingCarrier>(true);
            if (comms == null || carrier == null)
            {
                if (GUID.TryParse(HVRNetworkingPrefabGuid, out var guid))
                {
                    var instance = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetByGUID<GameObject>(guid), avi.transform);
                    EditorUtility.SetDirty(instance);
                }
            }
        }
    }
}
