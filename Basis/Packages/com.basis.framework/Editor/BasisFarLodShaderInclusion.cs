using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Basis.Scripts.Editor
{
    /// <summary>
    /// The avatar far LOD material is created at runtime through Shader.Find, which in a
    /// player only resolves shaders the build kept. Nothing references the far LOD shader
    /// from a scene, prefab or material, so put it in Always Included Shaders.
    /// </summary>
    public sealed class BasisFarLodShaderInclusion : IPreprocessBuildWithReport
    {
        public const string FarLodShaderName = "Basis/AvatarFarLod";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => EnsureIncluded();

        [MenuItem("Basis/Build/Shaders/Include Far LOD Shader", false, 361)]
        private static void EnsureIncluded()
        {
            Shader shader = Shader.Find(FarLodShaderName);
            if (shader == null)
            {
                Debug.LogError($"[BasisFarLod] Shader '{FarLodShaderName}' not found — distance far LODs will not render in builds.");
                return;
            }

            SerializedObject graphics = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            SerializedProperty included = graphics.FindProperty("m_AlwaysIncludedShaders");
            if (included == null)
            {
                Debug.LogError($"[BasisFarLod] Could not reach Always Included Shaders — add '{FarLodShaderName}' by hand under Project Settings > Graphics.");
                return;
            }

            for (int Index = 0; Index < included.arraySize; Index++)
            {
                if (included.GetArrayElementAtIndex(Index).objectReferenceValue == shader)
                {
                    return;
                }
            }

            included.InsertArrayElementAtIndex(included.arraySize);
            included.GetArrayElementAtIndex(included.arraySize - 1).objectReferenceValue = shader;
            graphics.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"[BasisFarLod] Added '{FarLodShaderName}' to Always Included Shaders.");
        }
    }
}
