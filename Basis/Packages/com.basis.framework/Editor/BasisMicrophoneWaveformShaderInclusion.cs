using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Basis.Scripts.Editor
{
    /// <summary>
    /// The microphone waveform material is created at runtime through Shader.Find, which in a
    /// player only resolves shaders the build kept. Nothing references the waveform shader from
    /// a scene, prefab or material, so put it in Always Included Shaders.
    /// </summary>
    public sealed class BasisMicrophoneWaveformShaderInclusion : IPreprocessBuildWithReport
    {
        public const string WaveformShaderName = "Basis/UI/MicrophoneWaveform";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => EnsureIncluded();

        [MenuItem("Basis/Build/Shaders/Include Microphone Waveform Shader", false, 362)]
        private static void EnsureIncluded()
        {
            Shader shader = Shader.Find(WaveformShaderName);
            if (shader == null)
            {
                Debug.LogError($"[BasisWaveform] Shader '{WaveformShaderName}' not found — the microphone waveform will not render in builds.");
                return;
            }

            SerializedObject graphics = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            SerializedProperty included = graphics.FindProperty("m_AlwaysIncludedShaders");
            if (included == null)
            {
                Debug.LogError($"[BasisWaveform] Could not reach Always Included Shaders — add '{WaveformShaderName}' by hand under Project Settings > Graphics.");
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
            Debug.Log($"[BasisWaveform] Added '{WaveformShaderName}' to Always Included Shaders.");
        }
    }
}
