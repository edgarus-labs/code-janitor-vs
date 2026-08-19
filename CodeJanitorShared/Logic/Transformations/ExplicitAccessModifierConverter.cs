using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodeJanitor.Properties;
using System.Linq;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Inserts the default access modifier on declarations that omit it, governed per-kind by
/// the Cleaning_InsertExplicitAccessModifiersOn* settings. Pure Roslyn; unit-testable without
/// Visual Studio.
/// </summary>

public class ExplicitAccessModifierConverter : ISourceTransformation
{
    public string Name => "Explicit Access Modifiers";

    public string Apply(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var rewriter = new AccessModifierRewriter();
        var newRoot = rewriter.Visit(root);

        return newRoot.ToFullString();
    }

    private sealed class AccessModifierRewriter : CSharpSyntaxRewriter
    {
        // ── Type declarations ──────────────────────────────────────────────────

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);
            if (!Settings.Default.Cleaning_InsertExplicitAccessModifiersOnClasses) return visited;
            if (HasAccessModifier(visited.Modifiers)) return visited;
            if (HasModifier(visited.Modifiers, SyntaxKind.PartialKeyword)) return visited;
            var leading = FirstLeadingTrivia(visited.Modifiers, visited.Keyword);
            var newMods = PrependModifier(visited.Modifiers, DefaultAccessFor(node), leading, out _);

