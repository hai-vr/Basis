using System;
using System.Diagnostics;
using System.Threading;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// State-machine behavior of the far avatar builder around the worker-thread parse: the
/// non-blocking tick-only install path, the CreateAvatar-side parse prewarm, refused-payload
/// latching, and the already-wearing short-circuits. Install success paths (calibration,
/// bone jobs) need a running player stack and stay out of edit mode.
/// </summary>
public class BasisFarAvatarBuilderStateTests
{
    private static BasisRemotePlayer NewRemote()
    {
        return new BasisRemotePlayer
        {
            DisplayName = "far-test",
            UUID = Guid.NewGuid().ToString("N"),
        };
    }

    [Test]
    public void TryInstall_WithoutAnyPayload_ReturnsFalse()
    {
        BasisRemotePlayer remote = NewRemote();
        Assert.IsFalse(BasisFarAvatarBuilder.TryInstall(remote));
    }

    [Test]
    public void TryInstall_NullRemote_ReturnsFalse()
    {
        Assert.IsFalse(BasisFarAvatarBuilder.TryInstall(null));
    }

    [Test]
    public void PrewarmParse_WithoutAnyPayload_IsANoOp()
    {
        BasisRemotePlayer remote = NewRemote();
        Assert.DoesNotThrow(() => BasisFarAvatarBuilder.PrewarmParse(remote));
        Assert.DoesNotThrow(() => BasisFarAvatarBuilder.PrewarmParse(null));
        Assert.IsFalse(BasisFarAvatarBuilder.TryInstall(remote));
    }

    [Test]
    public void TryInstall_RefusedPayload_LatchesUnusableWithoutRetrySpam()
    {
        BasisRemotePlayer remote = NewRemote();
        remote.FarLodOverridePayload = BasisFarLodTestPayloads.CreateRefusedBase64();
        remote.FarLodOverrideVersion = $"refused-{Guid.NewGuid():N}";
        Assert.IsTrue(remote.HasFarLodPayload, "payload string present → considered available until tried");

        // First call kicks the worker parse and declines; the retry loop mirrors the
        // transmit tick calling back until the parse lands.
        Stopwatch deadline = Stopwatch.StartNew();
        bool installed = BasisFarAvatarBuilder.TryInstall(remote);
        while (remote.HasFarLodPayload && deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            Assert.IsFalse(installed, "a refused payload must never install");
            Thread.Sleep(5);
            installed = BasisFarAvatarBuilder.TryInstall(remote);
        }

        Assert.IsFalse(installed);
        Assert.IsFalse(remote.HasFarLodPayload, "refused payload must be latched unusable so the tick stops asking");
    }

    [Test]
    public void PrewarmParse_RefusedPayload_TickInstallConsumesAndLatches()
    {
        BasisRemotePlayer remote = NewRemote();
        remote.FarLodOverridePayload = BasisFarLodTestPayloads.CreateRefusedBase64();
        remote.FarLodOverrideVersion = $"refused-prewarm-{Guid.NewGuid():N}";

        // CreateAvatar's role: warm the parse only, never install.
        BasisFarAvatarBuilder.PrewarmParse(remote);

        // The transmit tick's role: retry TryInstall until the parse resolves.
        Stopwatch deadline = Stopwatch.StartNew();
        bool installed = BasisFarAvatarBuilder.TryInstall(remote);
        while (remote.HasFarLodPayload && deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            Assert.IsFalse(installed, "a refused payload must never install");
            Thread.Sleep(5);
            installed = BasisFarAvatarBuilder.TryInstall(remote);
        }

        Assert.IsFalse(installed);
        Assert.IsFalse(remote.HasFarLodPayload, "refused payload must be latched unusable");
    }

    [Test]
    public void TryInstall_AlreadyWearingResolvedVersion_ShortCircuitsTrue()
    {
        string version = $"worn-{Guid.NewGuid():N}";
        GameObject avatarObject = new GameObject("FarAvatarWornTest");
        try
        {
            BasisAvatar avatar = avatarObject.AddComponent<BasisAvatar>();
            avatar.IsFarLodAvatar = true;
            // Both, as BuildAvatar sets them: the component owns the release key, the avatar
            // carries the mirror the tick reads.
            avatar.FarLodSharedVersion = version;
            BasisFarAvatarInstance instance = avatarObject.AddComponent<BasisFarAvatarInstance>();
            instance.SharedVersion = version;

            BasisRemotePlayer remote = NewRemote();
            remote.BasisAvatar = avatar;
            remote.FarLodOverridePayload = "non-empty-but-never-parsed";
            remote.FarLodOverrideVersion = version;

            Assert.IsTrue(BasisFarAvatarBuilder.TryInstall(remote), "wearing the resolved version is already success");
            Assert.IsTrue(BasisFarAvatarBuilder.IsWearingResolvedVersion(remote), "tick sees the worn far avatar as the desired one");

            remote.FarLodOverrideVersion = $"changed-{Guid.NewGuid():N}";
            Assert.IsFalse(BasisFarAvatarBuilder.IsWearingResolvedVersion(remote), "an avatar-record change makes the worn far avatar stale — the tick then swaps it");
            remote.FarLodOverrideVersion = version;

            Assert.IsTrue(remote.HasFarLodPayload, "short-circuit must not touch payload state");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(avatarObject);
        }
    }

    [Test]
    public void WornFarVersion_ReadsVersionOnlyForFarAvatars()
    {
        GameObject avatarObject = new GameObject("FarAvatarVersionTest");
        try
        {
            BasisAvatar avatar = avatarObject.AddComponent<BasisAvatar>();
            avatar.FarLodSharedVersion = "some-version";
            BasisFarAvatarInstance instance = avatarObject.AddComponent<BasisFarAvatarInstance>();
            instance.SharedVersion = "some-version";

            BasisRemotePlayer remote = NewRemote();
            remote.BasisAvatar = avatar;

            // IsFarLodAvatar still gates the read, so a real avatar that somehow carried a
            // stale version string is never reported as wearing a far LOD.
            Assert.IsNull(BasisFarAvatarBuilder.WornFarVersion(remote), "a real avatar is never reported as a worn far version");

            avatar.IsFarLodAvatar = true;
            Assert.AreEqual("some-version", BasisFarAvatarBuilder.WornFarVersion(remote));

            Assert.IsNull(BasisFarAvatarBuilder.WornFarVersion(null));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(avatarObject);
        }
    }
}
