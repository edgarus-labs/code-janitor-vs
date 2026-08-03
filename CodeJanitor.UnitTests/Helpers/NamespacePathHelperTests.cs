using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Helpers;

namespace CodeJanitor.UnitTests.Helpers
{
    /// <summary>
    /// Unit tests for <see cref="NamespacePathHelper" />.
    /// </summary>
    [TestClass]
    public class NamespacePathHelperTests
    {
        [TestMethod]
        [TestCategory("Helpers UnitTests")]
        public void BuildExpectedNamespace_UsesRootNamespaceAndRelativeFolders()
        {
            var result = NamespacePathHelper.BuildExpectedNamespace(
                "CodeJanitor",
                @"C:\Src\CodeJanitor",
                @"C:\Src\CodeJanitor\Features\Cleanup\Foo.cs");

            Assert.AreEqual("CodeJanitor.Features.Cleanup", result);
        }

        [TestMethod]
        [TestCategory("Helpers UnitTests")]
        public void BuildExpectedNamespace_UsesProjectRootWhenFileIsAtProjectRoot()
        {
            var result = NamespacePathHelper.BuildExpectedNamespace(
                "CodeJanitor",
                @"C:\Src\CodeJanitor",
                @"C:\Src\CodeJanitor\Foo.cs");

            Assert.AreEqual("CodeJanitor", result);
        }

        [TestMethod]
        [TestCategory("Helpers UnitTests")]
        public void BuildExpectedNamespace_SanitizesFolderNames()
        {
            var result = NamespacePathHelper.BuildExpectedNamespace(
                "CodeJanitor",
                @"C:\Src\CodeJanitor",
                @"C:\Src\CodeJanitor\My Features\123Cleanup\Foo.cs");

            Assert.AreEqual("CodeJanitor.My_Features._123Cleanup", result);
        }
    }
}