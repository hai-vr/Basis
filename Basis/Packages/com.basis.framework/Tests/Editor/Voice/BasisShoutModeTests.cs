using Basis.BasisUI;
using Basis.Scripts.Networking;
using NUnit.Framework;

/// <summary>
/// Shout is the proximity loud mode: twice the microphone range, a broadband boost, and still on
/// the ordinary spatialized voice path. It is the counterpart to Announce, which leaves the world
/// entirely and is heard by everyone at the same level — so the two must not be confused for each
/// other anywhere in the mode machinery, and shout must never take the announce permission path.
/// </summary>
public class BasisShoutModeTests
{
    private bool _previousShout;
    private bool _previousNoOne;

    [SetUp]
    public void SetUp()
    {
        _previousShout = BasisSettingsDefaults.ShoutMode.RawValue;
        _previousNoOne = BasisSettingsDefaults.TalkToNoOne.RawValue;
    }

    [TearDown]
    public void TearDown()
    {
        BasisTalkModeManager.SetMode(BasisTalkMode.Normal);
        BasisSettingsDefaults.ShoutMode.SetValueWithoutNotify(_previousShout);
        BasisSettingsDefaults.TalkToNoOne.SetValueWithoutNotify(_previousNoOne);
    }

    [Test]
    public void ShoutAndAnnounceAreDistinctWireValues()
    {
        Assert.AreNotEqual((byte)BasisTalkMode.Announce, (byte)BasisTalkMode.Shout);

        // Announce keeps the byte it shipped with; renaming it must not have renumbered the
        // enum, or every already-deployed client reads the wrong mode off the wire.
        Assert.AreEqual(4, (byte)BasisTalkMode.Announce);
        Assert.AreEqual(6, (byte)BasisTalkMode.Shout);
    }

    [Test]
    public void ShoutCarriesTwiceTheRangeAndMoreLevel()
    {
        Assert.AreEqual(2f, BasisShout.RangeMultiplier);
        Assert.AreEqual(BasisShout.RangeMultiplier * BasisShout.RangeMultiplier, BasisShout.RangeMultiplierSquared);
        Assert.Greater(BasisShout.Gain, 1f, "Shout is supposed to be louder, not just further.");
    }

    [Test]
    public void ModeIsOfferedOnlyWhenEnabled()
    {
        BasisSettingsDefaults.ShoutMode.SetValueWithoutNotify(false);
        Assert.IsFalse(BasisTalkModeManager.ModeAvailable(BasisTalkMode.Shout));

        BasisSettingsDefaults.ShoutMode.SetValueWithoutNotify(true);
        Assert.IsTrue(BasisTalkModeManager.ModeAvailable(BasisTalkMode.Shout));
    }

    [Test]
    public void CycleReachesShoutWhenEnabled()
    {
        BasisSettingsDefaults.ShoutMode.SetValueWithoutNotify(true);
        BasisTalkModeManager.SetMode(BasisTalkMode.Normal);

        for (int Step = 0; Step < 8 && BasisTalkModeManager.CurrentMode != BasisTalkMode.Shout; Step++)
        {
            BasisTalkModeManager.CycleMode();
        }

        Assert.AreEqual(BasisTalkMode.Shout, BasisTalkModeManager.CurrentMode);
    }

    [Test]
    public void CycleSkipsShoutWhenDisabled()
    {
        BasisSettingsDefaults.ShoutMode.SetValueWithoutNotify(false);
        BasisTalkModeManager.SetMode(BasisTalkMode.Normal);

        for (int Step = 0; Step < 8; Step++)
        {
            BasisTalkModeManager.CycleMode();
            Assert.AreNotEqual(BasisTalkMode.Shout, BasisTalkModeManager.CurrentMode);
        }
    }

    /// <summary>
    /// Announce needs a server round trip (an admin grants it), so SetMode only *requests* it and
    /// the mode does not change until the server says so. Shout is client-side and must apply
    /// immediately — a shout that waited on a permission reply would silently do nothing offline.
    /// </summary>
    [Test]
    public void ShoutAppliesLocallyWithoutAServerRoundTrip()
    {
        BasisSettingsDefaults.ShoutMode.SetValueWithoutNotify(true);
        BasisTalkModeManager.SetMode(BasisTalkMode.Shout);

        Assert.AreEqual(BasisTalkMode.Shout, BasisTalkModeManager.CurrentMode);
        Assert.IsTrue(BasisTalkModeManager.LocalIsShouting);
    }

    [Test]
    public void ShoutStillTransmitsAndReachesEveryoneInRange()
    {
        BasisSettingsDefaults.ShoutMode.SetValueWithoutNotify(true);
        BasisTalkModeManager.SetMode(BasisTalkMode.Shout);

        // Not a private mode: the recipient list comes from microphone range, not a member set,
        // so IsRecipient (which only answers for the allowlist modes) must stay false.
        Assert.IsFalse(BasisTalkModeManager.TransmitBlockedLocally);
        Assert.IsFalse(BasisTalkModeManager.IsRecipient(1));
    }

    /// <summary>
    /// Turning the setting off while shouting has to leave the mode; the pref handler does that
    /// live, but its subscription is installed from a RuntimeInitializeOnLoadMethod that EditMode
    /// tests cannot rely on having run. Drive the cycle instead — with the setting off, shout must
    /// not be somewhere the button can land or stay.
    /// </summary>
    [Test]
    public void CycleLeavesShoutOnceTheSettingIsOff()
    {
        BasisSettingsDefaults.ShoutMode.SetValueWithoutNotify(true);
        BasisTalkModeManager.SetMode(BasisTalkMode.Shout);
        Assert.AreEqual(BasisTalkMode.Shout, BasisTalkModeManager.CurrentMode);

        BasisSettingsDefaults.ShoutMode.SetValueWithoutNotify(false);
        BasisTalkModeManager.CycleMode();

        Assert.AreNotEqual(BasisTalkMode.Shout, BasisTalkModeManager.CurrentMode);
        Assert.IsFalse(BasisTalkModeManager.LocalIsShouting);
    }

    [Test]
    public void ShoutAloneIsEnoughToShowTheModeButton()
    {
        BasisSettingsDefaults.TalkToNoOne.SetValueWithoutNotify(false);
        BasisTalkModeManager.SetMode(BasisTalkMode.Normal);

        BasisSettingsDefaults.ShoutMode.SetValueWithoutNotify(false);
        bool withoutShout = BasisTalkModeManager.ShouldShowModeButton();

        BasisSettingsDefaults.ShoutMode.SetValueWithoutNotify(true);
        Assert.IsTrue(BasisTalkModeManager.ShouldShowModeButton());
        Assert.IsFalse(withoutShout, "Nothing else should have been offering the button in this state.");
    }
}
