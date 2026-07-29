using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteveCadwallader.CodeJanitor.Logic.Transformations;

namespace SteveCadwallader.CodeJanitor.UnitTests.Transformations
{
    /// <summary>
    /// Unit tests for <see cref="FileScopedNamespaceConverter" />.
    /// Pure transformation tests (no Visual Studio / EnvDTE required).
    /// </summary>
    [TestClass]
    public class FileScopedNamespaceConverterTests
    {
        private INamespaceScopeConverter _converter;

        [TestInitialize]
        public void TestInitialize()
        {
            _converter = new FileScopedNamespaceConverter();
        }

        [TestMethod]
        [TestCategory("Transformations UnitTests")]
        public void ConvertsSingleBlockNamespaceToFileScoped()
        {
            var input = "namespace A\r\n{\r\n    class C\r\n    {\r\n    }\r\n}\r\n";
            var expected = "namespace A;\r\n\r\nclass C\r\n{\r\n}\r\n";

            Assert.AreEqual(expected, _converter.ConvertToFileScoped(input));
        }

        [TestMethod]
        [TestCategory("Transformations UnitTests")]
        public void PreservesUsingsInsideNamespaceAndDedents()
        {
            var input = "namespace A\r\n{\r\n    using System;\r\n\r\n    class C\r\n    {\r\n    }\r\n}\r\n";
            var expected = "namespace A;\r\n\r\nusing System;\r\n\r\nclass C\r\n{\r\n}\r\n";

            Assert.AreEqual(expected, _converter.ConvertToFileScoped(input));
        }

        [TestMethod]
        [TestCategory("Transformations UnitTests")]
        public void AlreadyFileScoped_ReturnsUnchanged()
        {
            var input = "namespace A;\r\n\r\nclass C\r\n{\r\n}\r\n";

            Assert.AreEqual(input, _converter.ConvertToFileScoped(input));
        }

        [TestMethod]
        [TestCategory("Transformations UnitTests")]
        public void MultipleNamespaces_ReturnsUnchanged()
        {
            var input = "namespace A\r\n{\r\n}\r\nnamespace B\r\n{\r\n}\r\n";

            Assert.AreEqual(input, _converter.ConvertToFileScoped(input));
        }

        [TestMethod]
        [TestCategory("Transformations UnitTests")]
        public void NoNamespace_ReturnsUnchanged()
        {
            var input = "class C\r\n{\r\n}\r\n";

            Assert.AreEqual(input, _converter.ConvertToFileScoped(input));
        }

        [TestMethod]
        [TestCategory("Transformations UnitTests")]
        public void NestedNamespace_ReturnsUnchanged()
        {
            var input = "namespace A\r\n{\r\n    namespace B\r\n    {\r\n    }\r\n}\r\n";

            Assert.AreEqual(input, _converter.ConvertToFileScoped(input));
        }

        [TestMethod]
        [TestCategory("Transformations UnitTests")]
        public void PreservesFileHeaderAndOuterUsings()
        {
            var input = "// file header\r\nusing System;\r\n\r\nnamespace A\r\n{\r\n    class C\r\n    {\r\n    }\r\n}\r\n";
            var expected = "// file header\r\nusing System;\r\n\r\nnamespace A;\r\n\r\nclass C\r\n{\r\n}\r\n";

            Assert.AreEqual(expected, _converter.ConvertToFileScoped(input));
        }
    }
}
