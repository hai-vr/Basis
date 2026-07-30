using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// State-machine behavior of the far avatar builder around the worker-thread parse: the
/// non-blocking tick path, the awaitable CreateAvatar path, refused-payload latching, and
/// the already-wearing short-circuits. Install success paths (calibration, bone jobs) need
/// a running player stack and stay out of edit mode.
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
    public void TryInstallAsync_WithoutAnyPayload_CompletesSynchronouslyFalse()
    {
        BasisRemotePlayer remote = NewRemote();
        Task<bool> install = BasisFarAvatarBuilder.TryInstallAsync(remote);
        Assert.IsTrue(install.IsCompleted, "no-payload path must not go async");
        Assert.IsFalse(install.Result);
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
    public async Task TryInstallAsync_RefusedPayload_AwaitsParseThenLatches()
    {
        BasisRemotePlayer remote = NewRemote();
        remote.FarLodOverridePayload = BasisFarLodTestPayloads.CreateRefusedBase64();
        remote.FarLodOverrideVersion = $"refused-async-{Guid.NewGuid():N}";

        bool installed = await BasisFarAvatarBuilder.TryInstallAsync(remote);

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
            BasisFarAvatarInstance instance = avatarObject.AddComponent<BasisFarAvatarInstance>();
            instance.SharedVersion = version;

            BasisRemotePlayer remote = NewRemote();
            remote.BasisAvatar = avatar;
            remote.FarLodOverridePayload = "non-empty-but-never-parsed";
            remote.FarLodOverrideVersion = version;

            Assert.IsTrue(BasisFarAvatarBuilder.TryInstall(remote), "wearing the resolved version is already success");

            Task<bool> install = BasisFarAvatarBuilder.TryInstallAsync(remote);
            Assert.IsTrue(install.IsCompleted, "already-wearing path must not go async");
            Assert.IsTrue(install.Result);

            Assert.IsTrue(remote.HasFarLodPayload, "short-circuit must not touch payload state");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(avatarObject);
        }
    }

    [Test]
    public void WornFarVersion_ReadsInstanceOnlyForFarAvatars()
    {
        GameObject avatarObject = new GameObject("FarAvatarVersionTest");
        try
        {
            BasisAvatar avatar = avatarObject.AddComponent<BasisAvatar>();
            BasisFarAvatarInstance instance = avatarObject.AddComponent<BasisFarAvatarInstance>();
            instance.SharedVersion = "some-version";

            BasisRemotePlayer remote = NewRemote();
            remote.BasisAvatar = avatar;

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
