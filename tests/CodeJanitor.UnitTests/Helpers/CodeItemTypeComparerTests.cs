using CodeJanitor.Helpers;
using CodeJanitor.Model.CodeItems;
using CodeJanitor.Properties;
using EnvDTE80;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace CodeJanitor.UnitTests.Helpers;

[TestClass]
public sealed class CodeItemTypeComparerTests
{
    [TestInitialize]
    public void TestInitialize()
    {
        Settings.Default.Reset();
    }

    [TestMethod]
    public void ShouldSortItemsOfTheSameTypeByName()
    {
        BaseCodeItem itemB = Create<CodeItemField>("b", 1);
        BaseCodeItem itemA = Create<CodeItemField>("a", 2);
        CodeItemTypeComparer comparer = new CodeItemTypeComparer(sortByName: true);

        int result = comparer.Compare(itemA, itemB);

        Assert.IsLessThan(0, result);
    }

    [TestMethod]
    public void ShouldSortItemsOfTheSameTypeByOffset()
    {
        BaseCodeItem itemB = Create<CodeItemField>("b", 1);
        BaseCodeItem itemA = Create<CodeItemField>("a", 2);
        CodeItemTypeComparer comparer = new CodeItemTypeComparer(sortByName: false);

        int result = comparer.Compare(itemA, itemB);

        Assert.IsGreaterThan(0, result);
    }

    [TestMethod]
    public void ShouldSortByGroupType()
    {
        BaseCodeItem method = Create<CodeItemMethod>("a", 1);
        BaseCodeItem field = Create<CodeItemField>("z", 2);
        CodeItemTypeComparer comparer = new CodeItemTypeComparer(sortByName: true);

        int result = comparer.Compare(field, method);

        Assert.IsLessThan(0, result);
    }

    [TestMethod]
    public void ShouldSortByExplicitInterfaceMemberName()
    {
        CodeItemMethod methodZ = CreateExplicitMethod("Interface", "Z", 1);
        BaseCodeItem methodX = Create<CodeItemMethod>("X", 2);
        CodeItemTypeComparer comparer = new CodeItemTypeComparer(sortByName: true);

        Settings.Default.Reorganizing_ExplicitMembersAtEnd = false;
        int result = comparer.Compare(methodX, methodZ);

        Assert.IsLessThan(0, result);
    }

    [TestMethod]
    public void ShouldPlaceExplicitInterfaceMembersAtTheEndOfTheGroup()
    {
        CodeItemMethod methodA = CreateExplicitMethod("Interface", "A", 1);
        BaseCodeItem methodB = Create<CodeItemMethod>("B", 2);
        CodeItemTypeComparer comparer = new CodeItemTypeComparer(sortByName: true);

        Settings.Default.Reorganizing_ExplicitMembersAtEnd = true;
        int result = comparer.Compare(methodB, methodA);

        Assert.IsLessThan(0, result);
    }

    private static T Create<T>(string name, int offset) where T : BaseCodeItem, new()
    {
        return new T
        {
            Name = name,
            StartOffset = offset
        };
    }

    private static CodeItemMethod CreateExplicitMethod(string interfaceName, string methodName, int offset)
    {
        CodeItemMethod method = Create<CodeItemMethod>(interfaceName + "." + methodName, offset);
        method.CodeFunction = Substitute.For<CodeFunction2>();
        method.CodeFunction.Name = method.Name;

        return method;
    }
}