            return visited.WithModifiers(newMods).WithKeyword(visited.Keyword.WithLeadingTrivia(SyntaxTriviaList.Empty));
        }

        public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node)
        {
            var visited = (StructDeclarationSyntax)base.VisitStructDeclaration(node);
            if (!Settings.Default.Cleaning_InsertExplicitAccessModifiersOnStructs) return visited;
            if (HasAccessModifier(visited.Modifiers)) return visited;
            if (HasModifier(visited.Modifiers, SyntaxKind.PartialKeyword)) return visited;
            var leading = FirstLeadingTrivia(visited.Modifiers, visited.Keyword);
            var newMods = PrependModifier(visited.Modifiers, DefaultAccessFor(node), leading, out _);

            return visited.WithModifiers(newMods).WithKeyword(visited.Keyword.WithLeadingTrivia(SyntaxTriviaList.Empty));
        }

        public override SyntaxNode VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            var visited = (InterfaceDeclarationSyntax)base.VisitInterfaceDeclaration(node);
            if (!Settings.Default.Cleaning_InsertExplicitAccessModifiersOnInterfaces) return visited;
            if (HasAccessModifier(visited.Modifiers)) return visited;
            if (HasModifier(visited.Modifiers, SyntaxKind.PartialKeyword)) return visited;
            var leading = FirstLeadingTrivia(visited.Modifiers, visited.Keyword);
            var newMods = PrependModifier(visited.Modifiers, DefaultAccessFor(node), leading, out _);

            return visited.WithModifiers(newMods).WithKeyword(visited.Keyword.WithLeadingTrivia(SyntaxTriviaList.Empty));
        }

        public override SyntaxNode VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            var visited = (EnumDeclarationSyntax)base.VisitEnumDeclaration(node);
            if (!Settings.Default.Cleaning_InsertExplicitAccessModifiersOnEnumerations) return visited;
            if (HasAccessModifier(visited.Modifiers)) return visited;
            var leading = FirstLeadingTrivia(visited.Modifiers, visited.EnumKeyword);
            var newMods = PrependModifier(visited.Modifiers, DefaultAccessFor(node), leading, out _);

            return visited.WithModifiers(newMods).WithEnumKeyword(visited.EnumKeyword.WithLeadingTrivia(SyntaxTriviaList.Empty));
        }

        public override SyntaxNode VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            var visited = (RecordDeclarationSyntax)base.VisitRecordDeclaration(node);
            if (!Settings.Default.Cleaning_InsertExplicitAccessModifiersOnClasses) return visited;
            if (HasAccessModifier(visited.Modifiers)) return visited;
            if (HasModifier(visited.Modifiers, SyntaxKind.PartialKeyword)) return visited;
            var leading = FirstLeadingTrivia(visited.Modifiers, visited.ClassOrStructKeyword);
            var newMods = PrependModifier(visited.Modifiers, DefaultAccessFor(node), leading, out _);

            return visited.WithModifiers(newMods).WithClassOrStructKeyword(visited.ClassOrStructKeyword.WithLeadingTrivia(SyntaxTriviaList.Empty));
        }

        public override SyntaxNode VisitDelegateDeclaration(DelegateDeclarationSyntax node)
        {
            var visited = (DelegateDeclarationSyntax)base.VisitDelegateDeclaration(node);
            if (!Settings.Default.Cleaning_InsertExplicitAccessModifiersOnDelegates) return visited;
            if (HasAccessModifier(visited.Modifiers)) return visited;
            var leading = FirstLeadingTrivia(visited.Modifiers, visited.DelegateKeyword);
            var newMods = PrependModifier(visited.Modifiers, DefaultAccessFor(node), leading, out _);

            return visited.WithModifiers(newMods).WithDelegateKeyword(visited.DelegateKeyword.WithLeadingTrivia(SyntaxTriviaList.Empty));
        }

        // ── Members ────────────────────────────────────────────────────────────

        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            var visited = (FieldDeclarationSyntax)base.VisitFieldDeclaration(node);
            if (!Settings.Default.Cleaning_InsertExplicitAccessModifiersOnFields) return visited;
            if (!(node.Parent is TypeDeclarationSyntax)) return visited;
            if (HasAccessModifier(visited.Modifiers)) return visited;
            if (HasModifier(visited.Modifiers, SyntaxKind.FixedKeyword)) return visited;
            var leading = FirstLeadingTrivia(visited.Modifiers, visited.Declaration.Type);
            var newMods = PrependModifier(visited.Modifiers, SyntaxKind.PrivateKeyword, leading, out _);

            return visited.WithModifiers(newMods).WithDeclaration(
                visited.Declaration.WithType(visited.Declaration.Type.WithLeadingTrivia(SyntaxTriviaList.Empty)));
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);
            if (!Settings.Default.Cleaning_InsertExplicitAccessModifiersOnMethods) return visited;
            if (!(node.Parent is TypeDeclarationSyntax parentType)) return visited;
            if (parentType is InterfaceDeclarationSyntax) return visited;
            if (HasAccessModifier(visited.Modifiers)) return visited;
            if (HasModifier(visited.Modifiers, SyntaxKind.PartialKeyword)) return visited;
            if (visited.ExplicitInterfaceSpecifier != null) return visited;
            var leading = FirstLeadingTrivia(visited.Modifiers, visited.ReturnType);
            var newMods = PrependModifier(visited.Modifiers, SyntaxKind.PrivateKeyword, leading, out _);

            return visited.WithModifiers(newMods).WithReturnType(visited.ReturnType.WithLeadingTrivia(SyntaxTriviaList.Empty));
        }

        public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            var visited = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node);
            if (!Settings.Default.Cleaning_InsertExplicitAccessModifiersOnMethods) return visited;
            if (!(node.Parent is TypeDeclarationSyntax)) return visited;
            if (HasModifier(visited.Modifiers, SyntaxKind.StaticKeyword)) return visited;
            if (HasAccessModifier(visited.Modifiers)) return visited;
            var leading = FirstLeadingTrivia(visited.Modifiers, visited.Identifier);
            var newMods = PrependModifier(visited.Modifiers, SyntaxKind.PrivateKeyword, leading, out _);

            return visited.WithModifiers(newMods).WithIdentifier(visited.Identifier.WithLeadingTrivia(SyntaxTriviaList.Empty));
        }

        public override SyntaxNode VisitDestructorDeclaration(DestructorDeclarationSyntax node)
        {
            return base.VisitDestructorDeclaration(node);
        }

        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            var visited = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node);
            if (!Settings.Default.Cleaning_InsertExplicitAccessModifiersOnProperties) return visited;
            if (!(node.Parent is TypeDeclarationSyntax parentType)) return visited;
            if (parentType is InterfaceDeclarationSyntax) return visited;
            if (HasAccessModifier(visited.Modifiers)) return visited;
            if (visited.ExplicitInterfaceSpecifier != null) return visited;
            var leading = FirstLeadingTrivia(visited.Modifiers, visited.Type);
            var newMods = PrependModifier(visited.Modifiers, SyntaxKind.PrivateKeyword, leading, out _);

            return visited.WithModifiers(newMods).WithType(visited.Type.WithLeadingTrivia(SyntaxTriviaList.Empty));
        }

        public override SyntaxNode VisitEventDeclaration(EventDeclarationSyntax node)
        {
            var visited = (EventDeclarationSyntax)base.VisitEventDeclaration(node);
            if (!Settings.Default.Cleaning_InsertExplicitAccessModifiersOnEvents) return visited;
            if (!(node.Parent is TypeDeclarationSyntax parentType)) return visited;
            if (parentType is InterfaceDeclarationSyntax) return visited;
            if (HasAccessModifier(visited.Modifiers)) return visited;
            if (visited.ExplicitInterfaceSpecifier != null) return visited;
            var leading = FirstLeadingTrivia(visited.Modifiers, visited.EventKeyword);
            var newMods = PrependModifier(visited.Modifiers, SyntaxKind.PrivateKeyword, leading, out _);

            return visited.WithModifiers(newMods).WithEventKeyword(visited.EventKeyword.WithLeadingTrivia(SyntaxTriviaList.Empty));
        }

        public override SyntaxNode VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
        {
            var visited = (EventFieldDeclarationSyntax)base.VisitEventFieldDeclaration(node);
            if (!Settings.Default.Cleaning_InsertExplicitAccessModifiersOnEvents) return visited;
            if (!(node.Parent is TypeDeclarationSyntax parentType)) return visited;
            if (parentType is InterfaceDeclarationSyntax) return visited;
            if (HasAccessModifier(visited.Modifiers)) return visited;
            var leading = FirstLeadingTrivia(visited.Modifiers, visited.EventKeyword);
            var newMods = PrependModifier(visited.Modifiers, SyntaxKind.PrivateKeyword, leading, out _);

            return visited.WithModifiers(newMods).WithEventKeyword(visited.EventKeyword.WithLeadingTrivia(SyntaxTriviaList.Empty));
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static bool HasAccessModifier(SyntaxTokenList modifiers)
        {
            return modifiers.Any(m =>
                m.IsKind(SyntaxKind.PublicKeyword) ||
                m.IsKind(SyntaxKind.InternalKeyword) ||
                m.IsKind(SyntaxKind.ProtectedKeyword) ||
                m.IsKind(SyntaxKind.PrivateKeyword));
        }

        private static bool HasModifier(SyntaxTokenList modifiers, SyntaxKind kind)
        {
            return modifiers.Any(m => m.IsKind(kind));
        }

        /// <summary>Returns the default implicit access modifier for a declaration in its current context.</summary>

        private static SyntaxKind DefaultAccessFor(MemberDeclarationSyntax node)
        {
            return node.Parent is TypeDeclarationSyntax
                ? SyntaxKind.PrivateKeyword
                : SyntaxKind.InternalKeyword;
        }

        /// <summary>Returns the leading trivia that belongs on the new first token of a declaration.</summary>

        private static SyntaxTriviaList FirstLeadingTrivia(SyntaxTokenList modifiers, SyntaxToken fallback)
        {
            return modifiers.Count > 0 ? modifiers[0].LeadingTrivia : fallback.LeadingTrivia;
        }

        private static SyntaxTriviaList FirstLeadingTrivia(SyntaxTokenList modifiers, TypeSyntax fallback)
        {
            return modifiers.Count > 0 ? modifiers[0].LeadingTrivia : fallback.GetLeadingTrivia();
        }

        /// <summary>
        /// Builds a new modifier list with <paramref name="kind"/> prepended carrying
        /// <paramref name="leadingTrivia"/>.  Strips leading trivia from the formerly-first modifier.
        /// </summary>

        private static SyntaxTokenList PrependModifier(
            SyntaxTokenList existing,
            SyntaxKind kind,
            SyntaxTriviaList leadingTrivia,
            out SyntaxTriviaList strippedTrivia)
        {
            var newToken = SyntaxFactory.Token(leadingTrivia, kind, SyntaxFactory.TriviaList(SyntaxFactory.Space));

            if (existing.Count == 0)
            {
                strippedTrivia = SyntaxTriviaList.Empty;

                return SyntaxFactory.TokenList(newToken);
            }

            strippedTrivia = existing[0].LeadingTrivia;
            var strippedFirst = existing[0].WithLeadingTrivia(SyntaxTriviaList.Empty);

            return SyntaxFactory.TokenList(
                new[] { newToken, strippedFirst }.Concat(existing.Skip(1)));
        }
    }
}