using EnvDTE;
using EnvDTE80;
using System;

namespace CodeJanitor.Model.CodeItems;

/// <summary>
/// The representation of a code interface.
/// </summary>

public class CodeItemInterface : BaseCodeItemElementParent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CodeItemInterface" /> class.
    /// </summary>

    public CodeItemInterface()
    {
        _Access = LazyTryDefault(
            () => CodeInterface?.Access ?? vsCMAccess.vsCMAccessPublic);

        _Attributes = LazyTryDefault(
            () => CodeInterface?.Attributes);

        _DocComment = LazyTryDefault(
            () => CodeInterface?.DocComment);

        _Namespace = LazyTryDefault(
            () => CodeInterface?.Namespace?.Name);

        _TypeString = new Lazy<string>(
            () => "interface");
    }

    /// <summary>
    /// Gets the kind.
    /// </summary>
    public override KindCodeItem Kind => KindCodeItem.Interface;

    /// <summary>
    /// Gets or sets the underlying VSX CodeInterface.
    /// </summary>
    public CodeInterface2 CodeInterface { get; set; }
}
