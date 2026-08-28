using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Basis.Tests.GlobalIllumination
{
    /// <summary>
    /// Every ray fired at the acceleration structure has to step out of any avatar capsule it began inside.
    ///
    /// Avatars are traced as capsules on their bones while the depth buffer - the point every ray starts
    /// from - is the avatar's real surface, so a ray leaving a visible chest starts inside its own torso
    /// capsule. <c>BasisGIRtTraceEscapingProxies</c> is the answer to that and the gather and the reflection
    /// both went through it; the light visibility ray did not, and every lit avatar wore the shape of its
    /// own proxy as a hard edged black patch.
    ///
    /// This is asserted against the kernel SOURCE rather than against rendered light, and that is a
    /// deliberate choice rather than a shortcut. The defect was structural - one call site out of four not
    /// using a wrapper - and it is structural again the next time somebody adds a ray. Reproducing it
    /// through the renderer would need a humanoid Animator, a real avatar built at runtime and a ray
    /// tracing device, and would then only cover the one trace it happened to be written around. This
    /// covers all of them, including the ones not written yet.
    ///
    /// It cannot tell you the spots are gone. It can tell you nobody has quietly re-opened the door.
    /// </summary>
    public class BasisGlobalIlluminationRayProxyEscapeTests
    {
        private const string KernelPath =
            "Packages/com.basis.globalillumination/Shaders/BasisGlobalIlluminationRTKernel.hlsl";
        private const string EscapeFunction = "BasisGIRtTraceEscapingProxies";
        private const string LightingFunction = "BasisGIRtDirectLighting";

        /// <summary>
        /// The kernel with every comment replaced by a space.
        ///
        /// The comments in this file discuss the traces at length - one of them names the very any-hit call
        /// that caused the bug, while explaining why it is gone - so counting occurrences in the raw text
        /// would find the prose rather than the code.
        /// </summary>
        private static string ReadKernelWithoutComments()
        {
            string full = Path.GetFullPath(KernelPath);
            Assert.IsTrue(File.Exists(full),
                KernelPath + " is missing, so nothing here is guarding anything - fix the path.");

            string source = File.ReadAllText(full);
            source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            source = Regex.Replace(source, @"//[^\r\n]*", " ");
            return source;
        }

        /// <summary>The half-open character range of a function body, found by matching braces.</summary>
        private static void BodyOf(string source, string signature, out int start, out int end)
        {
            int declaration = source.IndexOf(signature, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(declaration, 0, "could not find " + signature + " in the kernel");

            start = source.IndexOf('{', declaration);
            Assert.GreaterOrEqual(start, 0, signature + " has no body");

            int depth = 0;
            for (int index = start; index < source.Length; index++)
            {
                if (source[index] == '{') { depth++; }
                else if (source[index] == '}')
                {
                    depth--;
                    if (depth == 0) { end = index; return; }
                }
            }

            Assert.Fail(signature + " has an unbalanced body, so this test cannot bound it");
            end = source.Length;
        }

        [Test]
        public void EveryTraceAgainstTheStructureStepsOutOfAvatarProxies()
        {
            string source = ReadKernelWithoutComments();
            BodyOf(source, "UnifiedRT::Hit " + EscapeFunction, out int start, out int end);

            MatchCollection traces = Regex.Matches(source, @"UnifiedRT::TraceRay\w*\s*\(");
            Assert.Greater(traces.Count, 0,
                "no traces found at all, so this test is reading the wrong file or the wrong syntax");

            foreach (Match trace in traces)
            {
                Assert.IsTrue(trace.Index > start && trace.Index < end,
                    "a ray is fired at the acceleration structure outside " + EscapeFunction + ", at character " +
                    trace.Index + ". Avatars are in that structure as capsules that swallow their own visible " +
                    "surface, so a ray starting on an avatar hits its own proxy at nearly zero distance and " +
                    "reads as fully enclosed. Route it through " + EscapeFunction + " like every other ray here.");
            }
        }

        [Test]
        public void NoRayIsAnAnyHit()
        {
            string source = ReadKernelWithoutComments();

            Assert.IsFalse(source.Contains("TraceRayAnyHit"),
                "an any-hit trace is back in the kernel. It is the cheaper primitive and it is the reason " +
                "this bug existed: any-hit answers whether SOMETHING was in the way and returns a bool, so " +
                "there is no instance to test the proxy flag on and no hit distance to step past - the " +
                "capsule escape cannot be written against it at all. If a future trace genuinely runs " +
                "against a structure with no avatar proxies in it, this test is the place to say so.");
        }

        [Test]
        public void TheLightVisibilityRayStillEscapesProxies()
        {
            string source = ReadKernelWithoutComments();
            BodyOf(source, "float3 " + LightingFunction, out int start, out int end);

            string body = source.Substring(start, end - start);
            Assert.IsTrue(body.Contains(EscapeFunction + "("),
                LightingFunction + " no longer walks its shadow ray out of avatar capsules. This is the " +
                "exact call site that painted a hard edged black patch, shaped like the torso capsule, onto " +
                "every directly lit avatar in ray traced mode.");
        }
    }
}
