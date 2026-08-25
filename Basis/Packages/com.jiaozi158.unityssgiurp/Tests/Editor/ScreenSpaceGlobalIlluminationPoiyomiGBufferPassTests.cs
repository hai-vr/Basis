using NUnit.Framework;
using UnityEngine;

namespace SSGIURP.Tests
{
    public class ScreenSpaceGlobalIlluminationPoiyomiGBufferPassTests
    {
        // The shape of a Poiyomi 10 URP DepthNormals pass, reduced to the lines the injector keys on.
        private static string PoiyomiLikeShader(string newline)
        {
            string[] lines =
            {
                "Shader \".poiyomi/Poiyomi Toon URP\"",
                "{",
                "\tSubShader",
                "\t{",
                "\t\tPass",
                "\t\t{",
                "\t\t\tName \"DepthOnly\"",
                "\t\t\tTags { \"LightMode\" = \"DepthOnly\" }",
                "\t\t\tHLSLPROGRAM",
                "\t\t\t#define POI_PASS_DEPTH_ONLY",
                "\t\t\tENDHLSL",
                "\t\t}",
                "\t\t",
                "\t\tPass",
                "\t\t{",
                "\t\t\tName \"DepthNormals\"",
                "\t\t\tTags { \"LightMode\" = \"DepthNormals\" }",
                "\t\t\t",
                "\t\t\tStencil",
                "\t\t\t{",
                "\t\t\t\tRef [_StencilRef]",
                "\t\t\t\t//ifex _StencilType==1",
                "\t\t\t\tComp [_StencilCompareFunction]",
                "\t\t\t\t//endex",
                "\t\t\t}",
                "\t\t\t",
                "\t\t\tZWrite [_ZWrite]",
                "\t\t\tCull [_Cull]",
                "\t\t\tAlphaToMask Off",
                "\t\t\tBlendOp [_BlendOp], [_BlendOpAlpha]",
                "\t\t\tBlend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]",
                "\t\t\t",
                "\t\t\tHLSLPROGRAM",
                "\t\t\t#pragma target 5.0",
                "\t\t\t#define POI_PASS_DEPTH_NORMALS",
                "\t\t\t#if POI_PIPE == POI_URP",
                "\t\t\t#include_with_pragmas \"Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl\"",
                "\t\t\t#endif",
                "\t\t\t#if POI_PIPE == POI_BIRP",
                "\t\t\tfloat4",
                "\t\t\t#else",
                "\t\t\tvoid",
                "\t\t\t#endif",
                "\t\t\tfrag( VertexOut i, bool facing : SV_IsFrontFace",
                "\t\t\t#if POI_PIPE == POI_URP",
                "\t\t\t,out half4 outNormalWS : SV_Target0",
                "\t\t\t#ifdef _WRITE_RENDERING_LAYERS",
                "\t\t\t,out uint outRenderingLayers : SV_Target1",
                "\t\t\t#endif",
                "\t\t\t#endif",
                "\t\t\t)",
                "\t\t\t{",
                "\t\t\t\tUNITY_SETUP_INSTANCE_ID(i);",
                "\t\t\t\tclip(poiFragData.alpha - _Cutoff);",
                "\t\t\t\t",
                "\t\t\t\t#if POI_PIPE == POI_URP",
                "\t\t\t\tfloat3 normalWS = NormalizeNormalPerPixel(poiMesh.normals[0]);",
                "\t\t\t\toutNormalWS = half4(normalWS, 0.0) + POI_SAFE_RGB0;",
                "\t\t\t\t#ifdef _WRITE_RENDERING_LAYERS",
                "\t\t\t\toutRenderingLayers = EncodeMeshRenderingLayer();",
                "\t\t\t\t#endif",
                "\t\t\t\t#else",
                "\t\t\t\treturn float4(0, 1, 0, 1);",
                "\t\t\t\t#endif",
                "\t\t\t}",
                "\t\t\tENDHLSL",
                "\t\t}",
                "\t\t",
                "\t\tPass",
                "\t\t{",
                "\t\t\tName \"MotionVectors\"",
                "\t\t\tTags { \"LightMode\" = \"MotionVectors\" }",
                "\t\t\tHLSLPROGRAM",
                "\t\t\tENDHLSL",
                "\t\t}",
                "\t}",
                "}",
            };
            return string.Join(newline, lines) + newline;
        }

