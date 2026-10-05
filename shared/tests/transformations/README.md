# Shared transformation test corpus

Behavior-level fixtures executed by **both** implementations of Code Janitor:

- Visual Studio extension (C#/Roslyn): `CodeJanitor.UnitTests/Cleaning/SharedTransformationCorpusTests.cs`
- VS Code extension (TypeScript): `test/sharedTransformationCorpus.test.ts` in `code-janitor-vscode`

## Fixture format

Each file is a JSON object:

```json
{
  "name": "regions-removed-by-default",
  "description": "Why this case matters.",
  "input": "#region Fields\nclass C { }\n#endregion\n",
  "settings": { "insertBlankLinePadding": false, "removeRegions": true },
  "mustContain": ["class C { }"],
  "mustNotContain": ["#region", "#endregion"]
}
```

Fields:

| Field | Required | Meaning |
|---|---|---|
| `name` | yes | Stable kebab-case identifier used in failure messages. |
| `description` | no | Rationale and known limitations. |
| `input` | yes | C# source text. |
| `settings` | yes | Overrides on top of the default cleanup settings. Keys use the shared camelCase `.codejanitor` schema; the group alias `insertBlankLinePadding` fans out to all group members, and `insertExplicitAccessModifiers` fans out in VS Code and is the single accessibility-modifier setting in Visual Studio. Use `newlines: "lf"` to normalize input/output to `\n` before running. |
| `mustContain` | yes | Substrings that must appear in the pipeline output. |
| `mustNotContain` | no | Substrings that must not appear in the pipeline output. |

## Rules for authors

1. Fixtures must pass on **both** platforms. Express behavior, not implementation quirks.
2. Prefer the default settings plus the smallest override set needed for the case.
3. Disable unrelated noise when a case targets one transformation (e.g. `insertBlankLinePadding: false` when asserting namespace conversion).
4. Newlines default to CRLF (the `.gitattributes` normalization for text files); set `newlines: "lf"` for cases that are sensitive to line endings.
5. When the implementations legitimately diverge (Roslyn semantic vs. lexical heuristics), do not add a fixture for that behavior - document the divergence instead.

## Divergences not in the corpus

The Visual Studio extension applies these steps through Roslyn's own analyzers and code fixes in its diagnostic cleanup, not in the text pipeline the corpus runs: `var` when the type is apparent, explicit accessibility modifiers, file-scoped namespaces, using directive placement, collection expressions, expression-bodied lambdas, inlined `out` variables and (for C#) multiple blank lines.

The accessibility-modifier insertion itself is therefore not in the corpus; only the disabled case (`explicit-access-modifiers-disabled.json`) is shared.
