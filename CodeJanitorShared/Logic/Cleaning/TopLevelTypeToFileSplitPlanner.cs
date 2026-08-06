using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeJanitor.Logic.Cleaning
{
    internal sealed class TopLevelTypeToFileSplitPlanner
    {
        internal sealed class PlannedFile
        {
            internal PlannedFile(string filePath, string content)
            {
                FilePath = filePath;
                Content = content;
            }

            internal string FilePath { get; }

            internal string Content { get; }
        }

        internal sealed class SplitPlan
        {
            internal SplitPlan(string updatedSource, IReadOnlyList<PlannedFile> newFiles)
            {
                UpdatedSource = updatedSource;
                NewFiles = newFiles;
            }

            internal string UpdatedSource { get; }

            internal IReadOnlyList<PlannedFile> NewFiles { get; }

            internal bool HasChanges => NewFiles.Count > 0;
        }

        internal SplitPlan CreatePlan(string source, string filePath)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(filePath))
            {
                return new SplitPlan(source, Array.Empty<PlannedFile>());
            }

            var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
            if (HasUnsupportedStructure(root) ||
                !TryGetContainerMembers(root, out var members) ||
                members.Any(x => x is BaseNamespaceDeclarationSyntax || x is GlobalStatementSyntax))
            {
                return new SplitPlan(source, Array.Empty<PlannedFile>());
            }

            var eligibleMembers = members.Where(IsEligibleTopLevelType).ToList();
            if (eligibleMembers.Count <= 1)
            {
                return new SplitPlan(source, Array.Empty<PlannedFile>());
            }

            var memberToKeep = ChooseMemberToKeep(eligibleMembers, Path.GetFileName(filePath)) ?? eligibleMembers[0];
            var movedMembers = eligibleMembers.Where(x => x != memberToKeep).ToList();
            if (movedMembers.Count == 0)
            {
                return new SplitPlan(source, Array.Empty<PlannedFile>());
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

        internal static string BuildTypeFileName(MemberDeclarationSyntax member)
        {
            return BuildTypeFileStem(member) + ".cs";
        }

        internal static string BuildTypeFileStem(MemberDeclarationSyntax member)
        {
            var identifier = GetIdentifier(member);
            var typeParameters = GetTypeParameterNames(member);

            return typeParameters.Count == 0
                ? identifier
                : identifier + "{" + string.Join(",", typeParameters) + "}";
        }

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

        private static bool HasPartialModifier(SyntaxTokenList modifiers)
        {
            return modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword));
        }

        private static MemberDeclarationSyntax ChooseMemberToKeep(IEnumerable<MemberDeclarationSyntax> eligibleMembers, string originalFileName)
        {
            return eligibleMembers.FirstOrDefault(x => string.Equals(BuildTypeFileName(x), originalFileName, StringComparison.OrdinalIgnoreCase));
        }

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
}
