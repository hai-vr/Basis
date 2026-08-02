using Basis.Scripts.BasisSdk;
using Basis.Scripts.Drivers;
using NUnit.Framework;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;

public class BasisVisemeResponseTests
{
    private const int VisemeCount = BasisVisemeDriveConfig.VisemeCount;
    private const float Frame = 1f / 90f;

    private readonly List<GameObject> _spawned = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        for (int Index = 0; Index < _spawned.Count; Index++)
        {
            if (_spawned[Index] != null)
            {
                Object.DestroyImmediate(_spawned[Index]);
            }
        }
        _spawned.Clear();
    }

    private BasisAvatar BuildAvatar()
    {
        GameObject root = new GameObject("VisemeTestAvatar");
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

    [Test]
    public void UnauthoredAvatarKeepsProbabilityPassthrough()
    {
        BasisAvatar avatar = BuildAvatar();
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[10] = 0.42f;
        context.RawVisemeWeights[11] = 0.20f;
        context.Apply(Frame);

        Assert.AreEqual(42f, Weight(avatar, 10), 0.3f);
        Assert.AreEqual(20f, Weight(avatar, 11), 0.3f);
    }

    [Test]
    public void AllDefaultProfilesStayOnPassthrough()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeProfiles = DefaultProfiles();
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[7] = 0.55f;
        context.Apply(Frame);

        Assert.AreEqual(55f, Weight(avatar, 7), 0.3f);
    }

    [Test]
    public void WinnerTakeAllShowsOnlyTheStrongestVisemeAtFullWeight()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeProfiles = DefaultProfiles();
        avatar.FaceVisemeDrive.Mode = BasisVisemeDriveMode.WinnerTakeAll;
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[10] = 0.62f;
        context.RawVisemeWeights[12] = 0.31f;
        context.Apply(Frame);

        Assert.AreEqual(100f, Weight(avatar, 10), 0.3f);
        Assert.AreEqual(0f, Weight(avatar, 12), 0.3f);
    }

    [Test]
    public void WinnerTakeAllHoldsThroughANearTie()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeProfiles = DefaultProfiles();
        avatar.FaceVisemeDrive.Mode = BasisVisemeDriveMode.WinnerTakeAll;
        avatar.FaceVisemeDrive.WinnerMargin = 0.05f;
        avatar.FaceVisemeDrive.WinnerHoldSeconds = 0f;
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[10] = 0.60f;
        context.Apply(Frame);
        Assert.AreEqual(100f, Weight(avatar, 10), 0.3f);

        context.RawVisemeWeights[10] = 0.58f;
        context.RawVisemeWeights[12] = 0.60f;
        context.Apply(Frame);

        Assert.AreEqual(100f, Weight(avatar, 10), 0.3f, "a challenger inside the margin must not steal the mouth");
        Assert.AreEqual(0f, Weight(avatar, 12), 0.3f);

        context.RawVisemeWeights[12] = 0.90f;
        context.Apply(Frame);

        Assert.AreEqual(0f, Weight(avatar, 10), 0.3f);
        Assert.AreEqual(100f, Weight(avatar, 12), 0.3f);
    }

    [Test]
    public void WinnerTakeAllRespectsTheMinimumHold()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeProfiles = DefaultProfiles();
        avatar.FaceVisemeDrive.Mode = BasisVisemeDriveMode.WinnerTakeAll;
        avatar.FaceVisemeDrive.WinnerMargin = 0f;
        avatar.FaceVisemeDrive.WinnerHoldSeconds = 0.06f;
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[10] = 0.60f;
        context.Apply(Frame);

        context.RawVisemeWeights[10] = 0.10f;
        context.RawVisemeWeights[12] = 0.95f;
        context.Apply(Frame);
        Assert.AreEqual(100f, Weight(avatar, 10), 0.3f, "the hold window must survive a single frame");

        for (int Index = 0; Index < 8; Index++)
        {
            context.Apply(Frame);
        }
        Assert.AreEqual(100f, Weight(avatar, 12), 0.3f, "the switch must land once the hold elapses");
    }

    [Test]
    public void WinnerTakeAllRestsBelowTheSilenceFloor()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeProfiles = DefaultProfiles();
        avatar.FaceVisemeDrive.Mode = BasisVisemeDriveMode.WinnerTakeAll;
        avatar.FaceVisemeDrive.SilenceFloor = 0.15f;
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[10] = 0.60f;
        context.Apply(Frame);
        Assert.AreEqual(100f, Weight(avatar, 10), 0.3f);

        context.RawVisemeWeights[10] = 0.05f;
        context.Apply(Frame);
        Assert.AreEqual(0f, Weight(avatar, 10), 0.3f);
    }

    [Test]
    public void SilWinningRestsTheMouthWhenSilIsRest()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeProfiles = DefaultProfiles();
        avatar.FaceVisemeDrive.Mode = BasisVisemeDriveMode.WinnerTakeAll;
        avatar.FaceVisemeDrive.SilIsRest = true;
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[BasisVisemeDriveConfig.SilVisemeIndex] = 0.95f;
        context.Apply(Frame);

        Assert.AreEqual(0f, Weight(avatar, BasisVisemeDriveConfig.SilVisemeIndex), 0.3f);
    }

    [Test]
    public void GainThresholdAndOutputRangeRemapTheResponse()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeProfiles = DefaultProfiles();
        avatar.FaceVisemeProfiles[5].Gain = 2f;
        avatar.FaceVisemeProfiles[5].Threshold = 0.5f;
        avatar.FaceVisemeProfiles[5].OutMax = 80f;
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[5] = 0.2f;
        context.Apply(Frame);
        Assert.AreEqual(0f, Weight(avatar, 5), 0.3f, "post-gain 0.4 sits under the 0.5 threshold");

        context.RawVisemeWeights[5] = 0.5f;
        context.Apply(Frame);
        Assert.AreEqual(80f, Weight(avatar, 5), 0.3f, "post-gain 1.0 is the top of the range");

        context.RawVisemeWeights[5] = 0.375f;
        context.Apply(Frame);
        Assert.AreEqual(40f, Weight(avatar, 5), 0.5f, "post-gain 0.75 is halfway above the threshold");
    }

    [Test]
    public void BinaryProfileSnapsInsteadOfBlending()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeProfiles = DefaultProfiles();
        avatar.FaceVisemeProfiles[6].Binary = true;
        avatar.FaceVisemeProfiles[6].Threshold = 0.4f;
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[6] = 0.45f;
        context.Apply(Frame);
        Assert.AreEqual(100f, Weight(avatar, 6), 0.3f);

        context.RawVisemeWeights[6] = 0.35f;
        context.Apply(Frame);
        Assert.AreEqual(0f, Weight(avatar, 6), 0.3f);
    }

    [Test]
    public void AttackRampsRatherThanJumping()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeProfiles = DefaultProfiles();
        avatar.FaceVisemeProfiles[4].AttackSeconds = 0.1f;
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[4] = 1f;
        context.Apply(0.05f);
        Assert.AreEqual(50f, Weight(avatar, 4), 1f, "half the attack time is half the travel");

        context.Apply(0.05f);
        Assert.AreEqual(100f, Weight(avatar, 4), 0.3f);
    }

    [Test]
    public void SlowReleaseStillConvergesBelowTheWriteThreshold()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeProfiles = DefaultProfiles();
        avatar.FaceVisemeProfiles[3].ReleaseSeconds = 10f;
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[3] = 1f;
        context.Apply(1f);
        Assert.AreEqual(100f, Weight(avatar, 3), 0.3f);

        context.RawVisemeWeights[3] = 0f;
        for (int Index = 0; Index < 90; Index++)
        {
            context.Apply(Frame);
        }

        float afterOneSecond = Weight(avatar, 3);
        Assert.Less(afterOneSecond, 95f, "a 10s release must still be moving at per-frame steps under the write epsilon");
        Assert.Greater(afterOneSecond, 85f);
    }

    [Test]
    public void ZeroVisemesReleasesTheWinnerLatch()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeProfiles = DefaultProfiles();
        avatar.FaceVisemeDrive.Mode = BasisVisemeDriveMode.WinnerTakeAll;
        avatar.FaceVisemeDrive.WinnerHoldSeconds = 5f;
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[10] = 0.60f;
        context.Apply(Frame);
        Assert.AreEqual(100f, Weight(avatar, 10), 0.3f);

        context.ZeroVisemes();
        Assert.AreEqual(0f, Weight(avatar, 10), 0.3f);

        context.RawVisemeWeights[10] = 0f;
        context.RawVisemeWeights[12] = 0.80f;
        context.Apply(Frame);
        Assert.AreEqual(100f, Weight(avatar, 12), 0.3f, "a cleared latch must not be held by the old winner's dwell");
    }

    /// <summary>
    /// The shape an avatar takes when its payload predates response shaping: the table exists but
    /// nothing in it was ever authored. Baking it verbatim gives every viseme gain 0 into a range
    /// of zero width, which mutes the mouth on an avatar that never opted in.
    /// </summary>
    [Test]
    public void BlankProfileTableDoesNotMuteTheMouth()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeProfiles = new BasisVisemeProfile[VisemeCount];
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[10] = 0.42f;
        context.Apply(Frame);

        Assert.AreEqual(42f, Weight(avatar, 10), 0.3f);
    }

    /// <summary>
    /// One authored viseme must not drag the blank slots beside it to silence.
    /// </summary>
    [Test]
    public void BlankSlotsBesideAnAuthoredOneStayPassthrough()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeProfiles = new BasisVisemeProfile[VisemeCount];
        avatar.FaceVisemeProfiles[5] = BasisVisemeProfile.Default;
        avatar.FaceVisemeProfiles[5].OutMax = 60f;
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[5] = 1f;
        context.RawVisemeWeights[10] = 0.42f;
        context.Apply(Frame);

        Assert.AreEqual(60f, Weight(avatar, 5), 0.3f, "the authored slot keeps its ceiling");
        Assert.AreEqual(42f, Weight(avatar, 10), 0.3f, "a blank slot stays on passthrough");
    }

    /// <summary>
    /// Switching a viseme off is authored intent, not missing data. Rebuilding it on the default
    /// hands the shape back to the model, and since sil sits near 1.0 whenever nobody is talking,
    /// that parks the silenced shape wide open at rest.
    /// </summary>
    [Test]
    public void SilencedVisemeIsNotRebuiltOnTheDefault()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeProfiles = DefaultProfiles();
        avatar.FaceVisemeProfiles[0].OutMax = 0f;
        avatar.FaceVisemeProfiles[6].Gain = 0f;
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[0] = 0.98f;
        context.RawVisemeWeights[6] = 0.90f;
        context.RawVisemeWeights[10] = 0.42f;
        context.Apply(Frame);

        Assert.AreEqual(0f, Weight(avatar, 0), 0.3f, "a collapsed output range must stay at rest");
        Assert.AreEqual(0f, Weight(avatar, 6), 0.3f, "a zeroed gain must stay at rest");
        Assert.AreEqual(42f, Weight(avatar, 10), 0.3f, "its neighbours keep responding");
    }

    /// <summary>
    /// A zeroed config is what a deserializer hands back for a payload built before it existed.
    /// Every field of it is a legal authored value, so it has to be recognised as absent rather
    /// than honoured — otherwise the avatar silently asks for no smoothing and no silence floor.
    /// </summary>
    [Test]
    public void ZeroedDriveConfigIsTreatedAsUnauthored()
    {
        BasisVisemeDriveConfig zeroed = new BasisVisemeDriveConfig
        {
            Mode = BasisVisemeDriveMode.Continuous,
            WinnerMargin = 0f,
            WinnerHoldSeconds = 0f,
            SilenceFloor = 0f,
            SilIsRest = false,
            BackendSmoothing = 0,
        };

        Assert.IsTrue(zeroed.IsUnset);
        Assert.IsFalse(new BasisVisemeDriveConfig().IsUnset, "a constructed config is authored, not absent");

        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeDrive = zeroed;
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[7] = 0.55f;
        context.Apply(Frame);

        Assert.AreEqual(55f, Weight(avatar, 7), 0.3f);
    }

    /// <summary>
    /// FaceVisemeMovement is authored against the SDK-time mesh. One that arrives with fewer
    /// shapes leaves indices pointing past the end, and SetBlendShapeWeight throws on every one
    /// of them, every frame.
    /// </summary>
    [Test]
    public void VisemesMappedPastTheMeshAreDropped()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeMovement[7] = VisemeCount + 84;
        LogAssert.Expect(LogType.Warning, new Regex("map past the face mesh"));
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[7] = 0.90f;
        context.RawVisemeWeights[10] = 0.42f;
        Assert.DoesNotThrow(() => context.Apply(Frame));

        Assert.AreEqual(42f, Weight(avatar, 10), 0.3f, "the mappings that do resolve keep working");
    }

    /// <summary>
    /// A renderer outliving its sharedMesh mid-swap reports blendShapeCount 0, and every write
    /// into it throws "index out of bounds (size=0)" for as long as the window lasts.
    /// </summary>
    [Test]
    public void MeshLosingItsShapesDoesNotThrow()
    {
        BasisAvatar avatar = BuildAvatar();
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[7] = 0.90f;
        context.Apply(Frame);

        avatar.FaceVisemeMesh.sharedMesh = null;
        context.RawVisemeWeights[7] = 0.20f;
        Assert.DoesNotThrow(() => context.Apply(Frame));
    }

    [Test]
    public void UnmappedVisemesAreLeftAlone()
    {
        BasisAvatar avatar = BuildAvatar();
        avatar.FaceVisemeProfiles = DefaultProfiles();
        avatar.FaceVisemeDrive.Mode = BasisVisemeDriveMode.WinnerTakeAll;
        avatar.FaceVisemeMovement[12] = -1;
        avatar.FaceVisemeMesh.SetBlendShapeWeight(12, 37f);
        BasisOpenLipSyncContext context = Bind(avatar);

        context.RawVisemeWeights[12] = 0.99f;
        context.Apply(Frame);

        Assert.AreEqual(37f, avatar.FaceVisemeMesh.GetBlendShapeWeight(12), 0.3f);
    }
}
