using System;
using System.Linq;
using CodeJanitor.Logic.Cleaning.Diagnostics;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests.Cleaning.Diagnostics;

/// <summary>
/// Unit tests for <see cref="CodeFixProviderCatalog" />.
/// </summary>
[TestClass]
public sealed class CodeFixProviderCatalogTests
{
    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void GetProviders_ScansSolutionAnalyzerReferences_InDeterministicOrderWithoutDuplicates()
    {
        using var workspace = new DiagnosticCleanupTestWorkspace();
        var project = workspace.CreateSolution().Projects.Single();

        var providers = new CodeFixProviderCatalog().GetProviders(project);

        Assert.IsTrue(providers.Any(provider => provider.FixableDiagnosticIds.Contains("IDE1006")), "The naming fixer of the host analyzer reference must be discovered.");
        var typeNames = providers.Select(provider => provider.GetType().AssemblyQualifiedName).ToList();
        CollectionAssert.AreEqual(typeNames.OrderBy(name => name, StringComparer.Ordinal).ToList(), typeNames);
        Assert.AreEqual(typeNames.Count, providers.Select(provider => provider.GetType().FullName).Distinct().Count());
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void GetProviders_HostSuppliedProvider_ReplacesScannedProviderOfTheSameType()
    {
        using var workspace = new DiagnosticCleanupTestWorkspace();
        var project = workspace.CreateSolution().Projects.Single();
        var scannedType = new CodeFixProviderCatalog().GetProviders(project).Single(provider => provider.FixableDiagnosticIds.Contains("IDE1006")).GetType();
        var hostInstance = (CodeFixProvider)Activator.CreateInstance(scannedType, nonPublic: true);

        var providers = new CodeFixProviderCatalog(new[] { hostInstance }).GetProviders(project);

        Assert.AreSame(hostInstance, providers.Single(provider => provider.GetType().FullName == scannedType.FullName));
    }
}
