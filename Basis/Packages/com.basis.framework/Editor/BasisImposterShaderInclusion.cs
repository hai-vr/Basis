using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Basis.Scripts.Editor
{
    /// <summary>
    /// The avatar imposter material is created at runtime through Shader.Find, which in a
    /// player only resolves shaders the build kept. Nothing references the imposter shader
    /// from a scene, prefab or material, so put it in Always Included Shaders.
    /// </summary>
    public sealed class BasisImposterShaderInclusion : IPreprocessBuildWithReport
    {
        public const string ImposterShaderName = "Basis/AvatarImposter";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => EnsureIncluded();

        [MenuItem("Basis/Avatar/Include Imposter Shader")]
        private static void EnsureIncluded()
        {
            Shader shader = Shader.Find(ImposterShaderName);
            if (shader == null)
            {
                Debug.LogError($"[BasisImposter] Shader '{ImposterShaderName}' not found — distance imposters will not render in builds.");
                return;
            }

            SerializedObject graphics = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            SerializedProperty included = graphics.FindProperty("m_AlwaysIncludedShaders");
            if (included == null)
            {
                Debug.LogError($"[BasisImposter] Could not reach Always Included Shaders — add '{ImposterShaderName}' by hand under Project Settings > Graphics.");
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
            Debug.Log($"[BasisImposter] Added '{ImposterShaderName}' to Always Included Shaders.");
        }
    }
}
