using System;
using Basis.Scripts.BasisSdk.Players;
using NUnit.Framework;

/// <summary>
/// The two far avatar desire predicates: the transmit tick's <c>WantsFarAvatar</c> and
/// CreateAvatar's <c>WantsFarAvatarAfterLoad</c>. Their agreement outside a load is the
/// invariant that keeps CreateAvatar's direct swap and the tick from reversing each other
/// (each reversal is a full avatar install).
/// </summary>
public class BasisAvatarFarLodPolicyTests
{
    private bool _savedEnabled;

    [SetUp]
    public void SaveEnabled()
    {
        _savedEnabled = BasisAvatarFarLOD.Enabled;
    }

    [TearDown]
    public void RestoreEnabled()
    {
        BasisAvatarFarLOD.Enabled = _savedEnabled;
    }

    private static BasisRemotePlayer BuildRemote(bool blocked, bool hasPayload, bool inRange, bool failed, bool alwaysShow, bool loading)
    {
        BasisRemotePlayer remote = new BasisRemotePlayer
        {
            DisplayName = "policy-test",
            UUID = Guid.NewGuid().ToString("N"),
            IsBlocked = blocked,
            InAvatarRange = inRange,
            HasFailedAvatarLoadGlobally = failed,
            AlwaysShowAvatar = alwaysShow,
            IsLoadingAnAvatar = loading,
        };
        if (hasPayload)
        {
            remote.FarLodOverridePayload = "non-empty-payload";
            remote.FarLodOverrideVersion = "v";
        }
        return remote;
    }

    [Test]
    public void AfterLoadProjection_AgreesWithTick_ForEveryStateOutsideALoad()
    {
        for (int combination = 0; combination < 64; combination++)
        {
            bool enabled = (combination & 1) != 0;
            bool blocked = (combination & 2) != 0;
            bool hasPayload = (combination & 4) != 0;
            bool inRange = (combination & 8) != 0;
            bool failed = (combination & 16) != 0;
            bool alwaysShow = (combination & 32) != 0;

            BasisAvatarFarLOD.Enabled = enabled;
            BasisRemotePlayer remote = BuildRemote(blocked, hasPayload, inRange, failed, alwaysShow, loading: false);

            bool tick = BasisAvatarFarLOD.WantsFarAvatar(remote);
            bool afterLoad = BasisAvatarFarLOD.WantsFarAvatarAfterLoad(remote);
            Assert.AreEqual(tick, afterLoad,
                $"predicates disagree at enabled={enabled} blocked={blocked} payload={hasPayload} inRange={inRange} failed={failed} alwaysShow={alwaysShow} — the tick would reverse CreateAvatar's swap forever");
        }
    }

    [Test]
    public void WantsFarAvatar_BridgesDownloadsForAlwaysShowPlayers()
    {
        BasisAvatarFarLOD.Enabled = true;
        BasisRemotePlayer loading = BuildRemote(blocked: false, hasPayload: true, inRange: true, failed: false, alwaysShow: true, loading: true);
        Assert.IsTrue(BasisAvatarFarLOD.WantsFarAvatar(loading), "mid-download the far avatar fronts even always-show players");

        BasisRemotePlayer idle = BuildRemote(blocked: false, hasPayload: true, inRange: false, failed: false, alwaysShow: true, loading: false);
        Assert.IsFalse(BasisAvatarFarLOD.WantsFarAvatar(idle), "always-show pins the real avatar outside loads");
        Assert.IsFalse(BasisAvatarFarLOD.WantsFarAvatarAfterLoad(idle));
    }

    [Test]
    public void WantsFarAvatar_CoreGates()
    {
        BasisAvatarFarLOD.Enabled = true;

        Assert.IsTrue(BasisAvatarFarLOD.WantsFarAvatar(
            BuildRemote(blocked: false, hasPayload: true, inRange: false, failed: false, alwaysShow: false, loading: false)),
            "out of range with a payload wears the far avatar");

        Assert.IsFalse(BasisAvatarFarLOD.WantsFarAvatar(
            BuildRemote(blocked: false, hasPayload: false, inRange: false, failed: false, alwaysShow: false, loading: false)),
            "no payload, no far avatar");

        Assert.IsFalse(BasisAvatarFarLOD.WantsFarAvatar(
            BuildRemote(blocked: true, hasPayload: true, inRange: false, failed: false, alwaysShow: false, loading: false)),
            "blocked players are never represented");

        Assert.IsTrue(BasisAvatarFarLOD.WantsFarAvatar(
            BuildRemote(blocked: false, hasPayload: true, inRange: true, failed: true, alwaysShow: false, loading: false)),
            "failed load in range still shows the far silhouette");

        BasisAvatarFarLOD.Enabled = false;
        Assert.IsFalse(BasisAvatarFarLOD.WantsFarAvatar(
            BuildRemote(blocked: false, hasPayload: true, inRange: false, failed: false, alwaysShow: false, loading: false)),
            "master switch off drops to the loading dummy");
    }
}
