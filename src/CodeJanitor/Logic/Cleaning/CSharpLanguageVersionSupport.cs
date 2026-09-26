using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// Decides whether a cleanup step may emit syntax that needs a C# language version newer than C# 7.3 into a file: every
/// project (and target framework) that compiles the file must use that language version or newer. When the language
/// version cannot be determined the syntax is not emitted, because it might not compile.
/// </summary>
internal sealed class CSharpLanguageVersionSupport
{
    /// <summary>
    /// File-scoped namespace declarations (<c>namespace N;</c>).
    /// </summary>
    internal static readonly SyntaxRequirement FileScopedNamespaces = new SyntaxRequirement("file-scoped namespaces", LanguageVersion.CSharp10);

    /// <summary>
    /// Collection expressions (<c>[1, 2]</c>).
    /// </summary>
    internal static readonly SyntaxRequirement CollectionExpressions = new SyntaxRequirement("collection expressions", LanguageVersion.CSharp12);

    /// <summary>
    /// The negated null pattern (<c>is not null</c>); the null pattern (<c>is null</c>) only needs C# 7.0.
    /// </summary>
    internal static readonly SyntaxRequirement NotNullPatterns = new SyntaxRequirement("'is not null' patterns", LanguageVersion.CSharp9);

    private static Func<string, IReadOnlyList<LanguageVersion>> _languageVersionResolver;

    private readonly string _filePath;
    private readonly Lazy<(LanguageVersion? Oldest, string UnknownReason)> _oldestVersion;

    /// <summary>
    /// Initializes a new instance of the <see cref="CSharpLanguageVersionSupport" /> class.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="resolver">The language version resolver, or null when none is available.</param>

    private CSharpLanguageVersionSupport(string filePath, Func<string, IReadOnlyList<LanguageVersion>> resolver)
    {
        _filePath = filePath;
        _oldestVersion = new Lazy<(LanguageVersion?, string)>(() => ResolveOldestVersion(filePath, resolver));
    }

    /// <summary>
    /// Resolves language versions through the Visual Studio Roslyn workspace.
    /// </summary>
    /// <param name="package">The hosting package.</param>

    internal static void UseVisualStudioWorkspace(CodeJanitorPackage package)
    {
        SetLanguageVersionResolver(new VisualStudioRoslynWorkspace(package).GetCSharpLanguageVersions);
    }

    /// <summary>
    /// Sets the function that returns the C# language versions of the projects compiling a file (one entry per project
    /// flavor, empty when no project compiles it). Null removes it, so no syntax newer than C# 7.3 is emitted.
    /// </summary>
    /// <param name="resolver">The resolver.</param>

    internal static void SetLanguageVersionResolver(Func<string, IReadOnlyList<LanguageVersion>> resolver)
    {
        _languageVersionResolver = resolver;
    }

    /// <summary>
    /// Gets the language version support of a file. The language versions are resolved on the first
    /// <see cref="Supports" /> call, so a cleanup that emits no version-dependent syntax never resolves them.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>The language version support of the file.</returns>

    internal static CSharpLanguageVersionSupport For(string filePath)
    {
        return new CSharpLanguageVersionSupport(filePath, _languageVersionResolver);
    }

    /// <summary>
    /// Determines whether the specified syntax may be emitted into the file.
    /// </summary>
    /// <param name="requirement">The syntax and the language version it needs.</param>
    /// <param name="skipMessage">The warning explaining why the conversion to the syntax is skipped; null when it may be emitted.</param>
    /// <returns>True when every project compiling the file uses the required language version or newer.</returns>

    internal bool Supports(SyntaxRequirement requirement, out string skipMessage)
    {
        var (oldest, unknownReason) = _oldestVersion.Value;
        string reason;
        if (oldest is null)
        {
            reason = unknownReason;
        }
        else if (oldest.Value < requirement.MinimumVersion)
        {
            reason = $"its project uses C# {oldest.Value.ToDisplayString()} and {requirement.Syntax} require C# {requirement.MinimumVersion.ToDisplayString()} or newer";
        }
        else
        {
            skipMessage = null;

            return true;
        }

        skipMessage = $"CodeJanitor skipped the conversion to {requirement.Syntax} for '{_filePath}' because {reason}.";

        return false;
    }

    /// <summary>
    /// Resolves the oldest effective C# language version among the projects compiling the file.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="resolver">The language version resolver, or null when none is available.</param>
    /// <returns>The oldest language version, or null and the reason it is unknown.</returns>

    private static (LanguageVersion? Oldest, string UnknownReason) ResolveOldestVersion(string filePath, Func<string, IReadOnlyList<LanguageVersion>> resolver)
    {
        if (resolver is null)
        {
            return (null, "the C# language version of its project is unknown (the Visual Studio Roslyn workspace is not available)");
        }

        IReadOnlyList<LanguageVersion> versions;
        try
        {
            versions = resolver(filePath);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            return (null, VisualStudioRoslynWorkspace.IsRoslynBindingFailure(ex)
                ? "the C# language version of its project could not be read (CodeJanitor is compiled against Microsoft.CodeAnalysis 5.0; the host Roslyn may be older)"
                : $"the C# language version of its project could not be read ({ex.Message})");
        }

        if (versions is null || versions.Count == 0)
        {
            return (null, "no C# project loaded in the solution compiles it, so its C# language version is unknown");
        }

        return (versions.Select(version => version.MapSpecifiedToEffectiveVersion()).Min(), null);
    }

    /// <summary>
    /// Syntax emitted by a cleanup step and the oldest C# language version that compiles it.
    /// </summary>
    internal sealed class SyntaxRequirement
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SyntaxRequirement" /> class.
        /// </summary>
        /// <param name="syntax">The syntax, in plural form (for example "collection expressions").</param>
        /// <param name="minimumVersion">The oldest language version that compiles the syntax.</param>

        internal SyntaxRequirement(string syntax, LanguageVersion minimumVersion)
        {
            Syntax = syntax;
            MinimumVersion = minimumVersion;
        }

        /// <summary>
        /// Gets the syntax, in plural form.
        /// </summary>
        internal string Syntax { get; }

        /// <summary>
        /// Gets the oldest language version that compiles the syntax.
        /// </summary>
        internal LanguageVersion MinimumVersion { get; }
    }
}
