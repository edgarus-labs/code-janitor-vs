using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodeJanitor.Logic.Cleaning.Diagnostics;

/// <summary>
/// Supplies the existing <see cref="CodeFixProvider" />s available to a project: the providers supplied by the host
/// (e.g. Visual Studio's MEF exports) plus every <see cref="ExportCodeFixProviderAttribute" />-annotated provider found
/// in the assemblies behind the project's and the solution's analyzer references (NuGet analyzers, host analyzers).
/// </summary>
/// <remarks>
/// Scanned assemblies are cached, so the provider instances of an assembly are created once per catalog. The order
/// is deterministic: by provider type assembly-qualified name (ordinal). Providers of the same type (full name) are
/// collapsed; a host-supplied instance wins over a scanned one because it may carry imports that a reflection-created
/// instance lacks. Providers whose type cannot be created without dependency injection are only available when the
/// host supplies them.
/// </remarks>
public sealed class CodeFixProviderCatalog
{
    private readonly ImmutableArray<ProviderEntry> _hostProviders;
    private readonly ConcurrentDictionary<Assembly, ImmutableArray<ProviderEntry>> _scannedAssemblies =
        new ConcurrentDictionary<Assembly, ImmutableArray<ProviderEntry>>();

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeFixProviderCatalog" /> class.
    /// </summary>
    /// <param name="additionalProviders">Providers supplied by the host, e.g. Visual Studio's MEF exports.</param>
    public CodeFixProviderCatalog(IEnumerable<CodeFixProvider> additionalProviders = null)
    {
        _hostProviders = additionalProviders == null
            ? ImmutableArray<ProviderEntry>.Empty
            : additionalProviders
                .Where(provider => provider != null)
                .Select(provider => new ProviderEntry(provider, GetExportedLanguages(provider.GetType())))
                .ToImmutableArray();
    }

    /// <summary>
    /// Gets the code fix providers for the language of <paramref name="project" />, in deterministic order.
    /// </summary>
    /// <param name="project">The project whose analyzer references are scanned.</param>
    /// <returns>The providers, ordered by type assembly-qualified name, one per provider type.</returns>
    public ImmutableArray<CodeFixProvider> GetProviders(Project project)
    {
        if (project == null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        var language = project.Language;
        var hostProviders = _hostProviders
            .Where(host => host.Languages.Length == 0 || host.Languages.Contains(language, StringComparer.Ordinal))
            .Select(host => host.Provider);
        var scannedProviders = project.AnalyzerReferences
            .Concat(project.Solution.AnalyzerReferences)
            .Select(GetAssembly)
            .Where(assembly => assembly != null)
            .Distinct()
            .SelectMany(assembly => _scannedAssemblies.GetOrAdd(assembly, ScanAssembly))
            .Where(scanned => scanned.Languages.Contains(language, StringComparer.Ordinal))
            .Select(scanned => scanned.Provider)
            .OrderBy(GetSortKey, StringComparer.Ordinal);

        return hostProviders
            .Concat(scannedProviders)
            .GroupBy(provider => provider.GetType().FullName, StringComparer.Ordinal)
            .Select(sameType => sameType.First())
            .OrderBy(GetSortKey, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static string GetSortKey(CodeFixProvider provider) => provider.GetType().AssemblyQualifiedName;

    /// <summary>
    /// Only file references expose the assembly behind them; fixers for analyzers passed as instances must be
    /// supplied by the host. A reference whose assembly cannot be loaded contributes no analyzers either.
    /// </summary>
    private static Assembly GetAssembly(AnalyzerReference reference)
    {
        if (!(reference is AnalyzerFileReference fileReference))
        {
            return null;
        }

        try
        {
            return fileReference.GetAssembly();
        }
        catch (Exception exception) when (exception is IOException || exception is BadImageFormatException || exception is UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static ImmutableArray<ProviderEntry> ScanAssembly(Assembly assembly)
    {
        var builder = ImmutableArray.CreateBuilder<ProviderEntry>();

        foreach (var type in GetLoadableTypes(assembly))
        {
            if (type.IsAbstract || type.ContainsGenericParameters || !typeof(CodeFixProvider).IsAssignableFrom(type))
            {
                continue;
            }

            var languages = GetExportedLanguages(type);
            if (languages.Length == 0)
            {
                continue;
            }

            var provider = TryCreateProvider(type);
            if (provider != null)
            {
                builder.Add(new ProviderEntry(provider, languages));
            }
        }

        return builder.ToImmutable();
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type != null);
        }
    }

    private static string[] GetExportedLanguages(Type providerType) =>
        providerType.GetCustomAttributes<ExportCodeFixProviderAttribute>(inherit: false)
            .SelectMany(export => export.Languages ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Creates a provider through its parameterless (MEF importing) constructor. Providers that need injected
    /// dependencies, or whose dependencies cannot be loaded, are skipped: their diagnostics are then reported as
    /// having no code fix provider unless the host supplies an instance.
    /// </summary>
    private static CodeFixProvider TryCreateProvider(Type type)
    {
        try
        {
            return (CodeFixProvider)Activator.CreateInstance(type, nonPublic: true);
        }
        catch (Exception exception) when (exception is MemberAccessException
            || exception is TargetInvocationException
            || exception is TypeLoadException
            || exception is IOException
            || exception is BadImageFormatException
            || exception is NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// A provider with the languages it is exported for (empty: not exported, i.e. any language).
    /// </summary>
    private sealed class ProviderEntry
    {
        public ProviderEntry(CodeFixProvider provider, string[] languages)
        {
            Provider = provider;
            Languages = languages;
        }

        public CodeFixProvider Provider { get; }

        public string[] Languages { get; }
    }
}
