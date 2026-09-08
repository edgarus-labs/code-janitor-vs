using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Cleaning;

namespace CodeJanitor.UnitTests.Cleaning;

[TestClass]
public sealed class RemoveXmlDocumentationLogicTests
{
    [TestMethod]
    public void TryRemoveXmlDocumentation_RemovesSingleLineDocCommentsFromClassAndMethod()
    {
        var input = @"namespace Demo;

/// <summary>
/// Sample class summary.
/// </summary>
public class Sample
{
    /// <summary>
    /// Computes the sum of two integers.
    /// </summary>
    /// <param name=""a"">The first number.</param>
    /// <param name=""b"">The second number.</param>
    /// <returns>The total.</returns>
    public int Add(int a, int b)
    {
        return a + b;
    }
}
";

        var changed = RemoveXmlDocumentationLogic.TryRemoveXmlDocumentation(input, out var result);

        Assert.IsTrue(changed);
        Assert.IsFalse(result.Contains("/// <summary>"));
        Assert.IsFalse(result.Contains("Sample class summary."));
        Assert.IsFalse(result.Contains("Computes the sum of two integers."));
        Assert.IsFalse(result.Contains("<param"));
        Assert.IsFalse(result.Contains("<returns"));
        Assert.IsTrue(result.Contains("public class Sample"));
        Assert.IsTrue(result.Contains("    public int Add(int a, int b)"));
    }

    [TestMethod]
    public void TryRemoveXmlDocumentation_RemovesMultiLineDocComments()
    {
        var input = @"namespace Demo;

/**
 * <summary>
 * Multi-line documentation comment.
 * </summary>
 */
public interface IService
{
    void Execute();
}
";

        var changed = RemoveXmlDocumentationLogic.TryRemoveXmlDocumentation(input, out var result);

        Assert.IsTrue(changed);
        Assert.IsFalse(result.Contains("Multi-line documentation comment."));
        Assert.IsTrue(result.Contains("public interface IService"));
    }

    [TestMethod]
    public void TryRemoveXmlDocumentation_PreservesRegularComments()
    {
        var input = @"namespace Demo;

// Regular single line comment
/* Regular block comment */
public class Sample
{
    // Implementation note
    /// <summary>
    /// Method summary to remove.
    /// </summary>
    public void DoWork()
    {
        // Inside method body
    }
}
";

        var changed = RemoveXmlDocumentationLogic.TryRemoveXmlDocumentation(input, out var result);

        Assert.IsTrue(changed);
        Assert.IsFalse(result.Contains("Method summary to remove."));
        Assert.IsTrue(result.Contains("// Regular single line comment"));
        Assert.IsTrue(result.Contains("/* Regular block comment */"));
        Assert.IsTrue(result.Contains("// Implementation note"));
        Assert.IsTrue(result.Contains("// Inside method body"));
    }

    [TestMethod]
    public void TryRemoveXmlDocumentation_ReturnsFalseWhenNoXmlDocPresent()
    {
        var input = @"namespace Demo;

public class CleanClass
{
    // Just a regular comment
    public void CleanMethod()
    {
    }
}
";

        var changed = RemoveXmlDocumentationLogic.TryRemoveXmlDocumentation(input, out var result);

        Assert.IsFalse(changed);
        Assert.AreEqual(input, result);
    }

    [TestMethod]
    public void TryRemoveXmlDocumentation_HandlesNullOrWhitespace()
    {
        Assert.IsFalse(RemoveXmlDocumentationLogic.TryRemoveXmlDocumentation(null, out var r1));
        Assert.IsNull(r1);

        Assert.IsFalse(RemoveXmlDocumentationLogic.TryRemoveXmlDocumentation("   ", out var r2));
        Assert.AreEqual("   ", r2);
    }
}
