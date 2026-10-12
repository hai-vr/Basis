using NUnit.Framework;
using static SerializableBasis;

public class BasisContentShareSettingTests
{
    [SetUp]
    public void SetUp()
    {
        BasisContentShareSetting.Register();
    }

    private static ContentShareSetting.Contents Location() =>
        new ContentShareSetting.Contents { Tab = "settings.tab.graphics", Key = "shadowquality", Title = "Shadow Quality", Path = "Graphics › Lighting" };

    private static ContentShareSetting.Contents Value(string type, string value, string text)
    {
        ContentShareSetting.Contents setting = Location();
        setting.ValueType = type;
        setting.Value = value;
        setting.ValueText = text;
        return setting;
    }

    [Test]
    public void SettingIsRegisteredAsAnInlinePayload()
    {
        Assert.IsTrue(BasisContentSharePayloadRegistry.TryGet(ContentShareType.Setting, out BasisContentSharePayloadKind kind));
        Assert.AreEqual(BasisShareableKind.Setting, kind.ShareableKind);
        Assert.AreEqual("content.share.type.setting", kind.NameKey);
        Assert.IsNull(kind.Accept);
        Assert.IsTrue(ContentSharePayload.IsPayloadType(ContentShareType.Setting));
        Assert.IsTrue(BasisContentSharePayloadRegistry.IsHandled(ContentShareType.Setting));
    }

    [Test]
    public void TheOrbNamesTheSetting_AndAValueShareAddsTheValueAsShown()
    {
        Assert.AreEqual("Shadow Quality", BasisContentSharePayloadRegistry.Describe(ContentShareType.Setting, ContentShareSetting.Write(Location())));
        Assert.AreEqual("Shadow Quality: High", BasisContentSharePayloadRegistry.Describe(ContentShareType.Setting, ContentShareSetting.Write(Value(ContentShareSetting.TextType, "high", "High"))));
        Assert.AreEqual("Shadow Quality", BasisContentSharePayloadRegistry.Describe(ContentShareType.Setting, ContentShareSetting.Write(Value(ContentShareSetting.TextType, "high", string.Empty))));
    }

    [Test]
    public void AShareWithNoTitleIsNamedByItsKey()
    {
        ContentShareSetting.Contents untitled = Location();
        untitled.Title = "​\t";
        Assert.IsTrue(ContentShareSetting.TryRead(ContentShareSetting.Write(untitled), out ContentShareSetting.Contents read));
        Assert.AreEqual("shadowquality", BasisContentShareSetting.DisplayTitle(read));
        Assert.AreEqual("shadowquality", BasisContentSharePayloadRegistry.Describe(ContentShareType.Setting, ContentShareSetting.Write(untitled)));
    }

    [Test]
    public void AnUnreadablePayloadDescribesNothing()
    {
        Assert.IsNull(BasisContentSharePayloadRegistry.Describe(ContentShareType.Setting, ContentShareSetting.Header + "\nnot a setting"));
        Assert.IsNull(BasisContentSharePayloadRegistry.Describe(ContentShareType.Setting, "https://example.com"));
        Assert.IsNull(BasisContentSharePayloadRegistry.Describe(ContentShareType.Setting, null));
    }

    [Test]
    public void NothingIsCapturedOrMatchedWithoutAControl()
    {
        Assert.IsFalse(BasisContentShareSetting.TryCapture(null, out ContentShareSetting.Contents location, out ContentShareSetting.Contents value));
        Assert.IsNull(location);
        Assert.IsNull(value);
        Assert.IsFalse(BasisContentShareSetting.Matches(null, Location()));
    }
}
