using System;
using NUnit.Framework;

namespace Basis.ImagePickup.Tests
{
    /// <summary>
    /// Covers the clipboard bitmap reader. These are the cases a paste actually produces — different
    /// applications reach for different depths, row orders, and alpha conventions — and none of them
    /// announce themselves the way a file signature does, so each one is asserted directly.
    /// </summary>
    public class BasisDibImageTests
    {
        private const int BitmapInfoHeaderSize = 40;

        private const int BitmapV5HeaderSize = 124;

        private const uint BI_RGB = 0;
        private const uint BI_RLE8 = 1;
        private const uint BI_BITFIELDS = 3;
        private const uint BI_PNG = 5;
        private const uint BI_ALPHABITFIELDS = 6;

        /// <summary>
        /// Builds a packed DIB the way the clipboard carries one: header, optional masks and palette,
        /// then rows padded out to four bytes. Row data is supplied bottom-up when
        /// <paramref name="height"/> is positive, matching the format's own default.
        ///
        /// Masks always sit at offset 40, but where the pixels start does not: a V2-or-later header
        /// contains them, while a plain BITMAPINFOHEADER has them appended and everything after moves.
        /// Getting that wrong shifts the whole image by one mask, which is exactly the bug worth
        /// building both shapes to catch.
        /// </summary>
        private static byte[] BuildDib(
            int width,
            int height,
            int bitCount,
            uint compression,
            byte[] rows,
            uint[] masks = null,
            byte[] palette = null,
            uint paletteEntries = 0,
            int headerSize = BitmapInfoHeaderSize
        )
        {
            bool masksInsideHeader = headerSize > BitmapInfoHeaderSize;
            int maskBytes = masks != null && !masksInsideHeader ? masks.Length * 4 : 0;
            int paletteBytes = palette?.Length ?? 0;
            var dib = new byte[headerSize + maskBytes + paletteBytes + rows.Length];

            WriteUInt32(dib, 0, (uint)headerSize);
            WriteInt32(dib, 4, width);
            WriteInt32(dib, 8, height);
            WriteUInt16(dib, 12, 1);
            WriteUInt16(dib, 14, (ushort)bitCount);
            WriteUInt32(dib, 16, compression);
            WriteUInt32(dib, 20, (uint)rows.Length);
            WriteUInt32(dib, 32, paletteEntries);

            if (masks != null)
            {
                for (int i = 0; i < masks.Length; i++)
                {
                    WriteUInt32(dib, BitmapInfoHeaderSize + i * 4, masks[i]);
                }
            }

            int offset = headerSize + maskBytes;
            if (palette != null)
            {
                Buffer.BlockCopy(palette, 0, dib, offset, palette.Length);
                offset += palette.Length;
            }

            Buffer.BlockCopy(rows, 0, dib, offset, rows.Length);
            return dib;
        }

        private static void WriteUInt16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteInt32(byte[] data, int offset, int value) =>
            WriteUInt32(data, offset, unchecked((uint)value));

        private static void AssertPixel(BasisDibDecodeResult result, int x, int y, byte r, byte g, byte b, byte a)
        {
            int index = (y * result.Width + x) * 4;
            Assert.That(result.Rgba[index], Is.EqualTo(r), $"red at ({x},{y})");
            Assert.That(result.Rgba[index + 1], Is.EqualTo(g), $"green at ({x},{y})");
            Assert.That(result.Rgba[index + 2], Is.EqualTo(b), $"blue at ({x},{y})");
            Assert.That(result.Rgba[index + 3], Is.EqualTo(a), $"alpha at ({x},{y})");
        }

        [Test]
        public void BottomUpRowsAreFlippedAndChannelsReordered()
        {
            // Two 24-bit rows stored bottom-up and BGR: the first row in the buffer is the last row of
            // the picture, so a reader that trusts the buffer order silently renders it upside down.
            byte[] rows =
            {
                0x00, 0x00, 0xFF, 0x00, // bottom row: red, padded to four bytes
                0xFF, 0x00, 0x00, 0x00, // top row: blue
            };

            BasisDibDecodeResult result = BasisDibImage.Decode(BuildDib(1, 2, 24, BI_RGB, rows));

            Assert.That(result.Ok, Is.True, result.Error);
            Assert.That(result.Kind, Is.EqualTo(BasisDibPayloadKind.Pixels));
            Assert.That(result.Width, Is.EqualTo(1));
            Assert.That(result.Height, Is.EqualTo(2));
            AssertPixel(result, 0, 0, 0, 0, 255, 255);
            AssertPixel(result, 0, 1, 255, 0, 0, 255);
        }

