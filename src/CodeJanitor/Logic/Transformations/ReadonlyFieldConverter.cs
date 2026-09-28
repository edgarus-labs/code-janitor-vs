using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Adds the <c>readonly</c> modifier to fields only when provably safe to do so from a single
/// syntax tree, without a full solution-wide semantic analysis (see ADR-0007).
/// </summary>
/// <remarks>
/// Scope is intentionally conservative: only <c>private</c> fields of non-partial types with a
/// single declarator are considered, and only when every write to the field occurs directly in
/// the declaring type's own constructor (instance fields) or static constructor (static
/// fields) through the instance under construction - never in a regular method, accessor, local
/// function, or nested lambda, since those could execute after construction. Fields named in
/// code excluded by a preprocessor directive, fixed-size buffers, and fields with a method call on
/// them whose type may be a mutable struct are left alone. Pure logic, unit-testable without
/// Visual Studio.
/// </remarks>
public sealed class ReadonlyFieldConverter : IFieldMutabilityConverter, ISourceTransformation
{
    /// <inheritdoc />
    public string Name => "Readonly Field";

    /// <inheritdoc />
    public string Apply(string source) => AddReadonlyWhenSafe(source);

    /// <inheritdoc />
    public string AddReadonlyWhenSafe(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var fieldsToConvert = new List<FieldDeclarationSyntax>();

        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (typeDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            {
                continue;
            }

            foreach (var fieldDecl in typeDecl.Members.OfType<FieldDeclarationSyntax>())
            {
                if (IsSafeToMakeReadonly(typeDecl, fieldDecl))
                {
                    fieldsToConvert.Add(fieldDecl);
                }
            }
        }

        if (fieldsToConvert.Count == 0)
        {
            return source;
        }

        var newRoot = root.ReplaceNodes(fieldsToConvert, (original, _) => WithReadonlyModifier(original));

        return newRoot.ToFullString();
    }

    /// <summary>
    /// FieldAccessKind represents the access pattern for a field, distinguishing between no access, direct access, or access through a sub-member.
    /// </summary>
    private enum FieldAccessKind
    {
        None,
        Direct,
        SubMember
    }

