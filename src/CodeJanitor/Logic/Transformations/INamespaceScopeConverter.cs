namespace CodeJanitor.Logic.Transformations;

/// <summary>
/// Converts C# namespace declarations between block-scoped and file-scoped forms.
/// </summary>

public interface INamespaceScopeConverter
{
    /// <summary>
    /// Converts a single top-level block-scoped namespace to a file-scoped namespace. The body is dedented by one
    /// level; lines starting inside a multi-line string literal, text disabled by <c>#if</c> or a multi-line comment
    /// keep their exact text. Comments around the braces are kept (those on the closing brace line on a line of their
    /// own after the body), and so is everything after the closing brace.
    /// </summary>
    /// <param name="source">The full C# source text.</param>
    /// <returns>
    /// The converted source, or the original source unchanged when conversion is not applicable (no namespace,
    /// multiple namespaces, nested namespaces, a type or statement outside the namespace, already file-scoped, a
    /// directive between the namespace name and its body, a conditional directive block crossing a namespace brace, or
    /// text disabled by <c>#if</c> after the namespace).
    /// </returns>

    string ConvertToFileScoped(string source);

    /// <summary>
    /// Converts a single top-level file-scoped namespace to a block-scoped namespace. The body, from the line after the
    /// declaration to the end of the file, moves between braces and is indented by one level (the configured
    /// indentation, otherwise in the style of the body); lines starting inside a multi-line string literal, text
    /// disabled by <c>#if</c> or a multi-line comment keep their exact text.
    /// </summary>
    /// <param name="source">The full C# source text.</param>
    /// <returns>
    /// The converted source, or the original source unchanged when conversion is not applicable (no namespace,
    /// more than one namespace, a block-scoped namespace, a namespace declared inside <c>#if</c>, or syntax errors).
    /// </returns>

    string ConvertToBlockScoped(string source);

    /// <summary>
    /// Determines whether the specified source contains more than one namespace declaration
    /// (multiple top-level namespaces, and/or a namespace nested inside another), which makes
    /// conversion to a file-scoped namespace inapplicable.
    /// </summary>
    /// <param name="source">The full C# source text.</param>
    /// <returns>True if the source contains more than one namespace declaration.</returns>

    bool HasMultipleNamespaces(string source);
}
