using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Ai;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CodeJanitor.UnitTests.Ai;

[TestClass]
public sealed class AiFeaturesLogicTests
{
    [TestMethod]
    public async Task AiExplainLogic_ExplainCodeAsync_WhenCodeIsEmpty_ReturnsNoCodeMessage()
    {
        // Arrange
        var logic = CreateInstance<AiExplainLogic>();

        // Act
        var result = await logic.ExplainCodeAsync("TestMethod", string.Empty, CancellationToken.None);

        // Assert
        Assert.IsTrue(result.Contains("No code provided"), $"Expected 'No code provided' message but got: {result}");
    }

    [TestMethod]
    public async Task AiTestGeneratorLogic_GenerateUnitTestsAsync_WhenCodeIsEmpty_ReturnsNoCodeComment()
    {
        // Arrange
        var logic = CreateInstance<AiTestGeneratorLogic>();

        // Act
        var result = await logic.GenerateUnitTestsAsync("TestMethod", "   ", CancellationToken.None);

        // Assert
        Assert.IsTrue(result.Contains("No code provided"), $"Expected 'No code provided' comment but got: {result}");
    }

    [TestMethod]
    public void AiTestGeneratorLogic_ExtractCodeSnippet_ExtractsFromMarkdownBlocks()
    {
        // Arrange
        var responseWithMarkdown = "Here is the unit test class:\n```csharp\nusing Xunit;\npublic class SampleTests { }\n```\nHope this helps!";

        var method = typeof(AiTestGeneratorLogic).GetMethod("ExtractCodeSnippet", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method, "ExtractCodeSnippet method should exist on AiTestGeneratorLogic");

        // Act
        var extracted = (string)method.Invoke(null, new object[] { responseWithMarkdown });

        // Assert
        Assert.AreEqual("using Xunit;\npublic class SampleTests { }", extracted);
    }

    [TestMethod]
    public async Task AiCleanRefactorLogic_RefactorCodeAsync_WhenCodeIsEmpty_ReturnsFailure()
    {
        // Arrange
        var logic = CreateInstance<AiCleanRefactorLogic>();

        // Act
        var result = await logic.RefactorCodeAsync("TestMethod", null, CancellationToken.None);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.ErrorMessage.Contains("No code provided"));
    }

    [TestMethod]
    public void AiCleanRefactorLogic_ExtractCodeSnippet_ExtractsCleanCode()
    {
        // Arrange
        var response = "Key improvements:\n- Guard clauses\n- Flattened nesting\n```csharp\npublic void CleanMethod() { return; }\n```";

        var method = typeof(AiCleanRefactorLogic).GetMethod("ExtractCodeSnippet", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method, "ExtractCodeSnippet method should exist on AiCleanRefactorLogic");

        // Act
        var extracted = (string)method.Invoke(null, new object[] { response });

        // Assert
        Assert.AreEqual("public void CleanMethod() { return; }", extracted);
    }

    [TestMethod]
    public async Task AiCodeReviewLogic_ReviewCodeAsync_WhenCodeIsEmpty_ReturnsNoCodeMessage()
    {
        // Arrange
        var logic = CreateInstance<AiCodeReviewLogic>();

        // Act
        var result = await logic.ReviewCodeAsync("TestMethod", "", CancellationToken.None);

        // Assert
        Assert.IsTrue(result.Contains("No code provided"));
    }

    [TestMethod]
    public void AiExplainLogic_BuildExplainPrompt_IncludesMemberNameAndCode()
    {
        // Arrange
        var method = typeof(AiExplainLogic).GetMethod("BuildExplainPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method, "BuildExplainPrompt method should exist");

        // Act
        var prompt = (string)method.Invoke(null, new object[] { "CalculateDiscount", "public int CalculateDiscount() => 10;" });

        // Assert
        Assert.IsTrue(prompt.Contains("CalculateDiscount"));
        Assert.IsTrue(prompt.Contains("public int CalculateDiscount() => 10;"));
        Assert.IsTrue(prompt.Contains("Complexity & Risk Assessment"));
    }

    [TestMethod]
    public void AiTestGeneratorLogic_ExtractCodeSnippet_HandlesGenericCodeBlockAndPlainText()
    {
        // Arrange
        var method = typeof(AiTestGeneratorLogic).GetMethod("ExtractCodeSnippet", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        // Act & Assert - generic code block ```
        var genericBlock = "Summary:\n```\npublic class GenericTest {}\n```";
        var extractedGeneric = (string)method.Invoke(null, new object[] { genericBlock });
        Assert.AreEqual("public class GenericTest {}", extractedGeneric);

        // Act & Assert - plain text
        var plain = "public class PlainTest {}";
        var extractedPlain = (string)method.Invoke(null, new object[] { plain });
        Assert.AreEqual("public class PlainTest {}", extractedPlain);

        // Act & Assert - null/empty
        var extractedEmpty = (string)method.Invoke(null, new object[] { "   " });
        Assert.AreEqual(string.Empty, extractedEmpty);
    }

    [TestMethod]
    public void AiCleanRefactorLogic_ExtractCodeSnippet_HandlesGenericMarkdownAndEmptyInput()
    {
        // Arrange
        var method = typeof(AiCleanRefactorLogic).GetMethod("ExtractCodeSnippet", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        // Act & Assert - generic block
        var genericBlock = "Code:\n```\npublic void Simple() {}\n```";
        var extracted = (string)method.Invoke(null, new object[] { genericBlock });
        Assert.AreEqual("public void Simple() {}", extracted);

        // Act & Assert - null/empty
        var extractedEmpty = (string)method.Invoke(null, new object[] { null });
        Assert.AreEqual(string.Empty, extractedEmpty);
    }

    private static T CreateInstance<T>() where T : class
    {
        var ctor = typeof(T).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(CodeJanitorPackage) }, null);

        return (T)ctor?.Invoke(new object[] { null });
    }
}
