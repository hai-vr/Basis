using Basis.Scripts.Networking;
using NUnit.Framework;
using System;
using System.Collections.Generic;

public class BasisModeratorGateTests
{
    private HashSet<string> _previous;

    [SetUp]
    public void SetUp()
    {
        _previous = BasisNetworkManagement.LocalPermissions;
    }

    [TearDown]
    public void TearDown()
    {
        BasisNetworkManagement.LocalPermissions = _previous;
    }

    private static void Grant(params string[] nodes)
    {
        BasisNetworkManagement.LocalPermissions = new HashSet<string>(nodes, StringComparer.OrdinalIgnoreCase);
    }

    [Test]
    public void SeededModeratorGroupWithoutTheUmbrellaNodeIsAModerator()
    {
        Grant("basis.moderation.kick", "basis.moderation.ban", "basis.moderation.mute", "basis.permissions.view");
        Assert.IsTrue(BasisNetworkModeration.LocalPlayerIsModerator());
    }

    [Test]
    public void UmbrellaNodeAloneIsAModerator()
    {
        Grant("basis.moderation");
        Assert.IsTrue(BasisNetworkModeration.LocalPlayerIsModerator());
    }

    [Test]
    public void WildcardAdminIsAModerator()
    {
        Grant("*");
        Assert.IsTrue(BasisNetworkModeration.LocalPlayerIsModerator());
    }

    [Test]
    public void ModerationWildcardNodeIsAModerator()
    {
        Grant("basis.moderation.*");
        Assert.IsTrue(BasisNetworkModeration.LocalPlayerIsModerator());
    }

    [Test]
    public void SingleActionNodeIsAModerator()
    {
        Grant("basis.moderation.message");
        Assert.IsTrue(BasisNetworkModeration.LocalPlayerIsModerator());
    }

    [Test]
    public void NodeCaseIsIgnored()
    {
        Grant("Basis.Moderation.Kick");
        Assert.IsTrue(BasisNetworkModeration.LocalPlayerIsModerator());
    }

    [Test]
    public void ViewOnlyIsNotAModerator()
    {
        Grant("basis.permissions.view", "basis.permissions.edit");
        Assert.IsFalse(BasisNetworkModeration.LocalPlayerIsModerator());
    }

    [Test]
    public void SimilarPrefixWithoutTheDotIsNotAModerator()
    {
        Grant("basis.moderationx.kick", "basis.moderatio");
        Assert.IsFalse(BasisNetworkModeration.LocalPlayerIsModerator());
    }

    [Test]
    public void EmptyOrMissingPermissionSetIsNotAModerator()
    {
        Grant();
        Assert.IsFalse(BasisNetworkModeration.LocalPlayerIsModerator());
        BasisNetworkManagement.LocalPermissions = null;
        Assert.IsFalse(BasisNetworkModeration.LocalPlayerIsModerator());
    }
}
