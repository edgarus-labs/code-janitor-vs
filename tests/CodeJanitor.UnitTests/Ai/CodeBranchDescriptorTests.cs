using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Logic.Ai;

namespace CodeJanitor.UnitTests.Ai;

[TestClass]
public sealed class CodeBranchDescriptorTests
{
    [TestMethod]
    public void Properties_RoundTripAssignedValues()
    {
        var descriptor = new CodeBranchDescriptor
        {
            Id = "B1",
            BranchType = CodeBranchType.GuardClause,
            Description = "Null argument guard",
            ConditionSnippet = "arg is null",
            LineNumber = 42
        };

        Assert.AreEqual("B1", descriptor.Id);
        Assert.AreEqual(CodeBranchType.GuardClause, descriptor.BranchType);
        Assert.AreEqual("Null argument guard", descriptor.Description);
        Assert.AreEqual("arg is null", descriptor.ConditionSnippet);
        Assert.AreEqual(42, descriptor.LineNumber);
    }

    [TestMethod]
    public void ToString_FormatsBranchTypeDescriptionAndLineNumber()
    {
        var descriptor = new CodeBranchDescriptor
        {
            BranchType = CodeBranchType.ElseBranch,
            Description = "Fallback path",
            LineNumber = 7
        };

        Assert.AreEqual("[ElseBranch] Fallback path (Line 7)", descriptor.ToString());
    }
}
