using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Helpers;

namespace CodeJanitor.UnitTests;

[TestClass]
public sealed class MemberTypeSettingTests
{
    [TestMethod]
    public void CanSerializeMemberTypeSetting()
    {
        MemberTypeSetting memberTypeSetting = new MemberTypeSetting("Fields", "Member Variables", 1);
        Assert.IsNotNull(memberTypeSetting);

        string serializedString = (string)memberTypeSetting;
        Assert.IsFalse(string.IsNullOrWhiteSpace(serializedString));
    }

    [TestMethod]
    public void CanDeserializeMemberTypeSetting()
    {
        const string SerializedString = @"Fields||1||Member Variables";

        MemberTypeSetting memberTypeSetting = (MemberTypeSetting)SerializedString;

        Assert.IsNotNull(memberTypeSetting);
        Assert.AreEqual("Fields", memberTypeSetting.DefaultName);
        Assert.AreEqual("Member Variables", memberTypeSetting.EffectiveName);
        Assert.AreEqual(1, memberTypeSetting.Order);
    }
}
