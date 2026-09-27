using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// The Roslyn code-style options Code Janitor can apply when .editorconfig does not enforce them, and the format of
/// the Visual Studio setting that stores the enabled ones. Naming rules (<c>dotnet_naming_*</c>) are not included:
/// they are configured in .editorconfig only.
/// </summary>
internal static class CodeStyleRules
{
    private const string Modifiers = "Modifiers";
    private const string Blocks = "Blocks";
    private const string ExpressionBodies = "Expression-bodied members";
    private const string PatternMatching = "Pattern matching";
    private const string NullChecking = "Null checking";
    private const string ModernExpressions = "Modern expressions";
    private const string Qualification = "'this.' qualification";
    private const string PredefinedTypes = "Language keywords vs. framework type names";
    private const string Parentheses = "Parentheses";

    private static readonly string[] Boolean = { "true", "false" };
    private static readonly string[] ExpressionBody = { "true", "false", "when_on_single_line" };
    private static readonly string[] ParenthesesValues = { "always_for_clarity", "never_if_unnecessary" };

    /// <summary>
    /// The modifiers accepted in <c>csharp_preferred_modifier_order</c>.
    /// </summary>
    private static readonly HashSet<string> KnownModifiers = new HashSet<string>(StringComparer.Ordinal)
    {
        "public", "private", "protected", "internal", "file", "static", "extern", "new", "virtual", "abstract",
        "sealed", "override", "readonly", "unsafe", "required", "volatile", "async", "partial", "const", "fixed", "ref",
    };

