using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

namespace CodeJanitor.Logic.Transformations
{
    /// <summary>
    /// Updates the namespace declaration in a C# file to match an expected namespace.
    /// </summary>
    public class NamespaceFixerConverter
    {
        public string FixNamespace(string source, string expectedNamespace)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(expectedNamespace))
            {
                return source;
            }

            var tree = CSharpSyntaxTree.ParseText(source);
            if (!(tree.GetRoot() is CompilationUnitSyntax root))
            {
                return source;
            }

            var namespaceDeclaration = GetTopLevelNamespace(root);
            if (namespaceDeclaration == null)
            {
                return source;
            }

            if (string.Equals(namespaceDeclaration.Name.ToString(), expectedNamespace, StringComparison.Ordinal))
            {
                return source;
            }

            var namespaceSpan = namespaceDeclaration.Name.Span;
            return source.Substring(0, namespaceSpan.Start)
                + expectedNamespace
                + source.Substring(namespaceSpan.End);
        }

        private static BaseNamespaceDeclarationSyntax GetTopLevelNamespace(CompilationUnitSyntax root)
        {
            var namespaces = root.DescendantNodes()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Where(namespaceDeclaration => namespaceDeclaration.Parent is CompilationUnitSyntax)
                .ToList();

            return namespaces.Count == 1 ? namespaces[0] : null;
        }
    }
}