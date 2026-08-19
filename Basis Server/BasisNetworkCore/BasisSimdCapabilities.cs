using System.Numerics;
using System.Text;
#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
#endif

namespace Basis.Network.Core
{
    /// <summary>
    /// What vector width and instruction sets this process actually got, for the boot log.
    ///
    /// <para>Every vectorised path in the server is written against <see cref="Vector{T}"/> and
    /// selected by the JIT at runtime, which means the same binary runs 16, 32 or 64 bytes at a time
    /// depending on the host and nothing in the build says which. That is the right trade — one
    /// implementation, ARM included — but it makes the width invisible exactly when someone is trying
    /// to explain a throughput difference between two machines. Printing it once at boot is the whole
    /// point of this type.</para>
    ///
    /// <para><b>Two traps worth knowing before changing how the server is built:</b></para>
    /// <list type="bullet">
    /// <item><description><b>ReadyToRun / AOT silently costs the vector width.</b> R2R precompiles
    /// against a conservative ISA baseline (SSE2-era on x64), so <c>Vector&lt;byte&gt;.Count</c> is
    /// baked at 16 and the AVX2 path is never generated. Tiered compilation does re-JIT hot methods at
    /// the real width, but a startup-time optimisation that quietly halves the width of every vector
    /// loop until then is not free. If R2R is ever adopted here, publish with an explicit
    /// <c>&lt;PublishReadyToRunUseCrossgen2&gt;</c> instruction-set baseline and re-measure — do not
    /// assume it is a pure win.</description></item>
    /// <item><description><b>AVX-512 is opt-in at runtime, not at build time.</b> .NET defaults its
    /// preferred vector width to 256 bits even on hardware with AVX-512, because 512-bit loops
    /// down-clock some parts. <c>DOTNET_PreferredVectorBitWidth=512</c> raises it. It is an
    /// environment variable rather than a code change precisely so it can be A/B'd on a real host;
    /// this line is how you confirm it took effect.</description></item>
    /// </list>
    /// </summary>
    public static class BasisSimdCapabilities
    {
        /// <summary>Bytes processed per <see cref="Vector{T}"/> operation on this host.</summary>
        public static int VectorByteWidth => Vector<byte>.Count;

        /// <summary>False means every vector path is running as a scalar loop and is a red flag.</summary>
        public static bool HardwareAccelerated => Vector.IsHardwareAccelerated;

        /// <summary>
        /// One line for the boot log: the width actually in force, then the instruction sets behind it.
        /// </summary>
        public static string Describe()
        {
            var sb = new StringBuilder(160);
            sb.Append(Vector.IsHardwareAccelerated
                ? $"{Vector<byte>.Count * 8}-bit vectors ({Vector<byte>.Count} B/op)"
                : "NO hardware vectors - every vector path is running scalar");

#if NET8_0_OR_GREATER
            sb.Append(" [");
            bool any = false;
            void Add(string name, bool supported)
            {
                if (!supported) return;
                if (any) sb.Append(' ');
                sb.Append(name);
                any = true;
            }

            Add("AVX512F", Avx512F.IsSupported);
            Add("AVX2", Avx2.IsSupported);
            Add("SSE4.2", Sse42.IsSupported);
            Add("BMI2", Bmi2.IsSupported);
            Add("NEON", AdvSimd.IsSupported);
            Add("CRC32", Crc32.IsSupported);
            if (!any) sb.Append("baseline only");
            sb.Append(']');

            // Worth surfacing rather than leaving to be discovered: the machine can do 512 and the
            // runtime has chosen not to, which is a one-environment-variable difference.
            if (Vector512.IsHardwareAccelerated && Vector<byte>.Count < 64)
            {
                sb.Append(" - host supports 512-bit; set DOTNET_PreferredVectorBitWidth=512 to use it");
            }
#endif
            return sb.ToString();
        }
    }
}
