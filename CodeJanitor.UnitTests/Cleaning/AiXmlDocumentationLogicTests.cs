using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Reflection;

namespace CodeJanitor.UnitTests.Cleaning
{
    [TestClass]
    public class AiXmlDocumentationLogicTests
    {
        [TestMethod]
        public void GenerateXmlDocumentationForSource_InsertsSummaryParamReturnsAndException()
        {
            var source = @"
namespace Demo;

public class Sample
{
    public string BuildName(string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException(nameof(firstName));
        }

        return firstName + "" "" + lastName;
    }
}
";

            var updated = InvokeGenerateXmlDocumentation(source, _ => "Builds a combined display name.", 10);

            StringAssert.Contains(updated, "/// <summary>");
            StringAssert.Contains(updated, "/// Builds a combined display name.");
            StringAssert.Contains(updated, "<param name=\"firstName\">The first name.</param>");
            StringAssert.Contains(updated, "<param name=\"lastName\">The last name.</param>");
            StringAssert.Contains(updated, "<returns>A string value produced by this method.</returns>");
            StringAssert.Contains(updated, "<exception cref=\"ArgumentException\">");
        }

        [TestMethod]
        public void GenerateXmlDocumentationForSource_RespectsMethodLimit()
        {
            var source = @"
namespace Demo;

public class Sample
{
    public int First(int x)
    {
        return x + 1;
    }

    public int Second(int y)
    {
        return y + 2;
    }
}
";

            var updated = InvokeGenerateXmlDocumentation(source, m => "Summary for " + m.Identifier.ValueText + ".", 1);

            var summaryCount = CountOccurrences(updated, "/// <summary>");
            Assert.AreEqual(1, summaryCount, "Only one method should be documented when maxMethodsPerFile=1.");
            StringAssert.Contains(updated, "Summary for First.");
            Assert.IsFalse(updated.Contains("Summary for Second."));
        }

        [TestMethod]
        public void GenerateXmlDocumentationForSource_SkipsMethodsThatAlreadyHaveDocComments()
        {
            var source = @"
namespace Demo;

public class Sample
{
    /// <summary>
    /// Existing docs.
    /// </summary>
    public int Existing(int x)
    {
        return x;
    }

    public int Missing(int y)
    {
        return y;
    }
}
";

            var updated = InvokeGenerateXmlDocumentation(source, _ => "Generated docs.", 10);

            Assert.AreEqual(2, CountOccurrences(updated, "/// <summary>"));
            StringAssert.Contains(updated, "Existing docs.");
            StringAssert.Contains(updated, "Generated docs.");
        }

        private static string InvokeGenerateXmlDocumentation(string source, Func<MethodDeclarationSyntax, string> summaryProvider, int maxMethodsPerFile)
        {
            var assembly = typeof(CodeJanitor.Properties.Settings).Assembly;
            var type = assembly.GetType("CodeJanitor.Logic.Cleaning.AiXmlDocumentationLogic", throwOnError: true);
            var method = type.GetMethod("GenerateXmlDocumentationForSource", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method, "Could not locate GenerateXmlDocumentationForSource via reflection.");

            var result = method.Invoke(null, new object[] { source, summaryProvider, maxMethodsPerFile });
            return result as string;
        }

        private static int CountOccurrences(string text, string value)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
            {
                return 0;
            }

            return text.Split(new[] { value }, StringSplitOptions.None).Length - 1;
        }
    }
}