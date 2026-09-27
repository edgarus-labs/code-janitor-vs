using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeJanitor.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace CodeJanitor.UnitTests;

[TestClass]
public sealed class SettingsConfigurationSyncTests
{
    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void EveryUserScopedSettingHasAMatchingConfigurationEntry()
    {
        List<string> declared = typeof(Settings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(x => x.GetCustomAttributes().Any(a => a.GetType().Name == "UserScopedSettingAttribute"))
            .Select(x => x.Name)
            .ToList();

        List<string> configured = ReadConfiguredSettingNames();

        List<string> missing = declared.Except(configured, StringComparer.Ordinal).ToList();
        List<string> stale = configured.Except(declared, StringComparer.Ordinal).ToList();

        Assert.IsEmpty(missing, "Settings declared in code but missing from app.config: " + string.Join(", ", missing));
        Assert.IsEmpty(stale, "Entries left in app.config with no matching setting: " + string.Join(", ", stale));
    }

    private static List<string> ReadConfiguredSettingNames()
    {
        string configPath = typeof(Settings).Assembly.Location + ".config";
        Assert.IsTrue(File.Exists(configPath), "Configuration file not found: " + configPath);

        XElement section = XDocument.Load(configPath).Root
            ?.Element("userSettings")
            ?.Element("CodeJanitor.Properties.Settings");

        Assert.IsNotNull(section, "userSettings section not found in " + configPath);

        return section.Elements("setting")
            .Select(x => (string)x.Attribute("name"))
            .Where(x => !string.IsNullOrEmpty(x))
            .ToList();
    }
}
