namespace Basis.Scripts.Platform
{
    public enum BasisClipboardImageFormat
    {
        None = 0,
        /// <summary>A complete PNG file, signature and all.</summary>
        Png = 1,
        /// <summary>A complete GIF file. May be animated.</summary>
        Gif = 2,
        /// <summary>A complete JPEG file.</summary>
        Jpeg = 3,
        /// <summary>
        /// A packed device-independent bitmap: a BITMAPINFO/V4/V5 header, optional colour masks and
        /// palette, then raw rows. This is what "copy image" produces in most Windows applications,
        /// and it is not a file — there is no signature and no container, so a consumer must parse
        /// the header itself rather than handing it to an image loader.
        /// </summary>
        Bitmap = 4,
    }
}
