using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using System;
using System.Threading;

namespace CodeJanitor.Model.CodeItems;

/// <summary>
/// A base class representation of all code items that have an underlying VSX CodeElement.
/// </summary>

public abstract class BaseCodeItemElement : BaseCodeItem
{
    /// <summary>
    /// The access.
    /// </summary>
    protected Lazy<vsCMAccess> _Access;

    /// <summary>
    /// The attributes.
    /// </summary>
    protected Lazy<CodeElements> _Attributes;

    /// <summary>
    /// The doc comment.
    /// </summary>
    protected Lazy<string> _DocComment;

    /// <summary>
    /// The is static.
    /// </summary>
    protected Lazy<bool> _IsStatic;

    /// <summary>
    /// The type string.
    /// </summary>
    protected Lazy<string> _TypeString;

    /// <summary>
    /// Abstract initialization code for <see cref="BaseCodeItemElement" />.
    /// </summary>

    protected BaseCodeItemElement()
    {
        _Access = new Lazy<vsCMAccess>();
        _Attributes = new Lazy<CodeElements>(() => null);
        _DocComment = new Lazy<string>(() => null);
        _IsStatic = new Lazy<bool>();
        _TypeString = new Lazy<string>(() => null);
    }

    /// <summary>
    /// Gets the start point adjusted for leading comments, may be null.
    /// </summary>

    public override EditPoint StartPoint
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            return CodeElement is not null ? GetStartPointAdjustedForComments(CodeElement.GetStartPoint()) : null;
        }
    }

    /// <summary>
    /// Gets the end point, may be null.
    /// </summary>

    public override EditPoint EndPoint
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            return CodeElement?.GetEndPoint().CreateEditPoint();
        }
    }

    /// <summary>
    /// Loads all lazy initialized values immediately.
    /// </summary>

    public override void LoadLazyInitializedValues()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        base.LoadLazyInitializedValues();

        var ac = Access;
        var at = Attributes;
        var dc = DocComment;
        var isS = IsStatic;
        var ts = TypeString;
    }

    /// <summary>
    /// Refreshes the cached position and name fields on this item.
    /// </summary>

    public override void RefreshCachedPositionAndName()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var startPoint = CodeElement.GetStartPoint();
        var endPoint = CodeElement.GetEndPoint();

        StartLine = startPoint.Line;
        StartOffset = startPoint.AbsoluteCharOffset;
        EndLine = endPoint.Line;
        EndOffset = endPoint.AbsoluteCharOffset;
        Name = CodeElement.Name;
    }

    /// <summary>
    /// Gets or sets the code element, may be null.
    /// </summary>
    public CodeElement CodeElement { get; set; }

    /// <summary>
    /// Gets the access level.
    /// </summary>
    public vsCMAccess Access => _Access.Value;

    /// <summary>
    /// Gets the attributes.
    /// </summary>
    public CodeElements Attributes => _Attributes.Value;

    /// <summary>
    /// Gets the doc comment.
    /// </summary>
    public string DocComment => _DocComment.Value;

    /// <summary>
    /// Gets a flag indicating if this instance is static.
    /// </summary>
    public bool IsStatic => _IsStatic.Value;

    /// <summary>
    /// Gets the type string.
    /// </summary>
    public string TypeString => _TypeString.Value;

    /// <summary>
    /// Creates a lazy initializer wrapping TryDefault around the specified function.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="func">The function to execute.</param>
    /// <returns>A lazy initializer for the specified function.</returns>

    protected static Lazy<T> LazyTryDefault<T>(Func<T> func)
    {
        return new Lazy<T>(() => TryDefault(func), LazyThreadSafetyMode.PublicationOnly);
    }

    /// <summary>
    /// Tries to execute the specified function on a background thread, returning the default of
    /// the type on error or timeout.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="func">The function to execute.</param>
    /// <returns>The result of the function, otherwise the default for the result type.</returns>

    protected static T TryDefault<T>(Func<T> func)
    {
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            OutputWindowHelper.ExceptionWriteLine($"TryDefault caught an exception on '{func}'", ex);

            return default;
        }
    }

    /// <summary>
    /// Gets a starting point adjusted for leading comments.
    /// </summary>
    /// <param name="originalPoint">The original point.</param>
    /// <returns>The adjusted starting point.</returns>

    private static EditPoint GetStartPointAdjustedForComments(TextPoint originalPoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var commentPrefix = CodeCommentHelper.GetCommentPrefix(originalPoint.Parent);
        var point = originalPoint.CreateEditPoint();

        while (point.Line > 1)
        {
            string text = point.GetLines(point.Line - 1, point.Line);

            if (RegexNullSafe.IsMatch(text, @"^\s*" + commentPrefix))
            {
                point.LineUp();
                point.StartOfLine();
            }
            else
            {
                break;
            }
        }

        return point;
    }
}
