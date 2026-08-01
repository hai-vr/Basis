using UnityEngine;
[System.Serializable]
public class BasisBEEExtensionMeta
{
    [SerializeField]
    public BasisRemoteEncyptedBundle StoredRemote = new BasisRemoteEncyptedBundle();//where we got meta file from
    [SerializeField]
    public BasisStoredEncryptedBundle StoredLocal = new BasisStoredEncryptedBundle();//where we got bundle file from
    public string UniqueVersion;
    public string DownloadedPlatform;
    // Content version the cached payload actually corresponds to — the validator the SERVER
    // reported when these bytes were fetched, never a tag a peer merely claimed. Kept separate
    // from StoredRemote.RemoteVersionTag (which is whatever the requester asked for) so an
    // attacker-supplied tag can never be written into the cache as if it were verified.
    // Empty means the entry predates versioning, and BasisContentVersion treats that as
    // "unknown", not "current".
    public string CachedVersionTag;
    // When this entry was last checked against the remote, as Unix seconds UTC. Zero means never.
    // Only advisory: it drives the revalidation throttle and the storage UI, never correctness.
    public long LastValidatedUnixUtc;
}
