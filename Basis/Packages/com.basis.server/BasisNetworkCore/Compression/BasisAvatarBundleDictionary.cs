using System;

namespace Basis.Network.Core.Compression
{
    /// <summary>
    /// The Zstd dictionary both ends of <see cref="BasisAvatarBundleZstd"/> compress against.
    ///
    /// ⚠️ GENERATED — the body of this file is rewritten by the dictionary trainer
    /// (BasisServerTests/BundleDictionaryTrainer.cs). Hand-edit the doc comments if you like,
    /// but <see cref="Generation"/> and <see cref="Base64"/> are overwritten wholesale on the
    /// next training run.
    ///
    /// ── Why the dictionary is embedded in source rather than shipped as a file ───────────────
    ///
    /// Server and client MUST compress against byte-identical dictionary content or every
    /// bundle decodes to garbage — and because the frames are written with the dictionary id
    /// suppressed, zstd itself will not catch the mismatch. Embedding the bytes in
    /// BasisNetworkCore, which is the assembly both trees already share, makes divergence
    /// impossible by construction: there is one copy, versioned with the protocol.
    ///
    /// <see cref="Generation"/> is carried in the bundle flags byte so a future retrain can be
    /// rolled out without a wire-format change — a decoder that does not hold the generation a
    /// bundle names simply drops it, and the server falls back to LZ4 for peers it cannot
    /// serve. Bump it on every retrain; never reuse a generation for different bytes.
    ///
    /// Generation 0 is reserved for "no dictionary embedded", which disables the Zstd path
    /// entirely (<see cref="BasisAvatarBundleZstd.Available"/> is false) and leaves the server
    /// on LZ4. That is the correct state before the first training run: dictionary-less Zstd at
    /// this level measured worse than LZ4, so there is no partial win to ship.
    /// </summary>
    public static class BasisAvatarBundleDictionary
    {
        /// <summary>
        /// Dictionary generation, 1..31. 0 means no dictionary is embedded and the Zstd bundle
        /// codec is inert. Carried in the bundle flags byte.
        /// </summary>
        public const byte Generation = 0;

        /// <summary>
        /// Base64 of the raw zstd dictionary. Empty while <see cref="Generation"/> is 0.
        /// Stored as base64 rather than a byte-array literal so the generated file stays a few
        /// hundred lines instead of ~16k, and so a diff shows it as one changed blob.
        /// </summary>
        public const string Base64 = "";

        /// <summary>Decoded dictionary bytes; empty when no dictionary is embedded.</summary>
        public static readonly byte[] Bytes = Decode();

        private static byte[] Decode()
        {
            string b64 = Base64;
            if (b64.Length == 0) return Array.Empty<byte>();
            return Convert.FromBase64String(b64);
        }
    }
}
