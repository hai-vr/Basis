using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace SSGIURP.Tests
{
    public class ScreenSpaceGlobalIlluminationFeatureTests
    {
        private const string ShaderName = "Hidden/Lighting/ScreenSpaceGlobalIllumination";

        // Pass indices are hard-coded in the feature (Blitter.BlitCameraTexture(..., pass: N)), so the order is load-bearing.
        private static readonly string[] PassNames =
        {
            "Copy Direct Lighting",
            "Screen Space Global Illumination",
            "Temporal Reprojection",
            "Edge-Avoiding Spatial Denoise",
            "Temporal Stabilization",
            "Copy History Depth",
            "Combine GI",
            "Scene View Camera Motion Vectors",
            "Poisson Disk Recurrent Denoise",
            "Blit Color Texture",
            "Combine GI Add",
        };

        // Every screen-sized texture the passes read. Sampling any of these without the _X macros breaks stereo instancing.
        private static readonly Regex NonStereoScreenTextureRead = new Regex(
            @"\b(?:SAMPLE_TEXTURE2D_LOD|SAMPLE_TEXTURE2D|LOAD_TEXTURE2D_LOD|LOAD_TEXTURE2D)\s*\(\s*(?:_BlitTexture|_CameraDepthTexture|_GBuffer[0-3]|_MotionVectorTexture|_CameraBackDepthTexture|_CameraBackOpaqueTexture|_HistoryIndirectDiffuseTexture|_IndirectDiffuseTexture|_SSGI\w*Texture)\b",
            RegexOptions.Compiled);

        private static Shader FindShader()
        {
            Shader shader = Shader.Find(ShaderName);
            Assert.IsNotNull(shader, "shader not found: " + ShaderName);
            return shader;
        }

        private static string ShaderDirectory()
        {
            string assetPath = AssetDatabase.GetAssetPath(FindShader());
            Assert.IsFalse(string.IsNullOrEmpty(assetPath));
            return Path.GetDirectoryName(Path.GetFullPath(assetPath));
        }

        private static ScreenSpaceGlobalIlluminationURP CreateFeature()
        {
            // OnEnable calls Create(), which logs while no shader is assigned yet.
            LogAssert.ignoreFailingMessages = true;
            ScreenSpaceGlobalIlluminationURP feature = ScriptableObject.CreateInstance<ScreenSpaceGlobalIlluminationURP>();
            LogAssert.ignoreFailingMessages = false;
            return feature;
        }

        [Test]
        public void ShaderCompilesWithoutErrors()
        {
            Shader shader = FindShader();
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "shader has compile errors");
        }

        [Test]
        public void ShaderPassOrderMatchesTheHardCodedPassIndices()
        {
            Shader shader = FindShader();
            ShaderData.Subshader subshader = ShaderUtil.GetShaderData(shader).ActiveSubshader;
            Assert.AreEqual(PassNames.Length, subshader.PassCount);
            for (int i = 0; i < PassNames.Length; i++)
            {
                Assert.AreEqual(PassNames[i], subshader.GetPass(i).Name, "pass " + i);
            }
        }

        [Test]
        public void EveryPassSetsUpTheStereoEyeIndex()
        {
            string source = File.ReadAllText(Path.Combine(ShaderDirectory(), "ScreenSpaceGlobalIllumination.shader"));
            int setups = Regex.Matches(source, @"UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX\s*\(\s*input\s*\)").Count;
            Assert.AreEqual(PassNames.Length, setups, "each fragment program must call UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX");
        }

        [Test]
        public void ScreenTexturesAreOnlyReadThroughStereoAwareMacros()
        {
            foreach (string file in Directory.GetFiles(ShaderDirectory(), "*.*"))
            {
                if (!file.EndsWith(".hlsl") && !file.EndsWith(".shader"))
                    continue;

                string source = File.ReadAllText(file);
                MatchCollection matches = NonStereoScreenTextureRead.Matches(source);
                Assert.AreEqual(0, matches.Count, Path.GetFileName(file) + " reads a screen texture without a _X macro: " + (matches.Count > 0 ? matches[0].Value : ""));
            }
        }

        [Test]
        public void CombinePassesBlendOntoTheCameraTargetInsteadOfOverwritingIt()
        {
            string shader = File.ReadAllText(Path.Combine(ShaderDirectory(), "ScreenSpaceGlobalIllumination.shader"));
            int multiply = shader.IndexOf("Name \"Combine GI\"", System.StringComparison.Ordinal);
            int add = shader.IndexOf("Name \"Combine GI Add\"", System.StringComparison.Ordinal);
            Assert.Greater(multiply, 0);
            Assert.Greater(add, multiply);
            StringAssert.Contains("Blend Zero SrcColor", shader.Substring(multiply, 400));
            StringAssert.Contains("Blend One One", shader.Substring(add, 400));
        }

        private static string PassSource(string shader, string passName)
        {
            int start = shader.IndexOf("Name \"" + passName + "\"", System.StringComparison.Ordinal);
            Assert.Greater(start, 0, passName);
            int next = shader.IndexOf("Name \"", start + 1, System.StringComparison.Ordinal);
            return shader.Substring(start, (next < 0 ? shader.Length : next) - start);
        }

        [Test]
        public void CopyPassAlwaysWritesTheAmbientLightingTarget()
        {
            string pass = PassSource(File.ReadAllText(Path.Combine(ShaderDirectory(), "ScreenSpaceGlobalIllumination.shader")), "Copy Direct Lighting");
            // The feature binds both targets unconditionally, so the fragment signature must not depend on a keyword.
            Assert.AreEqual(1, Regex.Matches(pass, @"void\s+frag\s*\(").Count);
            Assert.IsTrue(Regex.IsMatch(pass, @"void\s+frag\s*\([^)]*SV_Target0[^)]*SV_Target1[^)]*\)"), "the copy pass writes the camera colour and the ambient lighting");
        }

        [Test]
        public void SurfacesWithoutAGBufferGetTheirAlbedoFromTheirColourAndAmbient()
        {
            string utilities = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGIUtilities.hlsl"));
            string shader = File.ReadAllText(Path.Combine(ShaderDirectory(), "ScreenSpaceGlobalIllumination.shader"));
            StringAssert.Contains("SSGIImpliedAlbedo(color, ambientLighting)", utilities);
            Assert.IsFalse(Regex.IsMatch(utilities, @"albedo\s*=\s*half3\(_SSGIFallbackAlbedo"), "the constant albedo is only the cap of the implied one");
            foreach (string passName in new[] { "Combine GI", "Combine GI Add" })
            {
                string pass = PassSource(shader, passName);
                StringAssert.Contains("_SSGIAmbientLightingTexture", pass);
                Assert.IsTrue(Regex.IsMatch(pass, @"SSGISampleAlbedoMetallic\(screenUV, (?:SSGIHasGBuffer\(screenUV\)|hasGBuffer), cameraColor, ambientLighting, albedo, metallic\)"), passName);
            }
        }

        [Test]
        public void ImpliedAlbedoIsCappedByTheAssumedAlbedoRatherThanByOne()
        {
            string utilities = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGIUtilities.hlsl"));
            string implied = utilities.Substring(utilities.IndexOf("half3 SSGIImpliedAlbedo", System.StringComparison.Ordinal));
            implied = implied.Substring(0, implied.IndexOf('}'));

            // saturate() would hand every directly lit surface an albedo of 1, and with it the full traced irradiance.
            Assert.IsFalse(implied.Contains("saturate("), "the implied albedo must not be capped at 1");
            StringAssert.Contains("min(implied", implied);
            StringAssert.Contains("_SSGIFallbackAlbedo", implied);
        }

        [Test]
        public void GBufferDataIsRejectedWhenItBelongsToASurfaceBehindThePixel()
        {
            string utilities = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGIUtilities.hlsl"));
            string input = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGIInput.hlsl"));

            StringAssert.Contains("_GBufferDepthTexture", input);
            StringAssert.Contains("_GBufferDepthTexture", utilities);

            // Both entry points that decide "does this pixel have GBuffer data" must apply the depth check, otherwise a surface
            // whose shader has no GBuffer pass is shaded with the data of whatever is behind it.
            Assert.AreEqual(2, Regex.Matches(utilities, @"SSGIHasGBuffer\([^)]*\)\s*&&\s*SSGIGBufferMatchesSurface\(screenUV\)").Count);
        }

        [Test]
        public void AGuessedAlbedoCannotBrightenAPixelWithoutBound()
        {
            string utilities = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGIUtilities.hlsl"));
            string add = PassSource(File.ReadAllText(Path.Combine(ShaderDirectory(), "ScreenSpaceGlobalIllumination.shader")), "Combine GI Add");

            StringAssert.Contains("SSGI_FALLBACK_MAX_GAIN", utilities);
            StringAssert.Contains("SSGIClampFallbackContribution(giContribution, cameraColor, hasGBuffer)", add);
        }

        [Test]
        public void ProbeAtlasFallbackUsesUrpsClusterCode()
        {
            string fallback = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGIFallback.hlsl"));
            string shader = File.ReadAllText(Path.Combine(ShaderDirectory(), "ScreenSpaceGlobalIllumination.shader"));
            Assert.IsFalse(fallback.Contains("ClusterIterator ClusterInit("), "the copied cluster iteration must be gone");
            StringAssert.Contains("urp_ReflProbes_Rotation", fallback);
            StringAssert.Contains("#define _CLUSTER_LIGHT_LOOP 1", shader);
            StringAssert.Contains("REFLECTION_PROBE_ROTATION", shader);
        }

        [Test]
        public void PreviousFrameReprojectionUsesThePerEyeMatrix()
        {
            string input = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGIInput.hlsl"));
            string shader = File.ReadAllText(Path.Combine(ShaderDirectory(), "ScreenSpaceGlobalIllumination.shader"));

            StringAssert.Contains("_PrevInvViewProjMatrixStereo[2]", input);
            StringAssert.Contains("SSGI_PREV_INV_VIEW_PROJ_MATRIX _PrevInvViewProjMatrixStereo[unity_StereoEyeIndex]", input);
            StringAssert.Contains("SSGI_PREV_INV_VIEW_PROJ_MATRIX", shader);
            Assert.IsFalse(Regex.IsMatch(shader, @"ComputeWorldSpacePosition\([^;]*\b_PrevInvViewProjMatrix\b"), "the shader must not reproject with the mono matrix");
        }

        [Test]
        public void CreateWithTheRightShaderBuildsTheMaterial()
        {
            ScreenSpaceGlobalIlluminationURP feature = CreateFeature();
            try
            {
                feature.SSGIShader = FindShader();
                feature.Create();
                Assert.IsNotNull(feature.SSGIMaterial);
                Assert.AreEqual(ShaderName, feature.SSGIMaterial.shader.name);
            }
            finally
            {
                feature.Dispose();
                Object.DestroyImmediate(feature);
            }
        }

        [Test]
        public void FreshFeatureGetsTheShaderFromItsDefaultReference()
        {
            ScreenSpaceGlobalIlluminationURP feature = CreateFeature();
            try
            {
                Assert.IsNotNull(feature.SSGIShader);
                Assert.AreEqual(ShaderName, feature.SSGIShader.name);
                Assert.IsTrue(feature.GBufferFallback);
                Assert.AreEqual(0.5f, feature.FallbackAlbedo);
                Assert.IsTrue(feature.OverrideAmbientLighting);
            }
            finally
            {
                feature.Dispose();
                Object.DestroyImmediate(feature);
            }
        }

        [Test]
        public void CreateWithoutTheShaderLogsAnError()
        {
            ScreenSpaceGlobalIlluminationURP feature = CreateFeature();
            try
            {
                // The public setter refuses anything but the real shader, so clear the serialized field directly.
                SerializedObject serialized = new SerializedObject(feature);
                serialized.FindProperty("m_Shader").objectReferenceValue = null;
                LogAssert.ignoreFailingMessages = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                LogAssert.ignoreFailingMessages = false;
                Assert.IsNull(feature.SSGIShader);

                LogAssert.Expect(LogType.Error, new Regex("Screen Space Global Illumination URP: Material is not using"));
                feature.Create();
            }
            finally
            {
                feature.Dispose();
                Object.DestroyImmediate(feature);
            }
        }

        [Test]
        public void ShaderSetterRejectsOtherShaders()
        {
            ScreenSpaceGlobalIlluminationURP feature = CreateFeature();
            try
            {
                feature.SSGIShader = FindShader();
                feature.SSGIShader = Shader.Find("Hidden/InternalErrorShader");
                Assert.AreEqual(ShaderName, feature.SSGIShader.name);
            }
            finally
            {
                feature.Dispose();
                Object.DestroyImmediate(feature);
            }
        }

        [TestCase(16, true, 0, 4)]
        [TestCase(24, false, 4, 10)]
        [TestCase(32, false, 4, 12)]
        [TestCase(64, false, 8, 24)]
        public void RayMarchingStepBudget(int maxRaySteps, bool expectLow, int expectSmall, int expectMedium)
        {
            ScreenSpaceGlobalIlluminationURP.ComputeRayMarchingSteps(maxRaySteps, out bool lowStepCount, out int smallSteps, out int mediumSteps);
            Assert.AreEqual(expectLow, lowStepCount);
            Assert.AreEqual(expectSmall, smallSteps);
            Assert.AreEqual(expectMedium, mediumSteps);
            Assert.LessOrEqual(mediumSteps, maxRaySteps);
        }

        [Test]
        public void TemporalIntensityAccumulatesMoreAtLowResolution()
        {
            float half = ScreenSpaceGlobalIlluminationURP.ComputeTemporalIntensity(0.95f, 0.5f);
            float full = ScreenSpaceGlobalIlluminationURP.ComputeTemporalIntensity(0.95f, 1.0f);
            Assert.AreEqual(0.91f, full, 1e-5f);
            Assert.AreEqual(0.94f, half, 1e-5f);
            Assert.Greater(half, full);
        }

        [Test]
        public void RotatorIsARotationMatrix()
        {
            Vector4 rotator = ScreenSpaceGlobalIlluminationURP.ScreenSpaceGlobalIlluminationPass.EvaluateRotator(0.3f);
            Assert.AreEqual(Mathf.Cos(0.3f), rotator.x, 1e-6f);
            Assert.AreEqual(Mathf.Sin(0.3f), rotator.y, 1e-6f);
            Assert.AreEqual(-Mathf.Sin(0.3f), rotator.z, 1e-6f);
            Assert.AreEqual(Mathf.Cos(0.3f), rotator.w, 1e-6f);
        }

        [Test]
        public void PixelSpreadAngleTangentFollowsTheTracedResolution()
        {
            float full = ScreenSpaceGlobalIlluminationURP.ScreenSpaceGlobalIlluminationPass.ComputePixelSpreadAngleTangent(60f, 1000, 500, 1f);
            float half = ScreenSpaceGlobalIlluminationURP.ScreenSpaceGlobalIlluminationPass.ComputePixelSpreadAngleTangent(60f, 1000, 500, 0.5f);
            Assert.AreEqual(Mathf.Tan(30f * Mathf.Deg2Rad) * 2f / 500f, full, 1e-7f);
            Assert.AreEqual(full * 2f, half, 1e-7f);
            Assert.IsFalse(float.IsInfinity(ScreenSpaceGlobalIlluminationURP.ScreenSpaceGlobalIlluminationPass.ComputePixelSpreadAngleTangent(60f, 0, 0, 0.5f)));
        }

        [Test]
        public void VerticalFieldOfViewIsRecoveredFromAProjection()
        {
            Matrix4x4 projection = Matrix4x4.Perspective(72f, 1.2f, 0.1f, 100f);
            Assert.AreEqual(72f, ScreenSpaceGlobalIlluminationURP.ScreenSpaceGlobalIlluminationPass.GetVerticalFieldOfView(projection), 1e-3f);
        }

        [Test]
        public void InverseViewProjectionRoundTripsAPoint()
        {
            Matrix4x4 view = Matrix4x4.TRS(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 20f, 0f), Vector3.one).inverse;
            Matrix4x4 projection = Matrix4x4.Perspective(60f, 1.5f, 0.1f, 100f);
            Matrix4x4 inverse = ScreenSpaceGlobalIlluminationURP.ScreenSpaceGlobalIlluminationPass.ComputeInverseViewProjection(view, projection);

            Vector4 point = new Vector4(0.5f, -0.25f, -4f, 1f);
            Vector4 clip = GL.GetGPUProjectionMatrix(projection, true) * view * point;
            Vector4 back = inverse * clip;
            back /= back.w;

            Assert.AreEqual(point.x, back.x, 1e-4f);
            Assert.AreEqual(point.y, back.y, 1e-4f);
            Assert.AreEqual(point.z, back.z, 1e-4f);
        }

        [Test]
        public void GBufferFormatsMatchUrpsDeferredLayout()
        {
            ScreenSpaceGlobalIlluminationURP.ForwardGBufferPass pass = new ScreenSpaceGlobalIlluminationURP.ForwardGBufferPass(new[] { "UniversalGBuffer" });
            GraphicsFormat albedo = pass.GetGBufferFormat(0);
            Assert.IsTrue(albedo == GraphicsFormat.R8G8B8A8_SRGB || albedo == GraphicsFormat.R8G8B8A8_UNorm);
            Assert.AreEqual(GraphicsFormat.R8G8B8A8_UNorm, pass.GetGBufferFormat(1));
            GraphicsFormat normals = pass.GetGBufferFormat(2);
            Assert.IsTrue(normals == GraphicsFormat.R8G8B8A8_SNorm || normals == GraphicsFormat.R16G16B16A16_SFloat);
            Assert.AreEqual(GraphicsFormat.None, pass.GetGBufferFormat(3));
        }
        [Test]
        public void BlueNoiseIsAUniform64x64Texture()
        {
            Texture2D noise = ScreenSpaceGlobalIlluminationBlueNoise.Texture;
            Assert.AreEqual(ScreenSpaceGlobalIlluminationBlueNoise.Size, noise.width);
            Assert.AreEqual(ScreenSpaceGlobalIlluminationBlueNoise.Size, noise.height);
            Assert.AreEqual(TextureFormat.R8, noise.format);
            Assert.AreEqual(FilterMode.Point, noise.filterMode);
            Assert.AreEqual(TextureWrapMode.Repeat, noise.wrapMode);

            int[] histogram = new int[256];
            foreach (byte value in noise.GetRawTextureData<byte>())
                histogram[value]++;
            foreach (int count in histogram)
                Assert.AreEqual(16, count, "a void-and-cluster texture uses every value equally often");
        }

        [Test]
        public void MaterialBindsTheBlueNoise()
        {
            ScreenSpaceGlobalIlluminationURP feature = CreateFeature();
            try
            {
                feature.SSGIShader = FindShader();
                feature.Create();
                Assert.AreEqual(ScreenSpaceGlobalIlluminationBlueNoise.Texture, feature.SSGIMaterial.GetTexture("_SSGIBlueNoise"));
            }
            finally
            {
                feature.Dispose();
                Object.DestroyImmediate(feature);
            }
        }

        [Test]
        public void RayMarchProjectsOncePerRay()
        {
            string source = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGI.hlsl"));
            StringAssert.Contains("float4 rayPositionCS = rayOriginCS + rayDirectionCS * rayDistance;", source);
            Assert.IsFalse(source.Contains("ComputeNormalizedDeviceCoordinatesWithZ(rayPositionWS"), "the loop must not project a world position every step");
            Assert.IsFalse(source.Contains("_BackDepthEnabled = 2.0"), "assignment where a comparison is meant");
        }

        [Test]
        public void DenoisersReadTheTracedResolutionDepthAndNormals()
        {
            string shader = File.ReadAllText(Path.Combine(ShaderDirectory(), "ScreenSpaceGlobalIllumination.shader"));
            foreach (string pass in new[] { "Edge-Avoiding Spatial Denoise", "Poisson Disk Recurrent Denoise" })
            {
                int start = shader.IndexOf("Name \"" + pass + "\"", System.StringComparison.Ordinal);
                Assert.Greater(start, 0, pass);
                int end = shader.IndexOf("ENDHLSL", start, System.StringComparison.Ordinal);
                string body = shader.Substring(start, end - start);
                Assert.IsFalse(body.Contains("_CameraDepthTexture"), pass + " reads the full resolution depth");
                Assert.IsFalse(body.Contains("SSGISampleNormalWS"), pass + " decodes GBuffer normals per tap");
                StringAssert.Contains("_SSGIDepthTexture", body);
                StringAssert.Contains("_SSGINormalTexture", body);
            }
        }
    }
}
