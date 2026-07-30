using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Basis.Editor
{
    /// <summary>
    /// The Generic (glTF) avatar fallback imports GLBs at runtime; glTFast resolves its
    /// materials via Shader.Find, which only sees shaders compiled into the player. Nothing in
    /// the project references the glTFast shader graphs directly, so without this they get
    /// stripped and every generic avatar renders with the error shader. Adding them to
    /// GraphicsSettings' Always Included Shaders before each player build keeps them available.
    /// </summary>
    public class BasisGltfShaderInclusion : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        // Shader.Find names of glTFast's shader graphs (URP/HDRP pick the matching subvariant
        // by these same names). Missing entries are skipped quietly — e.g. the clearcoat graph
        // only exists on some glTFast versions.
        private static readonly string[] GltfShaderNames =
        {
            "Shader Graphs/glTF-pbrMetallicRoughness",
            "Shader Graphs/glTF-pbrSpecularGlossiness",
            "Shader Graphs/glTF-unlit",
            "Shader Graphs/glTF-pbrMetallicRoughness-Clearcoat",
        };

        public void OnPreprocessBuild(BuildReport report)
        {
            EnsureGltfShadersAlwaysIncluded();
        }

        [MenuItem("Basis/Build/Shaders/Include glTF Shaders", false, 360)]
        public static void EnsureGltfShadersAlwaysIncluded()
        {
            var graphicsSettings = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (graphicsSettings == null)
            {
                Debug.LogWarning("BasisGltfShaderInclusion: could not load GraphicsSettings.asset.");
                return;
            }

            SerializedObject serialized = new SerializedObject(graphicsSettings);
            SerializedProperty alwaysIncluded = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (alwaysIncluded == null || !alwaysIncluded.isArray)
            {
                Debug.LogWarning("BasisGltfShaderInclusion: m_AlwaysIncludedShaders not found.");
                return;
            }

            HashSet<Shader> existing = new HashSet<Shader>();
            for (int Index = 0; Index < alwaysIncluded.arraySize; Index++)
            {
                if (alwaysIncluded.GetArrayElementAtIndex(Index).objectReferenceValue is Shader shader)
                {
                    existing.Add(shader);
                }
            }

            bool changed = false;
            foreach (string shaderName in GltfShaderNames)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null || existing.Contains(shader))
                {
                    continue;
                }
                int insertIndex = alwaysIncluded.arraySize;
                alwaysIncluded.InsertArrayElementAtIndex(insertIndex);
                alwaysIncluded.GetArrayElementAtIndex(insertIndex).objectReferenceValue = shader;
                existing.Add(shader);
                changed = true;
                Debug.Log($"BasisGltfShaderInclusion: added '{shaderName}' to Always Included Shaders.");
            }

            if (changed)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }
        }
    }
}
