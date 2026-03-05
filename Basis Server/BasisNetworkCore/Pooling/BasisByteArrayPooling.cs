using System.Buffers;

namespace BasisNetworkCore.Pooling
{
    /// <summary>
    /// Byte array pool backed by ArrayPool&lt;byte&gt;.Shared — lock-free, thread-local cached,
    /// zero GC on the hot path.
    /// Note: Rent() may return an array larger than requested. Callers must track the
    /// actual needed length separately and must not rely on array.Length for data bounds.
    /// </summary>
    public static class BasisByteArrayPooling
    {
        public static byte[] Rent(int size)
        {
            return ArrayPool<byte>.Shared.Rent(size);
        }

        public static void Return(byte[] array)
        {
            if (array == null) return;
            ArrayPool<byte>.Shared.Return(array);
        }
    }
}
