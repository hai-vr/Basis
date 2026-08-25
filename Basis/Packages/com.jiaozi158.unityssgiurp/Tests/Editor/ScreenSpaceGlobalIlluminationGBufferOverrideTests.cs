using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SSGIURP.Tests
{
    public class ScreenSpaceGlobalIlluminationGBufferOverrideTests
    {
        private const string OverrideShaderName = "Hidden/Lighting/ScreenSpaceGlobalIlluminationGBufferOverride";

        private GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
                root = null;
            }
        }

        private Renderer CreateRenderer(Shader shader)
        {
            if (root == null)
                root = new GameObject("gbuffer-override-root");
            GameObject child = new GameObject("renderer");
            child.transform.SetParent(root.transform, false);
            child.AddComponent<MeshFilter>();
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return renderer;
        }

        [Test]
        public void OverrideShaderCompilesAndExposesTheSsgiGBufferPass()
        {
            Shader shader = Shader.Find(OverrideShaderName);
            Assert.IsNotNull(shader);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "override shader has compile errors");
            Assert.AreEqual(1, shader.passCount);
            Assert.AreEqual(ScreenSpaceGlobalIlluminationURP.SSGIGBufferLightMode, shader.FindPassTagValue(0, new UnityEngine.Rendering.ShaderTagId("LightMode")).name);
        }

        [Test]
        public void LitHasAGBufferPassAndAnErrorShaderDoesNot()
        {
            Assert.IsTrue(ScreenSpaceGlobalIlluminationURP.HasGBufferPass(Shader.Find("Universal Render Pipeline/Lit")));
            Assert.IsTrue(ScreenSpaceGlobalIlluminationURP.HasGBufferPass(Shader.Find(OverrideShaderName)));
            Assert.IsFalse(ScreenSpaceGlobalIlluminationURP.HasGBufferPass(Shader.Find("Hidden/InternalErrorShader")));
            Assert.IsFalse(ScreenSpaceGlobalIlluminationURP.HasGBufferPass(null));
        }

        [Test]
        public void RenderersWithoutAGBufferPassAreMarkedForTheOverrideShader()
        {
            uint mask = ScreenSpaceGlobalIlluminationURP.GBufferOverrideRenderingLayerMask;
            Assert.AreNotEqual(0u, mask);

            Renderer plain = CreateRenderer(Shader.Find("Hidden/InternalErrorShader"));
            Renderer lit = CreateRenderer(Shader.Find("Universal Render Pipeline/Lit"));
            uint plainBefore = plain.renderingLayerMask;
            uint litBefore = lit.renderingLayerMask;

            Assert.AreEqual(1, ScreenSpaceGlobalIlluminationURP.RegisterRenderers(root));

            Assert.AreEqual(plainBefore | mask, plain.renderingLayerMask);
            Assert.AreEqual(litBefore & ~mask, lit.renderingLayerMask);

            // Switching the material to one with a GBuffer pass clears the mark again.
            plain.sharedMaterial = lit.sharedMaterial;
            Assert.IsFalse(ScreenSpaceGlobalIlluminationURP.RegisterRenderer(plain));
            Assert.AreEqual(0u, plain.renderingLayerMask & mask);
        }

        [Test]
        public void RegistrationToleratesNullsAndEmptyRoots()
        {
            Assert.IsFalse(ScreenSpaceGlobalIlluminationURP.RegisterRenderer(null));
            Assert.AreEqual(0, ScreenSpaceGlobalIlluminationURP.RegisterRenderers(null));
            root = new GameObject("empty");
            Assert.AreEqual(0, ScreenSpaceGlobalIlluminationURP.RegisterRenderers(root));
        }
    }
}
