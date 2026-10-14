using System;
using System.Collections.Concurrent;

namespace BasisNetworkServer.Storage
{
    public sealed class BasisMemoryStorageBackend : IBasisStorageBackend
    {
        private readonly ConcurrentDictionary<string, byte[]> store = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        public string Name => "memory";
        public void Open()
        {
        }
        public bool TryLoad(string ownerKey, out byte[] blob) => store.TryGetValue(ownerKey, out blob);
        public bool TrySave(string ownerKey, byte[] blob, long expectedRevision, long revision)
        {
            if (blob == null || blob.Length > BasisStorageRecord.MaxBlobBytes) throw new ArgumentException("Storage record is missing or too large.", nameof(blob));
            store[ownerKey] = blob;
            return true;
        }
        public bool TryDelete(string ownerKey, long expectedRevision)
        {
            store.TryRemove(ownerKey, out _);
            return true;
        }
        public void Quarantine(string ownerKey) => store.TryRemove(ownerKey, out _);
        public void Measure(out long records, out long bytes)
        {
            records = 0;
            bytes = 0;
            foreach (byte[] blob in store.Values)
            {
                records++;
                bytes += blob.Length;
            }
        }
        public void Dispose() => store.Clear();
    }
}
