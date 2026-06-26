using System;
using System.IO;
using UnityEngine;

namespace Basis.ImagePickup
{
    public struct BasisImageValidationResult
    {
        public bool Ok;
        public string Error;
        public Texture2D Texture;
        public byte[] CleanPng;
        public int Width;
        public int Height;
    }

    /// <summary>
    /// PNG import hardening applied to both file drops and bytes received over the network.
    /// Mirrors the OWASP File Upload guidance adapted to a local/relayed context: extension allowlist,
    /// magic-byte signature, size cap, an IHDR dimension parse that runs before any decode (decompression
    /// bomb guard), a guarded decode, and a re-encode that rewrites the pixels to strip injected payloads.
    /// </summary>
    public static class BasisImageSecurity
    {
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static string GenerateSafeFileName() => $"Image_{Guid.NewGuid():N}.png";

        public static bool HasPngExtension(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            foreach (char c in path)
            {
                if (c == '\0') return false;
            }
            return Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Validates a PNG file on disk and returns a sanitized, re-encoded texture ready to display and transmit.</summary>
        public static BasisImageValidationResult ValidateFile(string path)
        {
            var result = new BasisImageValidationResult();

            if (!HasPngExtension(path)) { result.Error = "Not a .png file"; return result; }

            FileInfo info;
            try { info = new FileInfo(path); }
            catch (Exception e) { result.Error = "Invalid path: " + e.Message; return result; }

            if (!info.Exists) { result.Error = "File not found"; return result; }
            if (info.Length <= 0) { result.Error = "Empty file"; return result; }
            if (info.Length > BasisImagePickupSettings.MaxImageBytes) { result.Error = $"File too large ({info.Length} bytes)"; return result; }

            byte[] header = new byte[24];
            try
            {
                using FileStream fs = File.OpenRead(path);
                if (fs.Read(header, 0, header.Length) < header.Length) { result.Error = "File truncated"; return result; }
            }
            catch (Exception e) { result.Error = "Read failed: " + e.Message; return result; }

            if (!TryReadPngDimensions(header, out int w, out int h, out string headerError)) { result.Error = headerError; return result; }
            if (!DimensionsWithinCaps(w, h, out string capError)) { result.Error = capError; return result; }

            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception e) { result.Error = "Read failed: " + e.Message; return result; }

            if (bytes.Length > BasisImagePickupSettings.MaxImageBytes) { result.Error = "File too large"; return result; }

            return BuildFromBytes(bytes, true);
        }

        /// <summary>Validates PNG bytes received over the network. Never trusts the wire: same caps and guarded decode.</summary>
        public static BasisImageValidationResult ValidateBytes(byte[] bytes)
        {
            var result = new BasisImageValidationResult();
            if (bytes == null || bytes.Length == 0) { result.Error = "No data"; return result; }
            if (bytes.Length > BasisImagePickupSettings.MaxImageBytes) { result.Error = "Too large"; return result; }
            if (!TryReadPngDimensions(bytes, out int w, out int h, out string headerError)) { result.Error = headerError; return result; }
            if (!DimensionsWithinCaps(w, h, out string capError)) { result.Error = capError; return result; }
            return BuildFromBytes(bytes, false);
        }

        private static BasisImageValidationResult BuildFromBytes(byte[] bytes, bool reencode)
        {
            var result = new BasisImageValidationResult();

            if (!VerifySignature(bytes)) { result.Error = "Not a PNG (bad signature)"; return result; }
            if (!TryReadPngDimensions(bytes, out int headerW, out int headerH, out string headerError)) { result.Error = headerError; return result; }
            if (!DimensionsWithinCaps(headerW, headerH, out string capError)) { result.Error = capError; return result; }

            Texture2D decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            bool loaded;
            try { loaded = decoded.LoadImage(bytes, false); }
            catch (Exception e) { UnityEngine.Object.Destroy(decoded); result.Error = "Decode failed: " + e.Message; return result; }

            if (!loaded) { UnityEngine.Object.Destroy(decoded); result.Error = "Decode failed"; return result; }
            if (decoded.width != headerW || decoded.height != headerH) { UnityEngine.Object.Destroy(decoded); result.Error = "Header/pixel size mismatch"; return result; }
            if (!DimensionsWithinCaps(decoded.width, decoded.height, out string decodeCapError)) { UnityEngine.Object.Destroy(decoded); result.Error = decodeCapError; return result; }

            byte[] clean = bytes;
            if (reencode)
            {
                try { clean = decoded.EncodeToPNG(); }
                catch (Exception e) { UnityEngine.Object.Destroy(decoded); result.Error = "Re-encode failed: " + e.Message; return result; }
                if (clean == null || clean.Length == 0 || clean.Length > BasisImagePickupSettings.MaxImageBytes)
                {
                    UnityEngine.Object.Destroy(decoded);
                    result.Error = "Re-encoded image invalid";
                    return result;
                }
            }

            decoded.wrapMode = TextureWrapMode.Clamp;
            decoded.Apply(false, true);

            result.Ok = true;
            result.Texture = decoded;
            result.CleanPng = clean;
            result.Width = headerW;
            result.Height = headerH;
            return result;
        }

        private static bool VerifySignature(byte[] data)
        {
            if (data == null || data.Length < PngSignature.Length) return false;
            for (int i = 0; i < PngSignature.Length; i++)
            {
                if (data[i] != PngSignature[i]) return false;
            }
            return true;
        }

        private static bool TryReadPngDimensions(byte[] data, out int width, out int height, out string error)
        {
            width = 0;
            height = 0;
            error = null;

            if (data == null || data.Length < 24) { error = "Header too short"; return false; }
            if (!VerifySignature(data)) { error = "Not a PNG (bad signature)"; return false; }
            if (data[12] != (byte)'I' || data[13] != (byte)'H' || data[14] != (byte)'D' || data[15] != (byte)'R') { error = "Missing IHDR"; return false; }

            width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
            height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];

            if (width <= 0 || height <= 0) { error = "Invalid dimensions"; return false; }
            return true;
        }

        private static bool DimensionsWithinCaps(int width, int height, out string error)
        {
            error = null;
            if (width > BasisImagePickupSettings.MaxDimension || height > BasisImagePickupSettings.MaxDimension)
            {
                error = $"Image too large ({width}x{height}, max {BasisImagePickupSettings.MaxDimension})";
                return false;
            }
            if ((long)width * height > BasisImagePickupSettings.MaxTotalPixels)
            {
                error = "Image exceeds pixel budget";
                return false;
            }
            return true;
        }
    }
}
