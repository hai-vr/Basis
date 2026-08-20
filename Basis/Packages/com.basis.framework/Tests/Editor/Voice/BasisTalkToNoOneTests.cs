using Basis.BasisUI;
using Basis.Scripts.Networking;
using NUnit.Framework;

/// <summary>
/// Talk-to-no-one is a mic mode, not a mute: the microphone keeps running so local visemes still
/// animate, while nothing leaves the client. It is opt-in, so with the setting off it must not
/// appear anywhere in the mic-mode cycle.
/// </summary>
public class BasisTalkToNoOneTests
{
    private bool _previousSetting;

    [SetUp]
    public void SetUp()
    {
        _previousSetting = BasisSettingsDefaults.TalkToNoOne.RawValue;
    }

    [TearDown]
    public void TearDown()
    {
        BasisTalkModeManager.SetMode(BasisTalkMode.Normal);
        BasisSettingsDefaults.TalkToNoOne.SetValueWithoutNotify(_previousSetting);
    }

    [Test]
    public void ModeIsOfferedOnlyWhenOptedIn()
    {
        BasisSettingsDefaults.TalkToNoOne.SetValueWithoutNotify(false);
        Assert.IsFalse(BasisTalkModeManager.ModeAvailable(BasisTalkMode.NoOne));

        BasisSettingsDefaults.TalkToNoOne.SetValueWithoutNotify(true);
        Assert.IsTrue(BasisTalkModeManager.ModeAvailable(BasisTalkMode.NoOne));
    }

    [Test]
    public void CycleReachesNoOneWhenOptedIn()
    {
        BasisSettingsDefaults.TalkToNoOne.SetValueWithoutNotify(true);
        BasisTalkModeManager.SetMode(BasisTalkMode.Normal);

        for (int Step = 0; Step < 8 && BasisTalkModeManager.CurrentMode != BasisTalkMode.NoOne; Step++)
        {
            BasisTalkModeManager.CycleMode();
        }

        Assert.AreEqual(BasisTalkMode.NoOne, BasisTalkModeManager.CurrentMode);
    }

    [Test]
    public void CycleSkipsNoOneWhenNotOptedIn()
    {
        BasisSettingsDefaults.TalkToNoOne.SetValueWithoutNotify(false);
        BasisTalkModeManager.SetMode(BasisTalkMode.Normal);

        for (int Step = 0; Step < 8; Step++)
        {
            BasisTalkModeManager.CycleMode();
            Assert.AreNotEqual(BasisTalkMode.NoOne, BasisTalkModeManager.CurrentMode);
        }
    }

    [Test]
    public void TransmitIsBlockedOnlyInNoOne()
    {
        BasisSettingsDefaults.TalkToNoOne.SetValueWithoutNotify(true);

        BasisTalkModeManager.SetMode(BasisTalkMode.Normal);
        Assert.IsFalse(BasisTalkModeManager.TransmitBlockedLocally);

        BasisTalkModeManager.SetMode(BasisTalkMode.NoOne);
        Assert.IsTrue(BasisTalkModeManager.TransmitBlockedLocally);

        BasisTalkModeManager.SetMode(BasisTalkMode.Normal);
        Assert.IsFalse(BasisTalkModeManager.TransmitBlockedLocally);
    }

    [Test]
    public void NoOneIsNotARecipientOfItsOwnMode()
    {
        BasisSettingsDefaults.TalkToNoOne.SetValueWithoutNotify(true);
        BasisTalkModeManager.SetMode(BasisTalkMode.NoOne);

        Assert.IsFalse(BasisTalkModeManager.IsRecipient(1));
        Assert.IsFalse(BasisTalkModeManager.IsRecipient(2));
    }
}