        private static string PassBlock(string source, string tag)
        {
            int tagIndex = source.IndexOf(tag, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(tagIndex, 0, "pass tag not found: " + tag);
            int start = source.LastIndexOf("Pass", tagIndex, System.StringComparison.Ordinal);
            int end = source.IndexOf("ENDHLSL", tagIndex, System.StringComparison.Ordinal);
            return source.Substring(start, end - start);
        }

        [TestCase("\r\n")]
        [TestCase("\n")]
        public void InjectsAGBufferPassAfterTheDepthNormalsPass(string newline)
        {
            string source = PoiyomiLikeShader(newline);
            Assert.IsTrue(ScreenSpaceGlobalIlluminationPoiyomiGBufferPass.IsCandidate(source));

            Assert.IsTrue(ScreenSpaceGlobalIlluminationPoiyomiGBufferPass.TryInject(source, out string result, out string error), error);

            string gbufferTag = "\"LightMode\" = \"" + ScreenSpaceGlobalIlluminationPoiyomiGBufferPass.LightMode + "\"";
            int depthNormals = result.IndexOf("\"LightMode\" = \"DepthNormals\"", System.StringComparison.Ordinal);
            int gbuffer = result.IndexOf(gbufferTag, System.StringComparison.Ordinal);
            int motionVectors = result.IndexOf("\"LightMode\" = \"MotionVectors\"", System.StringComparison.Ordinal);
            Assert.Greater(gbuffer, depthNormals);
            Assert.Greater(motionVectors, gbuffer);
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(result, System.Text.RegularExpressions.Regex.Escape(gbufferTag)).Count);

            string pass = PassBlock(result, gbufferTag);
            StringAssert.Contains("Name \"SSGIGBuffer\"", pass);
            StringAssert.DoesNotContain("Stencil", pass);
            StringAssert.Contains("Blend One Zero", pass);
            StringAssert.Contains("BlendOp Add", pass);
            StringAssert.DoesNotContain("[_SrcBlend]", pass);
            StringAssert.Contains("#define POI_PASS_DEPTH_NORMALS", pass);
            StringAssert.Contains("#define POI_PASS_SSGI_GBUFFER", pass);
            StringAssert.Contains("#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT", pass);
            StringAssert.DoesNotContain("RenderingLayers.hlsl", pass);
            StringAssert.Contains("out half4 outGBuffer0 : SV_Target0", pass);
            StringAssert.Contains("out half4 outGBuffer1 : SV_Target1", pass);
            StringAssert.Contains("out half4 outGBuffer2 : SV_Target2", pass);
            StringAssert.DoesNotContain("outNormalWS", pass);
            StringAssert.DoesNotContain("outRenderingLayers", pass);
            StringAssert.DoesNotContain("return float4(0, 1, 0, 1);", pass);
            StringAssert.Contains("outGBuffer0 = half4(poiFragData.baseColor, 0.0);", pass);
            StringAssert.Contains("outGBuffer2 = half4(packedNormalWS, 0.5);", pass);
            StringAssert.Contains("poiMesh.normals[1]", pass);
            StringAssert.Contains("clip(poiFragData.alpha - _Cutoff);", pass);

            // The original DepthNormals pass is untouched.
            string original = PassBlock(source, "\"LightMode\" = \"DepthNormals\"");
            Assert.AreEqual(original, PassBlock(result, "\"LightMode\" = \"DepthNormals\""));

            // Line endings are preserved.
            Assert.AreEqual(newline == "\r\n", result.Contains("\r\n"));
        }

        [Test]
        public void InjectionIsNotRepeated()
        {
            string source = PoiyomiLikeShader("\r\n");
            Assert.IsTrue(ScreenSpaceGlobalIlluminationPoiyomiGBufferPass.TryInject(source, out string once, out _));
            Assert.IsFalse(ScreenSpaceGlobalIlluminationPoiyomiGBufferPass.IsCandidate(once));
            Assert.IsFalse(ScreenSpaceGlobalIlluminationPoiyomiGBufferPass.TryInject(once, out string twice, out string error));
            StringAssert.Contains("already", error);
            Assert.AreEqual(once, twice);
        }

        [TestCase("\r\n")]
        [TestCase("\n")]
        public void RemoveRestoresTheOriginalShader(string newline)
        {
            string source = PoiyomiLikeShader(newline);
            Assert.IsTrue(ScreenSpaceGlobalIlluminationPoiyomiGBufferPass.TryInject(source, out string injected, out _));
            Assert.IsTrue(ScreenSpaceGlobalIlluminationPoiyomiGBufferPass.HasPass(injected));
            Assert.IsTrue(ScreenSpaceGlobalIlluminationPoiyomiGBufferPass.TryRemove(injected, out string restored));
            Assert.AreEqual(source, restored);
        }

        [Test]
        public void NonPoiyomiShadersAreRejected()
        {
            string lit = "Shader \"Universal Render Pipeline/Lit\" { SubShader { Pass { Tags { \"LightMode\" = \"DepthNormals\" } HLSLPROGRAM ENDHLSL } } }";
            Assert.IsFalse(ScreenSpaceGlobalIlluminationPoiyomiGBufferPass.IsCandidate(lit));
            Assert.IsFalse(ScreenSpaceGlobalIlluminationPoiyomiGBufferPass.TryInject(lit, out _, out string error));
            Assert.IsNotNull(error);
            Assert.IsFalse(ScreenSpaceGlobalIlluminationPoiyomiGBufferPass.TryRemove(lit, out _));
        }

        [Test]
        public void UnknownFragmentLayoutIsRefusedWithoutWriting()
        {
            string source = PoiyomiLikeShader("\r\n").Replace("outNormalWS = half4(normalWS, 0.0) + POI_SAFE_RGB0;", "outNormalWS = half4(normalWS, 1.0);");
            Assert.IsFalse(ScreenSpaceGlobalIlluminationPoiyomiGBufferPass.TryInject(source, out string result, out string error));
            StringAssert.Contains("fragment output", error);
            Assert.AreEqual(source, result);
        }

        [Test]
        public void TheFeatureDrawsTheInjectedPass()
        {
            Assert.AreEqual(ScreenSpaceGlobalIlluminationPoiyomiGBufferPass.LightMode, ScreenSpaceGlobalIlluminationURP.SSGIGBufferLightMode);
        }
    }
}
