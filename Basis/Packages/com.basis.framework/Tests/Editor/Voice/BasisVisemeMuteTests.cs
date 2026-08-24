using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using NUnit.Framework;
using OpenLipSync.Inference;
using OpenLipSync.Inference.OVRCompat;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class BasisVisemeMuteTests
{
    private const int VisemeCount = BasisVisemeDriveConfig.VisemeCount;
    private const float Frame = 1f / 90f;
    private const int Viseme = 10;

    private readonly List<GameObject> _spawned = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        DrainBatchPipeline();
        BasisOpenLipSyncDriver.ProcessFrameOverride = null;
        for (int Index = 0; Index < _spawned.Count; Index++)
        {
            if (_spawned[Index] != null)
            {
                Object.DestroyImmediate(_spawned[Index]);
            }
        }
        _spawned.Clear();
    }

    private static void DrainBatchPipeline()
    {
        BasisOpenLipSyncDriver.ProcessFrameOverride = (handle, audio, count, frame) => Result.Success;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            BasisOpenLipSyncContext.ProcessAllPending();
            if (!BasisOpenLipSyncContext.DebugBatchRunning && BasisOpenLipSyncContext.DebugPendingCount == 0)
            {
                return;
            }
            Thread.Sleep(2);
        }
    }

    private static void RunInference()
    {
        for (int attempt = 0; attempt < 2500; attempt++)
        {
            BasisOpenLipSyncContext.ProcessAllPending();
            if (!BasisOpenLipSyncContext.DebugBatchRunning && BasisOpenLipSyncContext.DebugPendingCount == 0)
            {
                return;
            }
            Thread.Sleep(2);
        }
        Assert.Fail("Batch inference did not complete within 5s.");
    }

    private BasisAvatar BuildAvatar()
    {
        GameObject root = new GameObject("VisemeMuteTestAvatar");
        _spawned.Add(root);

        Mesh mesh = new Mesh();
        mesh.vertices = new Vector3[] { Vector3.zero, Vector3.right, Vector3.up };
        mesh.triangles = new int[] { 0, 1, 2 };
        Vector3[] delta = new Vector3[] { Vector3.up, Vector3.up, Vector3.up };
        for (int Index = 0; Index < VisemeCount; Index++)
        {
            mesh.AddBlendShapeFrame($"viseme{Index}", 100f, delta, null, null);
        }

        SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = mesh;

        BasisAvatar avatar = root.AddComponent<BasisAvatar>();
        avatar.FaceVisemeMesh = renderer;
        avatar.FaceVisemeMovement = new int[VisemeCount];
        for (int Index = 0; Index < VisemeCount; Index++)
        {
            avatar.FaceVisemeMovement[Index] = Index;
        }
        return avatar;
    }

    private BasisRemotePlayer BuildPlayer(BasisAvatar avatar)
    {
        GameObject mouth = new GameObject("Mouth");
        _spawned.Add(mouth);
        return new BasisRemotePlayer
        {
            DisplayName = "viseme-mute-test",
            UUID = System.Guid.NewGuid().ToString("N"),
            MouthTransform = mouth.transform,
            BasisAvatar = avatar,
            FaceIsVisible = true,
        };
    }

    private static BasisVisemeProfile[] DefaultProfiles()
    {
        BasisVisemeProfile[] profiles = new BasisVisemeProfile[VisemeCount];
        for (int Index = 0; Index < VisemeCount; Index++)
        {
            profiles[Index] = BasisVisemeProfile.Default;
        }
        return profiles;
    }

    private static BasisOpenLipSyncContext Bind(BasisAvatar avatar)
    {
        BasisOpenLipSyncContext context = new BasisOpenLipSyncContext();
        context.Initialize(avatar, 0);
        return context;
    }

    private static float Weight(BasisAvatar avatar, int viseme)
    {
        return avatar.FaceVisemeMesh.GetBlendShapeWeight(avatar.FaceVisemeMovement[viseme]);
    }

    private static BasisOpenLipSyncContext OpenMouth(BasisAvatar avatar)
    {
        BasisOpenLipSyncContext context = Bind(avatar);
        context.RawVisemeWeights[Viseme] = 0.9f;
        context.Apply(Frame);
        Assert.AreEqual(90f, Weight(avatar, Viseme), 0.3f, "fixture: the mouth opens before the mute");
        return context;
    }

    private static int CloseMouth(BasisOpenLipSyncContext context, BasisAvatar avatar)
    {
        int frames = 0;
        while (Weight(avatar, Viseme) > 0f)
        {
            Assert.Less(frames, 200, "the mouth never closed");
            context.Apply(Frame);
            frames++;
        }
        return frames;
    }

    [Test]
    public void MuteRampsTheMouthClosedInsteadOfSnapping()
    {
        BasisAvatar avatar = BuildAvatar();
        BasisOpenLipSyncContext context = OpenMouth(avatar);

        context.SetMuted(true);
        float previous = 90f;
        int frames = 0;
        while (previous > 0f)
        {
            Assert.Less(frames, 60, "the mouth never closed");
            context.Apply(Frame);
            frames++;
            float weight = Weight(avatar, Viseme);
            Assert.LessOrEqual(weight, previous, "the release must be monotonic");
            if (frames == 1)
            {
                Assert.Less(weight, 90f, "the first muted frame must start closing");
                Assert.Greater(weight, 60f, "the first muted frame must not snap shut");
            }
            previous = weight;
        }

        Assert.Greater(frames, 3, "closing takes several frames, not one");
        Assert.LessOrEqual(frames, Mathf.CeilToInt(BasisOpenLipSyncContext.MuteReleaseSeconds / Frame) + 1, "a fully open mouth closes within MuteReleaseSeconds");
        Assert.AreEqual(0f, context.LastApplied[Viseme], "the rest write is exact, not an epsilon residue");
    }

    [Test]
    public void UnmuteResumesLipSync()
    {
        BasisAvatar avatar = BuildAvatar();
        BasisOpenLipSyncContext context = OpenMouth(avatar);

        context.SetMuted(true);
        CloseMouth(context, avatar);
        context.Apply(Frame);
        Assert.AreEqual(0f, Weight(avatar, Viseme), 0.3f, "a released mouth stays shut while muted");

        context.SetMuted(false);
        context.RawVisemeWeights[Viseme] = 0.7f;
        context.Apply(Frame);
        Assert.AreEqual(70f, Weight(avatar, Viseme), 0.3f);
    }

    [Test]
    public void ResultsPublishedWhileMutedAreDiscarded()
    {
        BasisAvatar avatar = BuildAvatar();
        BasisOpenLipSyncContext context = Bind(avatar);
        BasisOpenLipSyncDriver.ProcessFrameOverride = (handle, audio, count, frame) =>
        {
            frame.Visemes[Viseme] = 1f;
            return Result.Success;
        };

        context.ProcessAudioSamples(new float[480], 1, 480);
        context.Simulate(Frame);
        RunInference();
        context.Apply(Frame);
        Assert.AreEqual(100f, Weight(avatar, Viseme), 0.3f, "fixture: the batch path is live");

        context.SetMuted(true);
        CloseMouth(context, avatar);
        context.SetMuted(false);

        context.ProcessAudioSamples(new float[480], 1, 480);
        context.Simulate(Frame);
        RunInference();
        context.SetMuted(true);
        context.Apply(Frame);
        Assert.AreEqual(0f, Weight(avatar, Viseme), 0.3f, "inference that lands after the mute must not reopen the mouth");

        context.SetMuted(false);
        context.Apply(Frame);
        Assert.AreEqual(0f, Weight(avatar, Viseme), 0.3f, "nor be replayed on unmute");
    }

    [Test]
    public void AudioBufferedBeforeTheMuteIsDropped()
    {
        BasisAvatar avatar = BuildAvatar();
        BasisOpenLipSyncContext context = Bind(avatar);
        BasisOpenLipSyncDriver.ProcessFrameOverride = (handle, audio, count, frame) =>
        {
            frame.Visemes[Viseme] = 1f;
            return Result.Success;
        };

        context.ProcessAudioSamples(new float[480], 1, 480);
        Assert.AreEqual(480, context.DebugWriteIndexA + context.DebugWriteIndexB);

        context.SetMuted(true);
        context.Simulate(Frame);
        Assert.AreEqual(0, context.DebugWriteIndexA + context.DebugWriteIndexB, "a muted Simulate drops what the mic buffered");
        Assert.AreEqual(0, BasisOpenLipSyncContext.DebugPendingCount, "and queues nothing for inference");

        context.SetMuted(false);
        context.Simulate(Frame);
        RunInference();
        context.Apply(Frame);
        Assert.AreEqual(0f, Weight(avatar, Viseme), 0.3f, "pre-mute speech is not inferred on unmute");
    }

    [Test]
    public void DriverIgnoresMicrophoneAudioWhileMuted()
    {
        BasisAvatar avatar = BuildAvatar();
        BasisAudioAndVisemeDriver driver = new BasisAudioAndVisemeDriver();
        Assert.IsTrue(driver.TryInitialize(BuildPlayer(avatar)));
        BasisOpenLipSyncContext planted = Bind(avatar);
        driver.openLipSyncContext = planted;
        driver.UseOpenLipSync = true;

        driver.OnPausedEvent(true);
        driver.ProcessAudioSamples(new float[480], 1, 480);
        Assert.AreEqual(0, planted.DebugWriteIndexA + planted.DebugWriteIndexB, "a muted driver feeds the context nothing");

        driver.OnPausedEvent(false);
        driver.ProcessAudioSamples(new float[480], 1, 480);
        Assert.AreEqual(480, planted.DebugWriteIndexA + planted.DebugWriteIndexB, "unmuting restores the feed");

        driver.OnDestroy();
    }

    [Test]
    public void MuteHonoursAnAuthoredRelease()
    {
        BasisAvatar avatar = BuildAvatar();
        BasisVisemeProfile[] profiles = DefaultProfiles();
        for (int Index = 0; Index < VisemeCount; Index++)
        {
            profiles[Index].ReleaseSeconds = 1f;
        }
        avatar.FaceVisemeProfiles = profiles;
        BasisOpenLipSyncContext context = OpenMouth(avatar);

        context.SetMuted(true);
        for (int Index = 0; Index < 18; Index++)
        {
            context.Apply(Frame);
        }
        Assert.Greater(Weight(avatar, Viseme), 50f, "a 1 s authored release is not cut short by the mute ramp");

        for (int Index = 0; Index < 100; Index++)
        {
            context.Apply(Frame);
        }
        Assert.AreEqual(0f, Weight(avatar, Viseme), 0.3f);
        Assert.AreEqual(0f, context.LastApplied[Viseme]);
    }

    [Test]
    public void WinnerTakeAllReleasesOnMute()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeProfiles = DefaultProfiles();
        avatar.FaceVisemeDrive.Mode = BasisVisemeDriveMode.WinnerTakeAll;
        avatar.FaceVisemeDrive.WinnerHoldSeconds = 5f;
        BasisOpenLipSyncContext context = Bind(avatar);
        context.RawVisemeWeights[Viseme] = 0.6f;
        context.Apply(Frame);
        Assert.AreEqual(100f, Weight(avatar, Viseme), 0.3f);

        context.SetMuted(true);
        for (int Index = 0; Index < 12; Index++)
        {
            context.Apply(Frame);
        }
        Assert.AreEqual(0f, Weight(avatar, Viseme), 0.3f, "the held winner is released by the mute");
    }
}
