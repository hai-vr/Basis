using System;
namespace Basis.Scripts.Drivers
{
    [Flags]
    public enum BasisDeviceStreamFlags : uint
    {
        None = 0,

        /// <summary>
        /// Body is Deflate-compressed. Lossless, so decoded bytes are identical either way; only the
        /// FILE bytes differ. Nothing should ever assert on compressed bytes — the deflate encoder's
        /// output is a .NET implementation detail and may change between runtimes.
        /// </summary>
        DeflateBody = 1 << 0,
    }
}
