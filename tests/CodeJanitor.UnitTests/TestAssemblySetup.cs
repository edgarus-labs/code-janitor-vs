using System;
using System.IO;
using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeJanitor.UnitTests;

/// <summary>
/// Points the user settings file at a temporary directory for the whole test run, so tests neither read the settings
/// of the developer running them nor overwrite them through <see cref="System.Configuration.ApplicationSettingsBase.Save" />.
/// </summary>
[TestClass]
public static class TestAssemblySetup
{
    private static string _userSettingsDirectory;

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext context)
    {
        _userSettingsDirectory = Path.Combine(Path.GetTempPath(), "CodeJanitor.UnitTests", "UserSettings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_userSettingsDirectory);
        SettingsContextHelper.UserSettingsDirectoryOverride = _userSettingsDirectory;
        Settings.Default.Reload();
    }

    [AssemblyCleanup]
    public static void AssemblyCleanup()
    {
        SettingsContextHelper.UserSettingsDirectoryOverride = null;
        TryDeleteDirectory(_userSettingsDirectory, Console.Error);
    }

    /// <summary>
    /// Deletes the temporary directory best effort: a directory held by another process (e.g. a virus scanner) must not
    /// fail or mask the test run, and the leftover is reported to <paramref name="log" /> instead of swallowed silently.
    /// </summary>
    /// <param name="path">The directory to delete; ignored when it does not exist.</param>
    /// <param name="log">Receives a line naming the directory and the reason when deleting fails.</param>
    /// <returns>True when the directory no longer exists.</returns>
    internal static bool TryDeleteDirectory(string path, TextWriter log)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.WriteLine($"Could not delete the temporary test directory '{path}': {ex.Message}");

            return false;
        }
    }
}