    /// <summary>
    /// Checks whether a single private field can be made readonly by rejecting fields with multiple variables, readonly/const/volatile modifiers, or non-private accessibility, and scanning the containing type for any writes via assignments, increment/decrement operations, or ref/out arguments (including across object sub-members), returning true only if no such unsafe writes are found.
    /// </summary>
    /// <param name="typeDecl">The type decl.</param>
    /// <param name="fieldDecl">The field decl.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool IsSafeToMakeReadonly(TypeDeclarationSyntax typeDecl, FieldDeclarationSyntax fieldDecl)
    {
        if (fieldDecl.Declaration.Variables.Count != 1)
        {
            return false;
        }

        var modifiers = fieldDecl.Modifiers;
        if (modifiers.Any(m => m.IsKind(SyntaxKind.ReadOnlyKeyword) ||
                                m.IsKind(SyntaxKind.ConstKeyword) ||
                                m.IsKind(SyntaxKind.VolatileKeyword) ||
                                m.IsKind(SyntaxKind.FixedKeyword)))
        {
            return false;
        }

        // Only private (explicit or implicit) fields: external writes to
        // public/internal/protected fields cannot be ruled out from a single file.
        if (modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) ||
                                m.IsKind(SyntaxKind.InternalKeyword) ||
                                m.IsKind(SyntaxKind.ProtectedKeyword)))
        {
            return false;
        }

        var isStatic = modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
        var fieldName = fieldDecl.Declaration.Variables[0].Identifier.Text;
        var declaringTypeName = typeDecl.Identifier.Text;

        var scopeNodes = typeDecl.DescendantNodes().ToList();

        // Any ref or out argument (or ref expression) referencing this field or its sub-members
        // makes it unsafe to add readonly (both in methods and in constructors).
        foreach (var argument in scopeNodes.OfType<ArgumentSyntax>())
        {
            if (argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
            {
                if (GetFieldAccessKind(argument.Expression, fieldName, declaringTypeName) != FieldAccessKind.None)
                {
                    return false;
                }
            }
        }

        foreach (var refExpression in scopeNodes.OfType<RefExpressionSyntax>())
        {
            if (GetFieldAccessKind(refExpression.Expression, fieldName, declaringTypeName) != FieldAccessKind.None)
            {
                return false;
            }
        }
        foreach (var prefix in scopeNodes.OfType<PrefixUnaryExpressionSyntax>())
        {
            if (prefix.IsKind(SyntaxKind.AddressOfExpression) &&
                GetFieldAccessKind(prefix.Operand, fieldName, declaringTypeName) != FieldAccessKind.None)
            {
                return false;
            }
        }

        var writes = new List<KeyValuePair<SyntaxNode, List<ExpressionSyntax>>>();

        void AddWrite(SyntaxNode writeNode, ExpressionSyntax target)
        {
            var targets = new List<ExpressionSyntax>();
            CollectWrittenTargets(target, fieldName, declaringTypeName, targets);
            if (targets.Count > 0)
            {
                writes.Add(new KeyValuePair<SyntaxNode, List<ExpressionSyntax>>(writeNode, targets));
            }
        }

        foreach (var assignment in scopeNodes.OfType<AssignmentExpressionSyntax>())
        {
            AddWrite(assignment, assignment.Left);
        }

        foreach (var forEach in scopeNodes.OfType<ForEachVariableStatementSyntax>())
        {
            AddWrite(forEach, forEach.Variable);
        }

        foreach (var unary in scopeNodes.OfType<PostfixUnaryExpressionSyntax>())
        {
            if (unary.IsKind(SyntaxKind.PostIncrementExpression) || unary.IsKind(SyntaxKind.PostDecrementExpression))
            {
                AddWrite(unary, unary.Operand);
            }
        }

        foreach (var unary in scopeNodes.OfType<PrefixUnaryExpressionSyntax>())
        {
            if (unary.IsKind(SyntaxKind.PreIncrementExpression) || unary.IsKind(SyntaxKind.PreDecrementExpression))
            {
                AddWrite(unary, unary.Operand);
            }
        }

        foreach (var write in writes)
        {
            if (!IsWriteInMatchingConstructor(write.Key, write.Value, fieldName, typeDecl, isStatic))
            {
                return false;
            }
        }

        // A method called on a readonly field of a mutable struct type runs on a defensive copy, silently dropping the
        // mutation, so calls are only accepted when the field's type is known not to be a mutable struct.
        if (!IsKnownNotMutableStruct(fieldDecl.Declaration.Type, typeDecl.SyntaxTree.GetRoot()))
        {
            foreach (var invocation in scopeNodes.OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is MemberAccessExpressionSyntax calledMember &&
                    GetFieldAccessKind(calledMember.Expression, fieldName, declaringTypeName) != FieldAccessKind.None)
                {
                    return false;
                }
            }
        }

        return !IsMentionedInInactiveCode(typeDecl, fieldName);
    }

    /// <summary>
    /// Strips all outer parentheses from the given expression, returning the innermost non-parenthesized ExpressionSyntax.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <returns>A ExpressionSyntax value produced by this method.</returns>
    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax paren)
        {
            expression = paren.Expression;
        }

        return expression;
    }

    /// <summary>
    /// Determines how a field is accessed within a given expression by recursively unwrapping parentheses and inspecting identifier, member-access, element-access, and conditional-access syntax nodes, returning `Direct` for a top-level match on the field name, `SubMember` when the match is nested deeper in the expression chain, and `None` otherwise.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <param name="fieldName">The field name.</param>
    /// <param name="declaringTypeName">The declaring type name.</param>
    /// <returns>A FieldAccessKind value produced by this method.</returns>
    private static FieldAccessKind GetFieldAccessKind(ExpressionSyntax expression, string fieldName, string declaringTypeName)
    {
        expression = UnwrapParentheses(expression);
        if (expression is null)
        {
            return FieldAccessKind.None;
        }

        if (expression is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.Text == fieldName ? FieldAccessKind.Direct : FieldAccessKind.None;
        }

        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Name.Identifier.Text == fieldName)
            {
                return FieldAccessKind.Direct;
            }

            var leftKind = GetFieldAccessKind(memberAccess.Expression, fieldName, declaringTypeName);
            if (leftKind != FieldAccessKind.None)
            {
                return FieldAccessKind.SubMember;
            }

            return FieldAccessKind.None;
        }

        if (expression is ElementAccessExpressionSyntax elementAccess)
        {
            var leftKind = GetFieldAccessKind(elementAccess.Expression, fieldName, declaringTypeName);
            if (leftKind != FieldAccessKind.None)
            {
                return FieldAccessKind.SubMember;
            }

            return FieldAccessKind.None;
        }

        if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
        {
            var leftKind = GetFieldAccessKind(conditionalAccess.Expression, fieldName, declaringTypeName);
            if (leftKind != FieldAccessKind.None)
            {
                return FieldAccessKind.SubMember;
            }

            return FieldAccessKind.None;
        }

        return FieldAccessKind.None;
    }

    /// <summary>
    /// Determines whether a write occurs inside a constructor of the given type with matching staticness, through the instance under construction, by walking ancestor nodes, returning false if any non-constructor member boundary or type boundary is reached first.
    /// </summary>
    /// <param name="writeNode">The write node.</param>
    /// <param name="writtenTargets">The accesses of the field written by the node.</param>
    /// <param name="fieldName">The field name.</param>
    /// <param name="typeDecl">The type decl.</param>
    /// <param name="isStatic">The is static.</param>
    /// <returns>A bool value produced by this method.</returns>
    private static bool IsWriteInMatchingConstructor(SyntaxNode writeNode, IReadOnlyList<ExpressionSyntax> writtenTargets, string fieldName, TypeDeclarationSyntax typeDecl, bool isStatic)
    {
        foreach (var ancestor in writeNode.Ancestors())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax _:
                case LocalFunctionStatementSyntax _:
                case MethodDeclarationSyntax _:
                case AccessorDeclarationSyntax _:
                case DestructorDeclarationSyntax _:
                case OperatorDeclarationSyntax _:
                case ConversionOperatorDeclarationSyntax _:
                    // Any of these boundaries reached before a constructor means the write
                    // could execute after construction (or in an unrelated member) - unsafe.
                    return false;

                case ConstructorDeclarationSyntax constructor:
                    var constructorIsStatic = constructor.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                    return ReferenceEquals(constructor.Parent, typeDecl) && constructorIsStatic == isStatic &&
                           writtenTargets.All(target => IsAccessThroughOwnInstance(target, fieldName, typeDecl.Identifier.Text, isStatic));

                case TypeDeclarationSyntax _:
                    // Reached the type boundary (e.g. a field initializer) without finding a
                    // constructor context - conservatively unsafe.
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds a readonly modifier with trailing space to the field declaration&apos;s modifiers and returns the updated syntax node, with no side effects.
    /// </summary>
    /// <param name="fieldDecl">The field decl.</param>
    /// <returns>A FieldDeclarationSyntax value produced by this method.</returns>
    private static FieldDeclarationSyntax WithReadonlyModifier(FieldDeclarationSyntax fieldDecl)
    {
        var readonlyToken = SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword).WithTrailingTrivia(SyntaxFactory.Space);

        if (fieldDecl.Modifiers.Count == 0)
        {
            // The indentation (and anything else leading the declaration after its attributes) belongs to the type;
            // move it in front of the new first token so the layout is kept.
            var type = fieldDecl.Declaration.Type;
            readonlyToken = readonlyToken.WithLeadingTrivia(type.GetLeadingTrivia());
            var newDeclaration = fieldDecl.Declaration.WithType(type.WithLeadingTrivia(SyntaxTriviaList.Empty));

            return fieldDecl.WithDeclaration(newDeclaration).WithModifiers(SyntaxFactory.TokenList(readonlyToken));
        }

        return fieldDecl.WithModifiers(fieldDecl.Modifiers.Add(readonlyToken));
    }

    /// <summary>
    /// Determines whether a written field access (possibly a sub-member or element of the field) reaches the field
    /// through the instance under construction: a bare name, <c>this.</c>, or (for a static field) the declaring
    /// type's name. Writes through any other receiver, including object and <c>with</c> initializers, target another
    /// object, which a readonly field forbids even in a constructor.
    /// </summary>
    private static bool IsAccessThroughOwnInstance(ExpressionSyntax expression, string fieldName, string declaringTypeName, bool isStatic)
    {
        expression = UnwrapParentheses(expression);
        switch (expression)
        {
            case IdentifierNameSyntax identifier when identifier.Identifier.Text == fieldName:
                return !(identifier.Parent is AssignmentExpressionSyntax assignment &&
                         assignment.Left == identifier &&
                         assignment.Parent is InitializerExpressionSyntax);

            case MemberAccessExpressionSyntax memberAccess when memberAccess.Name.Identifier.Text == fieldName:
                var receiver = UnwrapParentheses(memberAccess.Expression);
                return isStatic
                    ? receiver is IdentifierNameSyntax typeName && typeName.Identifier.Text == declaringTypeName
                    : receiver is ThisExpressionSyntax;

            case MemberAccessExpressionSyntax memberAccess:
                return IsAccessThroughOwnInstance(memberAccess.Expression, fieldName, declaringTypeName, isStatic);

            case ElementAccessExpressionSyntax elementAccess:
                return IsAccessThroughOwnInstance(elementAccess.Expression, fieldName, declaringTypeName, isStatic);

            default:
                return false;
        }
    }

    /// <summary>
    /// Collects the field accesses written by the target of an assignment or a deconstruction (including nested
    /// tuples) into <paramref name="targets" />.
    /// </summary>
    private static void CollectWrittenTargets(ExpressionSyntax target, string fieldName, string declaringTypeName, List<ExpressionSyntax> targets)
    {
        target = UnwrapParentheses(target);
        if (target is TupleExpressionSyntax tuple)
        {
            foreach (var argument in tuple.Arguments)
            {
                CollectWrittenTargets(argument.Expression, fieldName, declaringTypeName, targets);
            }

            return;
        }

        if (GetFieldAccessKind(target, fieldName, declaringTypeName) != FieldAccessKind.None)
        {
            targets.Add(target);
        }
    }

    /// <summary>
    /// Determines whether the field name occurs as an identifier in code excluded by a preprocessor directive
    /// (<c>#if</c>/<c>#elif</c>/<c>#else</c> branches inactive without symbols). Such code is compiled in other build
    /// configurations (for example <c>DEBUG</c>) and may write the field there.
    /// </summary>
    private static bool IsMentionedInInactiveCode(SyntaxNode scope, string fieldName)
    {
        foreach (var trivia in scope.DescendantTrivia(descendIntoTrivia: true))
        {
            if (!trivia.IsKind(SyntaxKind.DisabledTextTrivia))
            {
                continue;
            }

            if (SyntaxFactory.ParseTokens(trivia.ToString()).Any(t => t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText == fieldName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Well-known framework reference types whose instance methods are commonly called on fields. A method call on a
    /// readonly field of a mutable struct type runs on a defensive copy, so calls are only accepted on types known not
    /// to be mutable structs.
    /// </summary>
    private static readonly HashSet<string> KnownReferenceTypeNames = new HashSet<string>(System.StringComparer.Ordinal)
    {
        "Object", "String", "Array", "Delegate", "Action", "Func", "EventHandler", "Task", "Lazy", "Random", "Type",
        "List", "Dictionary", "HashSet", "SortedSet", "SortedList", "SortedDictionary", "LinkedList", "Queue", "Stack",
        "ConcurrentDictionary", "ConcurrentQueue", "ConcurrentStack", "ConcurrentBag", "BlockingCollection",
        "StringBuilder", "Stream", "MemoryStream", "StreamReader", "StreamWriter", "TextReader", "TextWriter",
        "Stopwatch", "Timer", "CancellationTokenSource", "SemaphoreSlim", "ManualResetEventSlim", "HttpClient",
    };

    /// <summary>
    /// Determines whether the declared type is syntactically known not to be a mutable struct: a predefined type, an
    /// array or pointer, a nullable value, a well-known framework reference type, a type declared in this file as a
    /// class, interface, record class, delegate, enum or <c>readonly struct</c>, or a name following the interface
    /// naming convention (<c>I</c> followed by an upper-case letter).
    /// </summary>
    private static bool IsKnownNotMutableStruct(TypeSyntax type, SyntaxNode root)
    {
        switch (type)
        {
            case PredefinedTypeSyntax _:
            case ArrayTypeSyntax _:
            case PointerTypeSyntax _:
            case NullableTypeSyntax _:
                return true;
        }

        SimpleNameSyntax name;
        switch (type)
        {
            case SimpleNameSyntax simple:
                name = simple;
                break;
            case QualifiedNameSyntax qualified:
                name = qualified.Right;
                break;
            case AliasQualifiedNameSyntax aliasQualified:
                name = aliasQualified.Name;
                break;
            default:
                return false;
        }

        var text = name.Identifier.ValueText;
        if (KnownReferenceTypeNames.Contains(text) ||
            (text.Length > 1 && text[0] == 'I' && char.IsUpper(text[1])))
        {
            return true;
        }

        var declarations = root.DescendantNodes(n => !(n is BlockSyntax))
            .Where(n => n is BaseTypeDeclarationSyntax || n is DelegateDeclarationSyntax)
            .Where(n => (n is BaseTypeDeclarationSyntax t ? t.Identifier : ((DelegateDeclarationSyntax)n).Identifier).ValueText == text)
            .ToList();

        return declarations.Count > 0 && declarations.All(declaration =>
            !(declaration is StructDeclarationSyntax || declaration.IsKind(SyntaxKind.RecordStructDeclaration)) ||
            ((TypeDeclarationSyntax)declaration).Modifiers.Any(m => m.IsKind(SyntaxKind.ReadOnlyKeyword)));
    }
}
