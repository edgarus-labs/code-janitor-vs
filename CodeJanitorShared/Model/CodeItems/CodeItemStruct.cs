using EnvDTE;
using EnvDTE80;
using System;

namespace CodeJanitor.Model.CodeItems;

/// <summary>
/// The representation of a code struct.
/// </summary>

public class CodeItemStruct : BaseCodeItemElementParent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CodeItemStruct" /> class.
    /// </summary>

    public CodeItemStruct()
    {
        _Access = LazyTryDefault(
            () => CodeStruct?.Access ?? vsCMAccess.vsCMAccessPublic);

        _Attributes = LazyTryDefault(
            () => CodeStruct?.Attributes);

        _DocComment = LazyTryDefault(
            () => CodeStruct?.DocComment);

        _Namespace = LazyTryDefault(
            () => CodeStruct?.Namespace?.Name);

        _TypeString = new Lazy<string>(
            () => "struct");
    }

    /// <summary>
    /// Gets the kind.
    /// </summary>
    public override KindCodeItem Kind => KindCodeItem.Struct;

    /// <summary>
    /// Gets or sets the underlying VSX CodeStruct.
    /// </summary>
    public CodeStruct2 CodeStruct { get; set; }
}