    /// <summary>
    /// Gets every rule, in the order shown in Options.
    /// </summary>
    internal static IReadOnlyList<CodeStyleRule> All { get; } = new[]
    {
        new CodeStyleRule(Modifiers, "csharp_preferred_modifier_order", "Order modifiers", new[] { "IDE0036" }, Array.Empty<string>(),
            "public,private,protected,internal,file,static,extern,new,virtual,abstract,sealed,override,readonly,unsafe,required,volatile,async",
            IsModifierOrder),
        new CodeStyleRule(Modifiers, "csharp_prefer_static_local_function", "Make local functions static", new[] { "IDE0062" }, Boolean, "true"),
        new CodeStyleRule(Modifiers, "csharp_prefer_static_anonymous_function", "Make anonymous functions static", new[] { "IDE0320" }, Boolean, "true"),
        new CodeStyleRule(Modifiers, "csharp_style_prefer_readonly_struct", "Make structs readonly", new[] { "IDE0250" }, Boolean, "true"),
        new CodeStyleRule(Modifiers, "csharp_style_prefer_readonly_struct_member", "Make struct members readonly", new[] { "IDE0251" }, Boolean, "true"),

        new CodeStyleRule(Blocks, "csharp_prefer_braces", "Use braces", new[] { "IDE0011" }, new[] { "true", "false", "when_multiline" }, "true"),
        new CodeStyleRule(Blocks, "csharp_prefer_simple_using_statement", "Use simple 'using' statements", new[] { "IDE0063" }, Boolean, "true"),
        new CodeStyleRule(Blocks, "csharp_style_prefer_method_group_conversion", "Convert lambdas to method groups", new[] { "IDE0200" }, Boolean, "true"),

        new CodeStyleRule(ExpressionBodies, "csharp_style_expression_bodied_methods", "Methods", new[] { "IDE0022" }, ExpressionBody, "false"),
        new CodeStyleRule(ExpressionBodies, "csharp_style_expression_bodied_constructors", "Constructors", new[] { "IDE0021" }, ExpressionBody, "false"),
        new CodeStyleRule(ExpressionBodies, "csharp_style_expression_bodied_operators", "Operators", new[] { "IDE0023", "IDE0024" }, ExpressionBody, "false"),
        new CodeStyleRule(ExpressionBodies, "csharp_style_expression_bodied_properties", "Properties", new[] { "IDE0025" }, ExpressionBody, "true"),
        new CodeStyleRule(ExpressionBodies, "csharp_style_expression_bodied_indexers", "Indexers", new[] { "IDE0026" }, ExpressionBody, "true"),
        new CodeStyleRule(ExpressionBodies, "csharp_style_expression_bodied_accessors", "Accessors", new[] { "IDE0027" }, ExpressionBody, "true"),
        new CodeStyleRule(ExpressionBodies, "csharp_style_expression_bodied_local_functions", "Local functions", new[] { "IDE0061" }, ExpressionBody, "false"),

        new CodeStyleRule(PatternMatching, "csharp_style_pattern_matching_over_is_with_cast_check", "Pattern matching over 'is' with cast", new[] { "IDE0020", "IDE0038" }, Boolean, "true"),
        new CodeStyleRule(PatternMatching, "csharp_style_pattern_matching_over_as_with_null_check", "Pattern matching over 'as' with null check", new[] { "IDE0019", "IDE0260" }, Boolean, "true"),
        new CodeStyleRule(PatternMatching, "csharp_style_prefer_pattern_matching", "Combine patterns", new[] { "IDE0078" }, Boolean, "true"),
        new CodeStyleRule(PatternMatching, "csharp_style_prefer_not_pattern", "Use 'not' pattern", new[] { "IDE0083" }, Boolean, "true"),
        new CodeStyleRule(PatternMatching, "csharp_style_prefer_extended_property_pattern", "Use extended property patterns", new[] { "IDE0170" }, Boolean, "true"),
        new CodeStyleRule(PatternMatching, "csharp_style_prefer_switch_expression", "Use switch expressions", new[] { "IDE0066" }, Boolean, "true"),

        new CodeStyleRule(NullChecking, "dotnet_style_coalesce_expression", "Use '??'", new[] { "IDE0029", "IDE0030", "IDE0270" }, Boolean, "true"),
        new CodeStyleRule(NullChecking, "dotnet_style_null_propagation", "Use '?.'", new[] { "IDE0031" }, Boolean, "true"),
        new CodeStyleRule(NullChecking, "csharp_style_throw_expression", "Use throw expressions", new[] { "IDE0016" }, Boolean, "true"),
        new CodeStyleRule(NullChecking, "csharp_style_conditional_delegate_call", "Invoke delegates with '?.'", new[] { "IDE1005" }, Boolean, "true"),

        new CodeStyleRule(ModernExpressions, "csharp_style_prefer_primary_constructors", "Use primary constructors", new[] { "IDE0290" }, Boolean, "true"),
        new CodeStyleRule(ModernExpressions, "csharp_style_implicit_object_creation_when_type_is_apparent", "Use target-typed 'new()'", new[] { "IDE0090" }, Boolean, "true"),
        new CodeStyleRule(ModernExpressions, "csharp_style_prefer_index_operator", "Use index operator '^'", new[] { "IDE0056" }, Boolean, "true"),
        new CodeStyleRule(ModernExpressions, "csharp_style_prefer_range_operator", "Use range operator '..'", new[] { "IDE0057" }, Boolean, "true"),
        new CodeStyleRule(ModernExpressions, "csharp_style_prefer_utf8_string_literals", "Use UTF-8 string literals", new[] { "IDE0230" }, Boolean, "true"),
        new CodeStyleRule(ModernExpressions, "csharp_style_prefer_tuple_swap", "Swap values with tuples", new[] { "IDE0180" }, Boolean, "true"),
        new CodeStyleRule(ModernExpressions, "csharp_style_deconstructed_variable_declaration", "Deconstruct variable declarations", new[] { "IDE0042" }, Boolean, "true"),
        new CodeStyleRule(ModernExpressions, "csharp_style_unused_value_assignment_preference", "Unused value assignments", new[] { "IDE0059" }, new[] { "discard_variable", "unused_local_variable" }, "discard_variable"),
        new CodeStyleRule(ModernExpressions, "csharp_prefer_simple_default_expression", "Use 'default' literal", new[] { "IDE0034" }, Boolean, "true"),
        new CodeStyleRule(ModernExpressions, "dotnet_style_prefer_auto_properties", "Use auto-properties", new[] { "IDE0032" }, Boolean, "true"),
        new CodeStyleRule(ModernExpressions, "dotnet_style_prefer_compound_assignment", "Use compound assignment", new[] { "IDE0054", "IDE0074" }, Boolean, "true"),
        new CodeStyleRule(ModernExpressions, "dotnet_style_prefer_simplified_boolean_expressions", "Simplify boolean expressions", new[] { "IDE0075" }, Boolean, "true"),
        new CodeStyleRule(ModernExpressions, "dotnet_style_prefer_simplified_interpolation", "Simplify interpolation", new[] { "IDE0071" }, Boolean, "true"),
        new CodeStyleRule(ModernExpressions, "dotnet_style_object_initializer", "Use object initializers", new[] { "IDE0017" }, Boolean, "true"),
        new CodeStyleRule(ModernExpressions, "dotnet_style_collection_initializer", "Use collection initializers", new[] { "IDE0028" }, Boolean, "true"),
        new CodeStyleRule(ModernExpressions, "dotnet_style_explicit_tuple_names", "Use explicit tuple names", new[] { "IDE0033" }, Boolean, "true"),
        new CodeStyleRule(ModernExpressions, "dotnet_style_prefer_inferred_tuple_names", "Use inferred tuple element names", new[] { "IDE0037" }, Boolean, "true"),
        new CodeStyleRule(ModernExpressions, "dotnet_style_prefer_inferred_anonymous_type_member_names", "Use inferred anonymous type member names", new[] { "IDE0037" }, Boolean, "true"),

        new CodeStyleRule(Qualification, "dotnet_style_qualification_for_field", "Fields", new[] { "IDE0003", "IDE0009" }, Boolean, "false"),
        new CodeStyleRule(Qualification, "dotnet_style_qualification_for_property", "Properties", new[] { "IDE0003", "IDE0009" }, Boolean, "false"),
        new CodeStyleRule(Qualification, "dotnet_style_qualification_for_method", "Methods", new[] { "IDE0003", "IDE0009" }, Boolean, "false"),
        new CodeStyleRule(Qualification, "dotnet_style_qualification_for_event", "Events", new[] { "IDE0003", "IDE0009" }, Boolean, "false"),

        new CodeStyleRule(PredefinedTypes, "dotnet_style_predefined_type_for_locals_parameters_members", "Locals, parameters and members", new[] { "IDE0049" }, Boolean, "true"),
        new CodeStyleRule(PredefinedTypes, "dotnet_style_predefined_type_for_member_access", "Member access expressions", new[] { "IDE0049" }, Boolean, "true"),

        new CodeStyleRule(Parentheses, "dotnet_style_parentheses_in_arithmetic_binary_operators", "Arithmetic operators", new[] { "IDE0047", "IDE0048" }, ParenthesesValues, "always_for_clarity"),
        new CodeStyleRule(Parentheses, "dotnet_style_parentheses_in_relational_binary_operators", "Relational operators", new[] { "IDE0047", "IDE0048" }, ParenthesesValues, "always_for_clarity"),
        new CodeStyleRule(Parentheses, "dotnet_style_parentheses_in_other_binary_operators", "Other binary operators", new[] { "IDE0047", "IDE0048" }, ParenthesesValues, "always_for_clarity"),
        new CodeStyleRule(Parentheses, "dotnet_style_parentheses_in_other_operators", "Other operators", new[] { "IDE0047", "IDE0048" }, ParenthesesValues, "never_if_unnecessary"),
    };

