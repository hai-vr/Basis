using System;

namespace BasisNetworkServer.Storage
{
    public interface IBasisStorageBackend : IDisposable
    {
        string Name { get; }
        void Open();
        bool TryLoad(string ownerKey, out byte[] blob);
        bool TrySave(string ownerKey, byte[] blob, long expectedRevision, long revision);
        bool TryDelete(string ownerKey, long expectedRevision);
        void Quarantine(string ownerKey);
        void Measure(out long records, out long bytes);
    }
}
