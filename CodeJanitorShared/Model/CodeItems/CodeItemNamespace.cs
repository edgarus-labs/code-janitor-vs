using EnvDTE;
using System;

namespace CodeJanitor.Model.CodeItems;

/// <summary>
/// The representation of a code namespace.
/// </summary>

public class CodeItemNamespace : BaseCodeItemElementParent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CodeItemNamespace" /> class.
    /// </summary>

    public CodeItemNamespace()
    {
        _DocComment = LazyTryDefault(
            () => CodeNamespace?.DocComment);

        _TypeString = new Lazy<string>(
            () => "namespace");
    }

    /// <summary>
    /// Gets the kind.
    /// </summary>
    public override KindCodeItem Kind => KindCodeItem.Namespace;

    /// <summary>
    /// Gets or sets the underlying VSX CodeNamespace.
    /// </summary>
    public CodeNamespace CodeNamespace { get; set; }
}
