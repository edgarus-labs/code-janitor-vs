using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Helpers;

namespace CodeJanitor.UnitTests.Helpers
{
    [TestClass]
    public class EditorConfigHelperTests
    {
        [TestMethod]
        public void ApplyText_ShouldReadCSharpSectionSettings()
        {
            const string editorConfig = @"root = true

[*.cs]
trim_trailing_whitespace = true
insert_final_newline = true
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false
indent_style = space
indent_size = 4
tab_width = 4
";

            var options = new EditorConfigCSharpOptions();
            EditorConfigHelper.ApplyText(editorConfig, @"C:\Repo\File.cs", options);

            Assert.AreEqual(true, options.TrimTrailingWhitespace);
            Assert.AreEqual(true, options.InsertFinalNewline);
            Assert.AreEqual(true, options.SortSystemDirectivesFirst);
            Assert.AreEqual(false, options.SeparateImportDirectiveGroups);
            Assert.AreEqual("space", options.IndentStyle);
            Assert.AreEqual(4, options.IndentSize);
            Assert.AreEqual(4, options.TabWidth);
        }

        [TestMethod]
        public void ApplyText_ShouldIgnoreNonCSharpSections()
        {
            const string editorConfig = @"[*.vb]
trim_trailing_whitespace = true

[*.json]
insert_final_newline = true
";

            var options = new EditorConfigCSharpOptions();
            EditorConfigHelper.ApplyText(editorConfig, @"C:\Repo\File.cs", options);

            Assert.IsNull(options.TrimTrailingWhitespace);
            Assert.IsNull(options.InsertFinalNewline);
        }

        [TestMethod]
        public void ApplyText_ShouldApplyLastMatchingSection()
        {
            const string editorConfig = @"[*.cs]
trim_trailing_whitespace = false

[*.{cs,vb}]
trim_trailing_whitespace = true
insert_final_newline = true
dotnet_sort_system_directives_first = true
";

            var options = new EditorConfigCSharpOptions();
            EditorConfigHelper.ApplyText(editorConfig, @"C:\Repo\File.cs", options);

            Assert.AreEqual(true, options.TrimTrailingWhitespace);
            Assert.AreEqual(true, options.InsertFinalNewline);
            Assert.AreEqual(true, options.SortSystemDirectivesFirst);
        }
    }
}