        [Test]
        public void NegativeHeightIsReadTopDown()
        {
            byte[] rows =
            {
                0x00, 0x00, 0xFF, 0x00, // top row: red
                0xFF, 0x00, 0x00, 0x00, // bottom row: blue
            };

            BasisDibDecodeResult result = BasisDibImage.Decode(BuildDib(1, -2, 24, BI_RGB, rows));

            Assert.That(result.Ok, Is.True, result.Error);
            Assert.That(result.Height, Is.EqualTo(2));
            AssertPixel(result, 0, 0, 255, 0, 0, 255);
            AssertPixel(result, 0, 1, 0, 0, 255, 255);
        }

        [Test]
        public void RowPaddingIsSkipped()
        {
            // Three 24-bit pixels are nine bytes, which the format pads to twelve. Reading straight
            // through without honouring the stride shears every row after the first.
            byte[] rows =
            {
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0xEE, 0xEE, 0xEE,
                0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0xEE, 0xEE, 0xEE,
            };

            BasisDibDecodeResult result = BasisDibImage.Decode(BuildDib(3, -2, 24, BI_RGB, rows));

            Assert.That(result.Ok, Is.True, result.Error);
            AssertPixel(result, 0, 0, 0x03, 0x02, 0x01, 255);
            AssertPixel(result, 2, 0, 0x09, 0x08, 0x07, 255);
            AssertPixel(result, 0, 1, 0x13, 0x12, 0x11, 255);
            AssertPixel(result, 2, 1, 0x19, 0x18, 0x17, 255);
        }

        [Test]
        public void ThirtyTwoBitAlphaIsPreservedWhenAnyPixelSetsIt()
        {
            byte[] rows =
            {
                0x10, 0x20, 0x30, 0x00, // fully transparent
                0x40, 0x50, 0x60, 0x80, // half transparent
            };

            BasisDibDecodeResult result = BasisDibImage.Decode(BuildDib(2, -1, 32, BI_RGB, rows));

            Assert.That(result.Ok, Is.True, result.Error);
            AssertPixel(result, 0, 0, 0x30, 0x20, 0x10, 0x00);
            AssertPixel(result, 1, 0, 0x60, 0x50, 0x40, 0x80);
        }

        [Test]
        public void ThirtyTwoBitAlphaIsForcedOpaqueWhenNoPixelSetsIt()
        {
            // BI_RGB leaves the fourth byte undefined and many applications simply zero it. Taken at
            // face value that is a fully transparent image — an ordinary screenshot would vanish.
            byte[] rows =
            {
                0x10, 0x20, 0x30, 0x00,
                0x40, 0x50, 0x60, 0x00,
            };

            BasisDibDecodeResult result = BasisDibImage.Decode(BuildDib(2, -1, 32, BI_RGB, rows));

            Assert.That(result.Ok, Is.True, result.Error);
            AssertPixel(result, 0, 0, 0x30, 0x20, 0x10, 255);
            AssertPixel(result, 1, 0, 0x60, 0x50, 0x40, 255);
        }

        [Test]
        public void MaskedAlphaInsideAVersionFiveHeaderIsUsed()
        {
            // CF_DIBV5 is what a browser's "copy image" puts on the clipboard, and its masks live
            // inside the 124-byte header rather than after it.
            uint[] masks = { 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000 };
            byte[] rows =
            {
                0x10, 0x20, 0x30, 0x00, // transparent
                0x40, 0x50, 0x60, 0x80, // half transparent
            };

            BasisDibDecodeResult result = BasisDibImage.Decode(
                BuildDib(2, -1, 32, BI_BITFIELDS, rows, masks, headerSize: BitmapV5HeaderSize)
            );

            Assert.That(result.Ok, Is.True, result.Error);
            AssertPixel(result, 0, 0, 0x30, 0x20, 0x10, 0x00);
            AssertPixel(result, 1, 0, 0x60, 0x50, 0x40, 0x80);
        }

