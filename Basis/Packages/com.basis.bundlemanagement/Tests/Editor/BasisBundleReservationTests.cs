using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Covers load reservation pairing. A release has to find the wrapper the reservation was actually
/// taken on: the registry key carries the content version tag, and other systems write that tag
/// onto the record after the load has already reserved against it. A release that recomputes the
/// key from the drifted record decrements whichever wrapper it lands on, and that wrapper can then
/// reach zero and unload assets its own live instances are still built from.
/// </summary>
public class BasisBundleReservationTests
{
    private const string Url = "https://example.invalid/reservation-tests.bee";

    private readonly List<string> _registered = new List<string>();

    [TearDown]
    public void TearDown()
    {
        foreach (string key in _registered)
        {
            BasisLoadHandler.LoadedBundles.TryRemove(key, out _);
        }
        _registered.Clear();
    }

    private static BasisLoadableBundle MakeBundle(string versionTag)
    {
        return new BasisLoadableBundle
        {
            UnlockPassword = string.Empty,
            BasisRemoteBundleEncrypted = new BasisRemoteEncyptedBundle
            {
                RemoteBeeFileLocation = Url,
                RemoteVersionTag = versionTag
            }
        };
    }

    private BasisTrackedBundleWrapper Register(BasisLoadableBundle bundle)
    {
        string key = BasisLoadHandler.GetBundleKey(bundle);
        BasisTrackedBundleWrapper wrapper = new BasisTrackedBundleWrapper
        {
            AssetBundle = null,
            LoadableBundle = bundle,
            RegisteredKey = key,
            // Short-circuits UnloadIfReady, so the release under test resolves synchronously
            // instead of sitting on the unload grace timer. The DeIncrement still happens first,
            // which is the part being asserted.
            IsUnloaded = true
        };
        Assert.IsTrue(BasisLoadHandler.LoadedBundles.TryAdd(key, wrapper), "test key collided with an existing registry entry");
        _registered.Add(key);
        return wrapper;
    }

    private static void Release(BasisLoadableBundle bundle)
    {
        BasisLoadHandler.RequestDeIncrementOfBundle(bundle).GetAwaiter().GetResult();
    }

    /// <summary>The premise: a drifting tag really does move the key, so a release must not recompute it.</summary>
    [Test]
    public void BundleKeyMovesWhenTheVersionTagDrifts()
    {
        BasisLoadableBundle bundle = MakeBundle("v1");
        string before = BasisLoadHandler.GetBundleKey(bundle);

        bundle.BasisRemoteBundleEncrypted.RemoteVersionTag = "v2";
        string after = BasisLoadHandler.GetBundleKey(bundle);

        Assert.AreNotEqual(before, after, "the key must be version aware, which is exactly why a release cannot re-derive it");
    }

    [Test]
    public void TicketedReleasePaysItsOwnWrapperWhenTheVersionTagDrifts()
    {
        BasisLoadableBundle firstVersion = MakeBundle("v1");
        BasisLoadableBundle secondVersion = MakeBundle("v2");
        BasisTrackedBundleWrapper wrapperOne = Register(firstVersion);
        BasisTrackedBundleWrapper wrapperTwo = Register(secondVersion);

        // Both are worn: one reservation each, the way two live avatars on the same url at
        // different versions would sit in the registry.
        wrapperOne.Increment();
        firstVersion.ReservedWrapperKey = wrapperOne.RegisteredKey;
        wrapperTwo.Increment();

        // What LibraryProvider does to a record a load has already reserved against.
        firstVersion.BasisRemoteBundleEncrypted.RemoteVersionTag = "v2";

        Release(firstVersion);

        Assert.IsFalse(wrapperOne.IsInUse, "the reservation was not paid back to the wrapper that took it");
        Assert.IsTrue(wrapperTwo.IsInUse, "a drifted tag decremented an unrelated wrapper's live reservation");
    }

    [Test]
    public void ReleaseConsumesTheTicketSoTheSameReservationCannotBePaidTwice()
    {
        BasisLoadableBundle bundle = MakeBundle("v1");
        BasisTrackedBundleWrapper wrapper = Register(bundle);

        wrapper.Increment();
        wrapper.Increment();
        bundle.ReservedWrapperKey = wrapper.RegisteredKey;

        Release(bundle);

        Assert.IsNull(bundle.ReservedWrapperKey, "the ticket must be consumed by the release that used it");
        Assert.IsTrue(wrapper.IsInUse, "one release must not pay down two reservations");
    }

}
