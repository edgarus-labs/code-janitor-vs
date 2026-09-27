using System;
using System.Collections.Generic;
using System.Linq;
using CodeJanitor.Logic.Cleaning.Diagnostics;
using Microsoft.CodeAnalysis;
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
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        Project project = workspace.CreateSolution().Projects.Single();

        System.Collections.Immutable.ImmutableArray<CodeFixProvider> providers = new CodeFixProviderCatalog().GetProviders(project);

        Assert.IsTrue(providers.Any(provider => provider.FixableDiagnosticIds.Contains("IDE1006")), "The naming fixer of the host analyzer reference must be discovered.");
        List<string> typeNames = providers.Select(provider => provider.GetType().AssemblyQualifiedName).ToList();
        Assert.AreSequenceEqual(typeNames.OrderBy(name => name, StringComparer.Ordinal).ToList(), typeNames);
        Assert.HasCount(typeNames.Count, providers.Select(provider => provider.GetType().FullName).Distinct());
    }

    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void GetProviders_HostSuppliedProvider_ReplacesScannedProviderOfTheSameType()
    {
        using DiagnosticCleanupTestWorkspace workspace = new DiagnosticCleanupTestWorkspace();
        Project project = workspace.CreateSolution().Projects.Single();
        Type scannedType = new CodeFixProviderCatalog().GetProviders(project).Single(provider => provider.FixableDiagnosticIds.Contains("IDE1006")).GetType();
        CodeFixProvider hostInstance = (CodeFixProvider)Activator.CreateInstance(scannedType, nonPublic: true);

        System.Collections.Immutable.ImmutableArray<CodeFixProvider> providers = new CodeFixProviderCatalog(new[] { hostInstance }).GetProviders(project);

        Assert.AreSame(hostInstance, providers.Single(provider => provider.GetType().FullName == scannedType.FullName));
    }
}