        [Test]
        public void AllZeroAlphaIsRescuedEvenWhenAMaskDeclaresIt()
        {
            // Declaring an alpha mask and then writing nothing but zeroes is a real and common defect.
            // Trusting it verbatim would make the paste a fully transparent card, so the rescue has to
            // survive an explicit mask and not just an absent one.
            uint[] masks = { 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000 };
            byte[] rows =
            {
                0x10, 0x20, 0x30, 0x00,
                0x40, 0x50, 0x60, 0x00,
            };

            BasisDibDecodeResult result = BasisDibImage.Decode(
                BuildDib(2, -1, 32, BI_BITFIELDS, rows, masks, headerSize: BitmapV5HeaderSize)
            );

            Assert.That(result.Ok, Is.True, result.Error);
            AssertPixel(result, 0, 0, 0x30, 0x20, 0x10, 255);
            AssertPixel(result, 1, 0, 0x60, 0x50, 0x40, 255);
        }

        [Test]
        public void AlphaBitfieldsAppendsFourMasksToAPlainHeader()
        {
            // The four-mask form of a 40-byte header is BI_ALPHABITFIELDS, not BI_BITFIELDS. The extra
            // mask displaces the pixel data, so miscounting it shears the image by one pixel.
            uint[] masks = { 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000 };
            byte[] rows = { 0x10, 0x20, 0x30, 0x40 };

            BasisDibDecodeResult result = BasisDibImage.Decode(BuildDib(1, -1, 32, BI_ALPHABITFIELDS, rows, masks));

            Assert.That(result.Ok, Is.True, result.Error);
            AssertPixel(result, 0, 0, 0x30, 0x20, 0x10, 0x40);
        }

        [Test]
        public void BitfieldsAppendsThreeMasksToAPlainHeader()
        {
            uint[] masks = { 0x00FF0000, 0x0000FF00, 0x000000FF };
            byte[] rows = { 0x10, 0x20, 0x30, 0x40 };

            BasisDibDecodeResult result = BasisDibImage.Decode(BuildDib(1, -1, 32, BI_BITFIELDS, rows, masks));

            Assert.That(result.Ok, Is.True, result.Error);
            // No alpha mask was declared, so the fourth byte carries no meaning and the pixel is opaque.
            AssertPixel(result, 0, 0, 0x30, 0x20, 0x10, 255);
        }

        [Test]
        public void SixteenBitChannelsScaleToFullRange()
        {
            // 5-6-5 white. Shifting instead of scaling lands red on 248 and green on 252, which reads
            // as a dull grey rather than white.
            uint[] masks = { 0xF800, 0x07E0, 0x001F };
            byte[] rows = { 0xFF, 0xFF, 0x00, 0x00 };

            BasisDibDecodeResult result = BasisDibImage.Decode(BuildDib(1, -1, 16, BI_BITFIELDS, rows, masks));

            Assert.That(result.Ok, Is.True, result.Error);
            AssertPixel(result, 0, 0, 255, 255, 255, 255);
        }

        [Test]
        public void SixteenBitWithoutMasksUsesTheFiveFiveFiveDefault()
        {
            byte[] rows = { 0x00, 0x7C, 0x00, 0x00 }; // 0x7C00: red at full strength

            BasisDibDecodeResult result = BasisDibImage.Decode(BuildDib(1, -1, 16, BI_RGB, rows));

            Assert.That(result.Ok, Is.True, result.Error);
            AssertPixel(result, 0, 0, 255, 0, 0, 255);
        }

