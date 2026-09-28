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
        if (Directory.Exists(_userSettingsDirectory))
        {
            Directory.Delete(_userSettingsDirectory, recursive: true);
        }
    }
}
