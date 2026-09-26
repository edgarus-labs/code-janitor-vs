using CodeJanitor.Helpers;
using CodeJanitor.Logic.Cleaning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeJanitor.UI.Dialogs.Options;

/// <summary>
/// The Options notes for Visual Studio settings that the open solution's .editorconfig overrides. Options are global
/// while .editorconfig applies per directory, so the notes describe a C# file at the solution root: the root
/// .editorconfig chain decides, and nested .editorconfig files below the solution directory are not considered.
/// The setting ↔ .editorconfig key mapping is the one the cleanup applies (<see cref="EffectiveCleanupSettings" />).
/// </summary>

public sealed class EditorConfigOverrideNotes
{
    /// <summary>
    /// The notes when no solution is open: no setting is annotated.
    /// </summary>
    internal static readonly EditorConfigOverrideNotes None = new EditorConfigOverrideNotes(new Dictionary<string, string>());

    /// <summary>
    /// The name of the C# file, never created, whose .editorconfig options represent the solution root.
    /// </summary>
    private const string ProbeFileName = "CodeJanitorOptionsProbe.cs";

    private readonly IReadOnlyDictionary<string, string> _notes;

    /// <summary>
    /// Initializes a new instance of the <see cref="EditorConfigOverrideNotes" /> class.
    /// </summary>
    /// <param name="notes">The notes keyed by Visual Studio setting property name.</param>

    private EditorConfigOverrideNotes(IReadOnlyDictionary<string, string> notes)
    {
        _notes = notes;
    }

    /// <summary>
    /// Gets the note for the specified setting, naming the overriding .editorconfig key and file.
    /// </summary>
    /// <param name="settingName">The Visual Studio setting property name, for example <c>Cleaning_ConvertToVarWhenApparent</c>.</param>
    /// <returns>The note, or null when .editorconfig does not override the setting.</returns>
    public string this[string settingName] =>
        settingName is not null && _notes.TryGetValue(settingName, out var note) ? note : null;

    /// <summary>
    /// Resolves the notes for the solution at the specified path. Unreadable or malformed configuration files are
    /// ignored; a blank or invalid path yields <see cref="None" />.
    /// </summary>
    /// <param name="solutionFullName">The full path of the open solution file, or null when no solution is open.</param>
    /// <returns>The notes for the solution.</returns>

    internal static EditorConfigOverrideNotes ForSolution(string solutionFullName)
    {
        if (string.IsNullOrWhiteSpace(solutionFullName))
        {
            return None;
        }

        string probePath;
        try
        {
            var solutionDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionFullName));
            if (string.IsNullOrEmpty(solutionDirectory))
            {
                return None;
            }

            probePath = Path.Combine(solutionDirectory, ProbeFileName);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
        {
            return None;
        }

        var notes = EffectiveCleanupSettings.For(probePath).EditorConfigKeys.ToDictionary(
            entry => entry.Key,
            entry => $"Overridden by .editorconfig: {entry.Value} in {EditorConfigHelper.FindDefiningConfigPath(probePath, entry.Value)}",
            StringComparer.Ordinal);

        return new EditorConfigOverrideNotes(notes);
    }
}
