using CodeJanitor.UI.Dialogs.About;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace CodeJanitor.UnitTests.UI;

[TestClass]
public sealed class AboutVersionTests
{
    private static readonly string InjectedVersion =
        FileVersionInfo.GetVersionInfo(typeof(AboutWindow).Assembly.Location).FileVersion;

    [TestMethod]
    public void Header_UsesBuildInjectedVersion()
    {
        Assert.AreEqual($"Version {InjectedVersion}", AboutVersion.Header);
    }

    [TestMethod]
    public void Footer_UsesBuildInjectedVersion()
    {
        Assert.AreEqual($"v{InjectedVersion}", AboutVersion.Footer);
    }
}