    private static readonly Dictionary<string, CodeStyleRule> ByKey = All.ToDictionary(rule => rule.Key, StringComparer.Ordinal);

    /// <summary>
    /// Gets the rule of the specified .editorconfig option.
    /// </summary>
    /// <param name="key">The .editorconfig option name.</param>
    /// <param name="rule">The rule, or null when the option is not a Code Janitor code-style rule.</param>
    /// <returns>True when the rule exists.</returns>
    internal static bool TryGet(string key, out CodeStyleRule rule)
    {
        rule = null;

        return key is not null && ByKey.TryGetValue(key, out rule);
    }

    /// <summary>
    /// Parses the Visual Studio setting that stores the enabled rules: <c>key=value</c> entries separated by
    /// <c>;</c>. Unknown keys and invalid values are ignored; for a repeated key the last entry wins.
    /// </summary>
    /// <param name="setting">The setting value.</param>
    /// <returns>The value of each enabled rule, keyed by .editorconfig option name.</returns>
    internal static IReadOnlyDictionary<string, string> ParseSetting(string setting)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in (setting ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = entry.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = entry.Substring(0, separatorIndex).Trim();
            var value = entry.Substring(separatorIndex + 1).Trim();
            if (TryGet(key, out var rule) && rule.IsValidValue(value))
            {
                values[key] = rule.Normalize(value);
            }
        }

        return values;
    }

    /// <summary>
    /// Formats the value of each enabled rule as the Visual Studio setting value, in catalog order.
    /// </summary>
    /// <param name="values">The value of each enabled rule, keyed by .editorconfig option name.</param>
    /// <returns>The setting value.</returns>
    internal static string FormatSetting(IReadOnlyDictionary<string, string> values) =>
        string.Join(";", All
            .Where(rule => values.TryGetValue(rule.Key, out var value) && rule.IsValidValue(value))
            .Select(rule => rule.Key + "=" + rule.Normalize(values[rule.Key])));

    /// <summary>
    /// Validates <c>csharp_preferred_modifier_order</c>: a comma-separated list of distinct known modifiers.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <returns>True when the value is valid.</returns>
    private static bool IsModifierOrder(string value)
    {
        var modifiers = value.Split(',').Select(modifier => modifier.Trim()).ToList();

        return modifiers.All(KnownModifiers.Contains) && modifiers.Distinct(StringComparer.Ordinal).Count() == modifiers.Count;
    }
}
