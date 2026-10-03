using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests;

[TestClass]
public sealed class TestAssemblySetupTests
{
    [TestMethod]
    public void TryDeleteDirectory_DirectoryHeldByAnotherHandle_ReportsTheLeftoverInsteadOfThrowing()
    {
        string directory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", "CleanupFailure", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        StringWriter log = new StringWriter();
        FileStream held = new FileStream(Path.Combine(directory, "held.tmp"), FileMode.Create, FileAccess.Write, FileShare.None);
        try
        {
            Assert.IsFalse(TestAssemblySetup.TryDeleteDirectory(directory, log));
            Assert.Contains(directory, log.ToString());
        }
        finally
        {
            held.Dispose();
        }

        Assert.IsTrue(TestAssemblySetup.TryDeleteDirectory(directory, log));
        Assert.IsFalse(Directory.Exists(directory));
    }
}
