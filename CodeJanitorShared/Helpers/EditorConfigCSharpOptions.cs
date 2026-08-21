using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeJanitor.Helpers;

internal sealed class EditorConfigCSharpOptions
{
    internal bool? SortSystemDirectivesFirst { get; set; }

    internal bool? SeparateImportDirectiveGroups { get; set; }

    internal bool? TrimTrailingWhitespace { get; set; }

    internal bool? InsertFinalNewline { get; set; }

    internal string IndentStyle { get; set; }

    internal int? IndentSize { get; set; }

    internal int? TabWidth { get; set; }
}
