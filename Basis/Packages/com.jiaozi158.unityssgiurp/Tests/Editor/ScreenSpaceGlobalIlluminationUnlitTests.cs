using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace SSGIURP.Tests
{
    public class ScreenSpaceGlobalIlluminationUnlitTests
    {
        private const string ShaderPath = "Packages/com.jiaozi158.unityssgiurp/Shaders/ScreenSpaceGlobalIllumination.shader";

        [Test]
        public void UrpUnlitCarriesTheMaterialTypeTagTheGBufferPassMasks()
        {
            ShaderTagId tag = new ShaderTagId("UniversalMaterialType");
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.IsNotNull(unlit);
            Assert.IsTrue(ScreenSpaceGlobalIlluminationURP.HasGBufferPass(unlit), "URP Unlit is expected to reach the GBuffer pass, which is why it must be masked there");
            Assert.AreEqual(ScreenSpaceGlobalIlluminationURP.ForwardGBufferPass.UnlitMaterialType, unlit.FindSubshaderTagValue(0, tag).name);

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Assert.AreEqual("Lit", lit.FindSubshaderTagValue(0, tag).name);
        }

        [Test]
        public void UnlitStateBlockMasksOnlyTheAlbedoTargetAndKeepsTheDepthState()
        {
            RenderStateBlock baseBlock = new RenderStateBlock(RenderStateMask.Depth) { depthState = new DepthState(false, CompareFunction.Equal) };

            RenderStateBlock block = ScreenSpaceGlobalIlluminationURP.ForwardGBufferPass.CreateUnlitStateBlock(baseBlock);

            Assert.AreEqual(RenderStateMask.Depth | RenderStateMask.Blend, block.mask);
            Assert.AreEqual(CompareFunction.Equal, block.depthState.compareFunction);
            Assert.IsFalse(block.depthState.writeEnabled);
            Assert.IsTrue(block.blendState.separateMRTBlendStates);
            Assert.IsFalse(block.blendState.alphaToMask);
            Assert.AreEqual((ColorWriteMask)0, block.blendState.blendState0.writeMask);
            Assert.AreEqual(ColorWriteMask.All, block.blendState.blendState1.writeMask);
            Assert.AreEqual(ColorWriteMask.All, block.blendState.blendState2.writeMask);
            Assert.AreEqual(BlendMode.One, block.blendState.blendState1.sourceColorBlendMode);
            Assert.AreEqual(BlendMode.Zero, block.blendState.blendState1.destinationColorBlendMode);
            Assert.AreEqual(BlendMode.One, block.blendState.blendState2.sourceColorBlendMode);
            Assert.AreEqual(BlendMode.Zero, block.blendState.blendState2.destinationColorBlendMode);
        }

        [Test]
        public void AmbientRemovalLeavesGBufferPixelsWithoutAlbedoUntouched()
        {
            string source = File.ReadAllText(ShaderPath);
            int combine = source.IndexOf("Name \"Combine GI\"", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(combine, 0);
            Match skip = Regex.Match(source.Substring(combine), @"if \(!any\(albedo\)\)\s*return half4\(1\.0, 1\.0, 1\.0, 1\.0\);");
            Assert.IsTrue(skip.Success, "the combine pass must keep an ambient removal factor of 1 for unlit pixels");
            int removal = source.IndexOf("SSGIAmbientRemovalFactor(", combine, System.StringComparison.Ordinal);
            Assert.Less(combine + skip.Index, removal, "the unlit early-out must come before the ambient removal");
        }
    }
}