        [Test]
        public void PaletteIndexedPixelsAreResolved()
        {
            byte[] palette =
            {
                0x00, 0x00, 0xFF, 0x00, // entry 0: red, stored BGRX
                0x00, 0xFF, 0x00, 0x00, // entry 1: green
            };
            byte[] rows = { 0x01, 0x00, 0x00, 0x00 };

            BasisDibDecodeResult result = BasisDibImage.Decode(
                BuildDib(1, -1, 8, BI_RGB, rows, null, palette, 2)
            );

            Assert.That(result.Ok, Is.True, result.Error);
            AssertPixel(result, 0, 0, 0, 255, 0, 255);
        }

        [Test]
        public void FourBitPixelsShareABytePerPair()
        {
            var palette = new byte[16 * 4];
            palette[4] = 0x00; // entry 1 blue channel
            palette[5] = 0x00;
            palette[6] = 0xFF; // entry 1 red channel
            palette[8] = 0xFF; // entry 2 blue channel

            byte[] rows = { 0x12, 0x00, 0x00, 0x00 };

            BasisDibDecodeResult result = BasisDibImage.Decode(BuildDib(2, -1, 4, BI_RGB, rows, null, palette));

            Assert.That(result.Ok, Is.True, result.Error);
            AssertPixel(result, 0, 0, 0xFF, 0x00, 0x00, 255);
            AssertPixel(result, 1, 0, 0x00, 0x00, 0xFF, 255);
        }

        [Test]
        public void EmbeddedPngStreamIsReturnedWhole()
        {
            byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02 };

            BasisDibDecodeResult result = BasisDibImage.Decode(BuildDib(4, -4, 32, BI_PNG, png));

            Assert.That(result.Ok, Is.True, result.Error);
            Assert.That(result.Kind, Is.EqualTo(BasisDibPayloadKind.EmbeddedPng));
            Assert.That(result.Encoded, Is.EqualTo(png));
        }

        [Test]
        public void TruncatedPixelDataIsRejected()
        {
            byte[] rows = { 0x00, 0x00, 0xFF, 0x00 }; // one row where the header claims four

            BasisDibDecodeResult result = BasisDibImage.Decode(BuildDib(1, 4, 24, BI_RGB, rows));

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error, Does.Contain("truncated"));
        }

        [Test]
        public void RunLengthEncodedBitmapsAreRejected()
        {
            byte[] rows = new byte[16];

            BasisDibDecodeResult result = BasisDibImage.Decode(BuildDib(2, 2, 8, BI_RLE8, rows));

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error, Does.Contain("Run-length"));
        }

        [Test]
        public void UnsupportedColourDepthIsRejected()
        {
            BasisDibDecodeResult result = BasisDibImage.Decode(BuildDib(2, 2, 2, BI_RGB, new byte[16]));

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error, Does.Contain("colour depth"));
        }

        [Test]
        public void CoreHeaderBitmapsAreRejected()
        {
            var dib = new byte[12 + 16];
            WriteUInt32(dib, 0, 12);
            WriteUInt16(dib, 4, 2);
            WriteUInt16(dib, 6, 2);

            BasisDibDecodeResult result = BasisDibImage.Decode(dib);

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Error, Does.Contain("core format"));
        }

        [TestCase(0, 4)]
        [TestCase(4, 0)]
        [TestCase(-70000, 4)]
        public void InvalidDimensionsAreRejectedBeforeAnyRowIsRead(int width, int height)
        {
            var dib = new byte[BitmapInfoHeaderSize];
            WriteUInt32(dib, 0, BitmapInfoHeaderSize);
            WriteInt32(dib, 4, width);
            WriteInt32(dib, 8, height);

            Assert.That(BasisDibImage.TryReadDimensions(dib, out _, out _, out string error), Is.False);
            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void DimensionsAreReadWithoutUnpackingRows()
        {
            // A header alone is enough to reject an oversized paste, which is the point: nothing has
            // to be allocated for the pixels to find out they are too big.
            var dib = new byte[BitmapInfoHeaderSize];
            WriteUInt32(dib, 0, BitmapInfoHeaderSize);
            WriteInt32(dib, 4, 1920);
            WriteInt32(dib, 8, -1080);

            Assert.That(BasisDibImage.TryReadDimensions(dib, out int width, out int height, out _), Is.True);
            Assert.That(width, Is.EqualTo(1920));
            Assert.That(height, Is.EqualTo(1080));
        }
    }
}
