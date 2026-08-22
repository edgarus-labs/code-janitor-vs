using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// Planner that determines how to split a top-level type into separate files by identifying and relocating its contained types while handling naming, partial declarations, and file uniqueness.
/// </summary>
internal sealed class TopLevelTypeToFileSplitPlanner
{
    /// <summary>
    /// PlannedFile represents a file intended for creation or processing, encapsulating its destination path along with the content to be written.
    /// </summary>
    internal sealed class PlannedFile
    {
        internal PlannedFile(string filePath, string content)
        {
            FilePath = filePath;
            Content = content;
        }

        /// <summary>
        /// Gets the file path.
        /// </summary>
        internal string FilePath { get; }

        /// <summary>
        /// Gets the content.
        /// </summary>
        internal string Content { get; }
    }

    /// <summary>
    /// SplitPlan represents a proposed plan for splitting an existing source into updated source and new files, indicating whether changes are present and providing a reason if the operation is skipped.
    /// </summary>
    internal sealed class SplitPlan
    {
        internal SplitPlan(
            string updatedSource,
            IReadOnlyList<PlannedFile> newFiles,
            TopLevelTypeSplitSkipReason skipReason = TopLevelTypeSplitSkipReason.None)
        {
            UpdatedSource = updatedSource;
            NewFiles = newFiles;
            SkipReason = skipReason;
        }

        /// <summary>
        /// Gets the updated source.
        /// </summary>
        internal string UpdatedSource { get; }

        /// <summary>
        /// Gets the new files.
        /// </summary>
        internal IReadOnlyList<PlannedFile> NewFiles { get; }

        /// <summary>
        /// Gets the skip reason.
        /// </summary>
        internal TopLevelTypeSplitSkipReason SkipReason { get; }

        /// <summary>
        /// Gets the has changes.
        /// </summary>
        internal bool HasChanges => NewFiles.Count > 0;
    }

    /// <summary>
    /// Creates a SplitPlan by parsing the C# source, identifying multiple eligible top-level types, keeping one in the original file while generating new planned .cs files for the others with unique file names based on existing files in the target directory, and returns empty plans with skip reasons for empty input, unsupported structures, or fewer than two eligible types.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="filePath">The file path.</param>
    /// <returns>A SplitPlan value produced by this method.</returns>

