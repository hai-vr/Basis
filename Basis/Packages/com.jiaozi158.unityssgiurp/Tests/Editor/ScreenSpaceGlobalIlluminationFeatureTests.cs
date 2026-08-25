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
            "Prepare",
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
            "Prime Depth",
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

        private static string RuntimeDirectory()
        {
            return Path.Combine(Path.GetDirectoryName(ShaderDirectory()), "Runtime");
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

        [Test]
        public void RenderingLayerReadsCallAFunctionThisUrpDeclares()
        {
            // _USE_RENDERING_LAYERS is a multi_compile, so a player build compiles it even though Basis never
            // turns _WRITE_RENDERING_LAYERS on. Upstream called SampleSceneRenderingLayer, which URP 17 dropped,
            // and nothing caught it until a build: the editor only ever compiles the variants it renders with.
            string headerPath = Path.GetFullPath("Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareRenderingLayerTexture.hlsl");
            if (!File.Exists(headerPath))
            {
                Assert.Ignore("URP's DeclareRenderingLayerTexture.hlsl was not found at " + headerPath);
            }
            string header = File.ReadAllText(headerPath);
            string shader = File.ReadAllText(Path.Combine(ShaderDirectory(), "ScreenSpaceGlobalIllumination.shader"));
            MatchCollection calls = Regex.Matches(shader, @"\b(\w+SceneRenderingLayer)\s*\(");
            Assert.Greater(calls.Count, 0, "the combine passes no longer read the rendering layers texture");
            foreach (Match call in calls)
            {
                StringAssert.Contains(call.Groups[1].Value + "(", header, call.Groups[1].Value + " is not declared by this URP");
            }
        }

        private static string PassSource(string shader, string passName)
        {
            int start = shader.IndexOf("Name \"" + passName + "\"", System.StringComparison.Ordinal);
            Assert.Greater(start, 0, passName);
            int next = shader.IndexOf("Name \"", start + 1, System.StringComparison.Ordinal);
            return shader.Substring(start, (next < 0 ? shader.Length : next) - start);
        }

        [Test]
        public void PreparePassAlwaysWritesEverySurfaceTarget()
        {
            string pass = PassSource(File.ReadAllText(Path.Combine(ShaderDirectory(), "ScreenSpaceGlobalIllumination.shader")), "Prepare");
            // The feature binds all four targets unconditionally, so the fragment signature must not depend on a keyword.
            Assert.AreEqual(1, Regex.Matches(pass, @"void\s+frag\s*\(").Count);
            Assert.IsTrue(Regex.IsMatch(pass, @"void\s+frag\s*\([^)]*SV_Target0[^)]*SV_Target1[^)]*SV_Target2[^)]*SV_Target3[^)]*\)"),
                "the prepare pass writes the camera colour, the ambient lighting, the normal and the albedo");
        }

        [Test]
        public void TheCombinePassesReadThePreparedSurfaceInsteadOfRederivingIt()
        {
            // Re-deriving the normal in the upscale, five times per pixel, cost more than the trace it was upscaling.
            string shader = File.ReadAllText(Path.Combine(ShaderDirectory(), "ScreenSpaceGlobalIllumination.shader"));
            string combine = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGICombine.hlsl"));
            Assert.IsFalse(combine.Contains("SSGISampleNormalWS"), "the upscale must read the prepared normal");
            foreach (string passName in new[] { "Combine GI", "Combine GI Add" })
            {
                string pass = PassSource(shader, passName);
                Assert.IsFalse(Regex.IsMatch(pass, @"SSGISampleAlbedoMetallic\("), passName + " must read the prepared albedo");
                StringAssert.Contains("SSGIReadSurfaceAlbedoMetallic(screenUV, albedo, metallic)", pass);
            }
        }

        [Test]
        public void OnlyThePreparePassResolvesTheSurfaceFromScratch()
        {
            string shader = File.ReadAllText(Path.Combine(ShaderDirectory(), "ScreenSpaceGlobalIllumination.shader"));
            // SSGIGBufferMatchesSurface costs two fetches and two depth linearisations every time it is asked.
            Assert.AreEqual(1, Regex.Matches(shader, @"SSGISampleNormalWS\(screenUV, hasGBuffer\)").Count);
            Assert.IsFalse(PassSource(shader, "Screen Space Global Illumination").Contains("SSGISampleNormalWS"));
            Assert.IsFalse(PassSource(shader, "Temporal Reprojection").Contains("SSGISampleNormalWS"));
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
                Assert.IsTrue(Regex.IsMatch(pass, @"SSGIReadSurfaceAlbedoMetallic\(screenUV, albedo, metallic\)"), passName);
            }
        }

        [Test]
        public void EmissiveLightIsAddedOnTopOfTheColourHistoryRatherThanInsteadOfIt()
        {
            // The colour history already carries emission, so adding the emission buffer on top would double count it.
            // Scaling by (multiplier - 1) makes a multiplier of 1 byte for byte what the effect did without one.
            string utilities = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGIUtilities.hlsl"));
            string march = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGI.hlsl"));

            StringAssert.Contains("(_SSGIEmissiveMultiplier - 1.0)", utilities);
            StringAssert.Contains("historyColor + boost", utilities);
            StringAssert.Contains("SSGIHitRadiance(", march);
        }

        [Test]
        public void EmissionIsTakenNetOfTheAmbientTheGBufferTargetAlsoCarries()
        {
            // URP's Lit GBuffer pass writes "surfaceData.emission + bakedGI" into this target, so the ambient the
            // prepare pass already resolved has to come back out or ambient light would be counted as emission.
            string utilities = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGIUtilities.hlsl"));
            int start = utilities.IndexOf("half3 SSGISampleEmission", System.StringComparison.Ordinal);
            Assert.Greater(start, 0);
            string emission = utilities.Substring(start, utilities.IndexOf("half3 SSGIHitRadiance", System.StringComparison.Ordinal) - start);
            StringAssert.Contains("_SSGIEmissionTexture", emission);
            StringAssert.Contains("ambient * albedo", emission);
            StringAssert.Contains("max(emission -", emission);
        }

        [Test]
        public void TheFireflyClampIsAppliedPerRayNotToTheAverage()
        {
            // Clamping the mean lets one outlier lift the average and then scales every correct ray down with it.
            string pass = PassSource(File.ReadAllText(Path.Combine(ShaderDirectory(), "ScreenSpaceGlobalIllumination.shader")), "Screen Space Global Illumination");
            int clamp = pass.IndexOf("_MaxBrightness", System.StringComparison.Ordinal);
            int loopEnd = pass.IndexOf("lightingDistance.rgb += rayRadiance * sampleWeight;", System.StringComparison.Ordinal);
            Assert.Greater(clamp, 0);
            Assert.Greater(loopEnd, clamp, "the clamp must run before the ray is accumulated");
            Assert.IsFalse(Regex.IsMatch(pass, @"maxChannel\s*=\s*Max3\(lightingDistance"), "the clamp must not read the accumulated sum");
        }

        [Test]
        public void TheSpatialDenoisersWidenWhereTheEstimateHasLittleHistory()
        {
            string denoise = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGIDenoise.hlsl"));
            string shader = File.ReadAllText(Path.Combine(ShaderDirectory(), "ScreenSpaceGlobalIllumination.shader"));
            StringAssert.Contains("SSGIHistoryNoisiness", denoise);
            StringAssert.Contains("_SSGISampleTexture", denoise);
            foreach (string passName in new[] { "Edge-Avoiding Spatial Denoise", "Poisson Disk Recurrent Denoise" })
                StringAssert.Contains("SSGIHistoryNoisiness", PassSource(shader, passName));
        }

        [Test]
        public void TheTemporalPassesClipHistoryToTheVarianceTheyAlreadyMeasure()
        {
            // Both passes built first and second moments and then threw them away, clamping to a raw min and max box.
            string shader = File.ReadAllText(Path.Combine(ShaderDirectory(), "ScreenSpaceGlobalIllumination.shader"));
            string denoise = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGIDenoise.hlsl"));
            StringAssert.Contains("half3 ClipToVarianceBox(", denoise);
            foreach (string passName in new[] { "Temporal Reprojection", "Temporal Stabilization" })
            {
                string pass = PassSource(shader, passName);
                StringAssert.Contains("ClipToVarianceBox(prevColor, boxMin, boxMax, moment1, moment2", pass);
                Assert.IsFalse(Regex.IsMatch(pass, @"prevColor\s*=\s*clamp\(prevColor"), passName + " must use the moments it computed");
            }
        }

        [Test]
        public void TheColourHistoryIsFilteredWhenItIsDownsampled()
        {
            // Every ray hit reads this. Point sampling threw away three pixels in four and the aliasing that left
            // popped in and out of the bounce as the camera moved.
            string pass = PassSource(File.ReadAllText(Path.Combine(ShaderDirectory(), "ScreenSpaceGlobalIllumination.shader")), "Blit Color Texture");
            StringAssert.Contains("my_linear_clamp_sampler", pass);
            Assert.IsFalse(pass.Contains("my_point_clamp_sampler"));
        }

        [Test]
        public void TheRayMarchResolvesTheProjectionBranchOnceInsteadOfPerStep()
        {
            // ConvertLinearEyeDepth branches on the projection type, and the march called it up to 128 times a pixel.
            string march = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGI.hlsl"));
            StringAssert.Contains("SSGIGetDepthLinearizer(isPerspective)", march);
            int loop = march.IndexOf("for (int i = 1; i <= MAX_STEP", System.StringComparison.Ordinal);
            Assert.Greater(loop, 0);
            Assert.IsFalse(march.Substring(loop).Contains("ConvertLinearEyeDepth("), "the loop must use the resolved linearizer");
        }

        [Test]
        public void ImpliedAlbedoIsCappedByTheAssumedAlbedoRatherThanByOne()
        {
            string utilities = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGIUtilities.hlsl"));
            string implied = utilities.Substring(utilities.IndexOf("half3 SSGIImpliedAlbedo", System.StringComparison.Ordinal));
            implied = implied.Substring(0, implied.IndexOf('}'));

            // saturate() would hand every directly lit surface an albedo of 1, and with it the full traced irradiance.
            Assert.IsFalse(implied.Contains("saturate("), "the implied albedo must not be capped at 1");
            StringAssert.Contains("_SSGIFallbackAlbedo", implied);

            // Capping each channel on its own is what made every directly lit fallback surface come back grey: the
            // ratio is above the cap in all three channels, so all three land on the same value. The cap has to act
            // on the luminance and scale the estimate, so hue and saturation survive.
            Assert.IsFalse(Regex.IsMatch(implied, @"min\(implied,"), "the cap must not clamp the channels independently");
            StringAssert.Contains("Luminance(implied)", implied);
            StringAssert.Contains("implied * scale", implied);
        }

        [Test]
        public void AGuessedAlbedoKeepsTheSurfacesColour()
        {
            // Reproduces the shader's arithmetic: a red surface under white ambient must come back red, not grey,
            // whether it is lit only by that ambient or by direct light far brighter than it.
            Vector3 ambient = new Vector3(0.2f, 0.2f, 0.2f);
            const float cap = 0.5f;

            foreach (float directLight in new[] { 1f, 4f, 40f })
            {
                Vector3 color = new Vector3(0.8f * directLight * ambient.x, 0.1f * directLight * ambient.y, 0.1f * directLight * ambient.z);
                Vector3 implied = new Vector3(color.x / ambient.x, color.y / ambient.y, color.z / ambient.z);
                float luminance = 0.2126f * implied.x + 0.7152f * implied.y + 0.0722f * implied.z;
                float scale = Mathf.Min(1f, cap / Mathf.Max(luminance, 1e-4f));
                Vector3 albedo = implied * scale;

                Assert.Greater(albedo.x, albedo.y * 2f, "the red channel must stay dominant at direct light " + directLight);
                Assert.LessOrEqual(0.2126f * albedo.x + 0.7152f * albedo.y + 0.0722f * albedo.z, cap + 1e-4f);
                // The bound the ambient removal relies on: albedo * ambient never exceeds the pixel's own colour.
                Assert.LessOrEqual(albedo.x * ambient.x, color.x + 1e-4f);
            }
        }

        [Test]
        public void TheOverrideListMasksUnlitAlbedoTheSameWayTheRealListDoes()
        {
            // Drawing every opaque with the override shader covers unlit surfaces too, and URP's own unlit GBuffer
            // pass masks its albedo write, so it would never overwrite what the override put there: screens and
            // emissive panels would start receiving GI. The override list needs the same per material type states.
            string runtime = File.ReadAllText(Path.Combine(RuntimeDirectory(), "ScreenSpaceGlobalIlluminationURP.cs"));
            int start = runtime.IndexOf("passData.hasOverrideRendererList", System.StringComparison.Ordinal);
            Assert.Greater(start, 0);
            string overrideList = runtime.Substring(start, runtime.IndexOf("// Set render targets", start, System.StringComparison.Ordinal) - start);

            StringAssert.Contains("CreateUnlitStateBlock", overrideList);
            StringAssert.Contains("k_UnlitMaterialTypeIndex", overrideList);
            StringAssert.Contains("tagName = m_MaterialTypeTag", overrideList);
            // A single stateBlock cannot vary per material type, which is what RendererListDesc is limited to.
            Assert.IsFalse(overrideList.Contains("RendererListDesc"), "the override list must carry per material type states");
        }

        [Test]
        public void DrawingEveryOpaqueDoesNotForceTwoSidedRasterisation()
        {
            // The override pass draws the whole scene a second time; rasterising every closed mesh two sided would
            // double its triangles for nothing.
            string overrideShader = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGIGBufferOverride.shader"));
            StringAssert.Contains("Cull [_Cull]", overrideShader);
            Assert.IsFalse(Regex.IsMatch(overrideShader, @"^\s*Cull Off\s*$", RegexOptions.Multiline), "the material's own cull mode must be followed");
            // Materials that do not declare _Cull keep the two sided default cards and foliage need.
            Assert.IsTrue(Regex.IsMatch(overrideShader, @"_Cull \(""Cull"", Float\) = 0"));
        }

        [Test]
        public void AReconstructedNormalGivesWayToTheViewDirectionWhereTheDepthIsNotASurface()
        {
            // A leaf or hair card a couple of pixels wide has no neighbour on its own surface in either direction, so
            // the cross product of two unrelated depths is noise -- and the trace aims a whole hemisphere with it.
            string utilities = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGIUtilities.hlsl"));
            string config = File.ReadAllText(Path.Combine(ShaderDirectory(), "SSGIConfig.hlsl"));
            int start = utilities.IndexOf("half3 SSGIReconstructNormalWS", System.StringComparison.Ordinal);
            Assert.Greater(start, 0);
            string reconstruct = utilities.Substring(start, utilities.IndexOf("half3 SSGISampleNormalWS", System.StringComparison.Ordinal) - start);

            StringAssert.Contains("SSGI_NORMAL_MAX_CURVATURE", config);
            StringAssert.Contains("curvature", reconstruct);
            StringAssert.Contains("lerp(viewDirectionWS, normalWS, planarity)", reconstruct);
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
            StringAssert.Contains("SSGIClampFallbackContribution(giContribution, cameraColor, ambientLighting, hasGBuffer)", add);
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