    internal SplitPlan CreatePlan(string source, string filePath)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(filePath))
        {
            return new SplitPlan(source, Array.Empty<PlannedFile>(), TopLevelTypeSplitSkipReason.EmptySource);
        }

        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        if (HasUnsupportedStructure(root) ||
            !TryGetContainerMembers(root, out var members) ||
            members.Any(x => x is BaseNamespaceDeclarationSyntax || x is GlobalStatementSyntax))
        {
            return new SplitPlan(source, Array.Empty<PlannedFile>(), TopLevelTypeSplitSkipReason.UnsupportedStructure);
        }

        var eligibleMembers = members.Where(IsEligibleTopLevelType).ToList();
        if (eligibleMembers.Count <= 1)
        {
            return new SplitPlan(source, Array.Empty<PlannedFile>(), TopLevelTypeSplitSkipReason.NotMultipleEligibleTypes);
        }

        var memberToKeep = ChooseMemberToKeep(eligibleMembers, Path.GetFileName(filePath)) ?? eligibleMembers[0];
        var movedMembers = eligibleMembers.Where(x => x != memberToKeep).ToList();
        if (movedMembers.Count == 0)
        {
            return new SplitPlan(source, Array.Empty<PlannedFile>(), TopLevelTypeSplitSkipReason.NotMultipleEligibleTypes);
        }

        var updatedMembers = members.Where(x => !movedMembers.Contains(x)).ToList();
        var updatedRoot = ReplaceContainedMembers(root, updatedMembers);

        var directoryPath = Path.GetDirectoryName(filePath) ?? string.Empty;
        var reservedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(directoryPath))
        {
            foreach (var existingFilePath in Directory.GetFiles(directoryPath, "*.cs"))
            {
                reservedFileNames.Add(Path.GetFileName(existingFilePath));
            }
        }

        var plannedFiles = new List<PlannedFile>();
        foreach (var movedMember in movedMembers)
        {
            var desiredFileName = BuildTypeFileName(movedMember);
            var finalFileName = MakeFileNameUnique(desiredFileName, reservedFileNames);
            reservedFileNames.Add(finalFileName);

            var newRoot = ReplaceContainedMembers(root, new[] { movedMember });
            plannedFiles.Add(new PlannedFile(Path.Combine(directoryPath, finalFileName), newRoot.ToFullString()));
        }

        return new SplitPlan(updatedRoot.ToFullString(), plannedFiles);
    }

    /// <summary>
    /// Builds a C# filename by appending the &quot;.cs&quot; extension to the type file stem derived from the given member declaration syntax, with no side effects or exceptions.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <returns>A string value produced by this method.</returns>

    internal static string BuildTypeFileName(MemberDeclarationSyntax member)
    {
        return BuildTypeFileStem(member) + ".cs";
    }

    /// <summary>
    /// Constructs a file stem from a member&apos;s identifier, appending its type parameter names in curly braces when present, with no side effects.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <returns>A string value produced by this method.</returns>

    internal static string BuildTypeFileStem(MemberDeclarationSyntax member)
    {
        var identifier = GetIdentifier(member);
        var typeParameters = GetTypeParameterNames(member);

        return typeParameters.Count == 0
            ? identifier
            : identifier + "{" + string.Join(",", typeParameters) + "}";
    }

    /// <summary>
    /// Returns true if the compilation unit contains attribute lists, non-region preprocessor directives, multiple namespaces, or a namespace with additional top-level members, otherwise false, with no side effects.
    /// </summary>
    /// <param name="root">The root.</param>
    /// <returns>A bool value produced by this method.</returns>

    private static bool HasUnsupportedStructure(CompilationUnitSyntax root)
    {
        if (root.AttributeLists.Count > 0)
        {
            return true;
        }

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true).Where(x => x.IsDirective))
        {
            var directive = trivia.GetStructure();
            if (!(directive is RegionDirectiveTriviaSyntax) && !(directive is EndRegionDirectiveTriviaSyntax))
            {
                return true;
            }
        }

        var namespaces = root.Members.OfType<BaseNamespaceDeclarationSyntax>().ToList();
        if (namespaces.Count > 1)
        {
            return true;
        }

        if (namespaces.Count == 1 && root.Members.Count != 1)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// If the compilation unit contains exactly one member that is a namespace declaration, returns its members via the out parameter; otherwise returns all root members, and always returns true while populating the out parameter.
    /// </summary>
    /// <param name="root">The root.</param>
    /// <param name="members">The members.</param>
    /// <returns>A bool value produced by this method.</returns>

    private static bool TryGetContainerMembers(CompilationUnitSyntax root, out IReadOnlyList<MemberDeclarationSyntax> members)
    {
        if (root.Members.Count == 1 && root.Members[0] is BaseNamespaceDeclarationSyntax namespaceDeclaration)
        {
            members = namespaceDeclaration.Members.ToList();

            return true;
        }

        members = root.Members.ToList();

        return true;
    }

    /// <summary>
    /// Returns true only for class, interface, record, enum, or delegate declarations, excluding partial classes, interfaces, records, and enums, with no side effects.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <returns>A bool value produced by this method.</returns>

    private static bool IsEligibleTopLevelType(MemberDeclarationSyntax member)
    {
        if (member is ClassDeclarationSyntax classDeclaration)
        {
            return !HasPartialModifier(classDeclaration.Modifiers);
        }

        if (member is InterfaceDeclarationSyntax interfaceDeclaration)
        {
            return !HasPartialModifier(interfaceDeclaration.Modifiers);
        }

        if (member is RecordDeclarationSyntax recordDeclaration)
        {
            return !HasPartialModifier(recordDeclaration.Modifiers);
        }

        if (member is EnumDeclarationSyntax enumDeclaration)
        {
            return !HasPartialModifier(enumDeclaration.Modifiers);
        }

        return member is DelegateDeclarationSyntax;
    }

    /// <summary>
    /// Returns true if the modifier list contains a partial keyword token, otherwise false, with no side effects or exceptions.
    /// </summary>
    /// <param name="modifiers">The modifiers.</param>
    /// <returns>A bool value produced by this method.</returns>

    private static bool HasPartialModifier(SyntaxTokenList modifiers)
    {
        return modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword));
    }

    /// <summary>
    /// Returns the first eligible member whose generated type file name matches the original file name (case-insensitive), or null if no match is found.
    /// </summary>
    /// <param name="eligibleMembers">The eligible members.</param>
    /// <param name="originalFileName">The original file name.</param>
    /// <returns>A MemberDeclarationSyntax value produced by this method.</returns>

    private static MemberDeclarationSyntax ChooseMemberToKeep(IEnumerable<MemberDeclarationSyntax> eligibleMembers, string originalFileName)
    {
        return eligibleMembers.FirstOrDefault(x => string.Equals(BuildTypeFileName(x), originalFileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Replaces the compilation unit&apos;s members, or the members of a sole file-scoped or regular namespace, with the provided list and returns a new syntax tree without modifying the original.
    /// </summary>
    /// <param name="root">The root.</param>
    /// <param name="members">The members.</param>
    /// <returns>A CompilationUnitSyntax value produced by this method.</returns>

    private static CompilationUnitSyntax ReplaceContainedMembers(CompilationUnitSyntax root, IEnumerable<MemberDeclarationSyntax> members)
    {
        var memberList = SyntaxFactory.List(members);

        if (root.Members.Count == 1 && root.Members[0] is FileScopedNamespaceDeclarationSyntax fileScopedNamespace)
        {
            return root.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(fileScopedNamespace.WithMembers(memberList)));
        }

        if (root.Members.Count == 1 && root.Members[0] is NamespaceDeclarationSyntax namespaceDeclaration)
        {
            return root.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(namespaceDeclaration.WithMembers(memberList)));
        }

        return root.WithMembers(memberList);
    }

    /// <summary>
    /// If the desired filename is not reserved it returns it unchanged, otherwise it appends a numeric suffix with a tilde before the extension, incrementing until an unreserved candidate is found, with no side effects or exceptions.
    /// </summary>
    /// <param name="desiredFileName">The desired file name.</param>
    /// <param name="reservedFileNames">The reserved file names.</param>
    /// <returns>A string value produced by this method.</returns>

    private static string MakeFileNameUnique(string desiredFileName, ISet<string> reservedFileNames)
    {
        if (!reservedFileNames.Contains(desiredFileName))
        {
            return desiredFileName;
        }

        var extension = Path.GetExtension(desiredFileName);
        var stem = Path.GetFileNameWithoutExtension(desiredFileName);
        var suffix = 1;

        string candidate;
        do
        {
            candidate = stem + "~" + suffix + extension;
            suffix++;
        }
        while (reservedFileNames.Contains(candidate));

        return candidate;
    }

    /// <summary>
    /// Returns the identifier text for base type and delegate declarations, throwing InvalidOperationException for unsupported member types.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <returns>A string value produced by this method.</returns>
    /// <exception cref="InvalidOperationException">Thrown when method validation or execution fails for this exception type.</exception>

    private static string GetIdentifier(MemberDeclarationSyntax member)
    {
        switch (member)
        {
            case BaseTypeDeclarationSyntax baseTypeDeclaration:
                return baseTypeDeclaration.Identifier.ValueText;

            case DelegateDeclarationSyntax delegateDeclaration:
                return delegateDeclaration.Identifier.ValueText;

            default:
                throw new InvalidOperationException("Unsupported top-level type declaration.");
        }
    }

    /// <summary>
    /// Returns the list of type parameter names from a type or delegate declaration, or an empty array if none exist, with no side effects.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <returns>A IReadOnlyList&lt;string&gt; value produced by this method.</returns>

    private static IReadOnlyList<string> GetTypeParameterNames(MemberDeclarationSyntax member)
    {
        switch (member)
        {
            case TypeDeclarationSyntax typeDeclaration when typeDeclaration.TypeParameterList != null:
                return typeDeclaration.TypeParameterList.Parameters.Select(x => x.Identifier.ValueText).ToList();

            case DelegateDeclarationSyntax delegateDeclaration when delegateDeclaration.TypeParameterList != null:
                return delegateDeclaration.TypeParameterList.Parameters.Select(x => x.Identifier.ValueText).ToList();

            default:
                return Array.Empty<string>();
        }
    }
}
