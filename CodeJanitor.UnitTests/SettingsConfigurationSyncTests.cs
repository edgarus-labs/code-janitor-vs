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
public class SettingsConfigurationSyncTests
{
    [TestMethod]
    [TestCategory("Cleaning UnitTests")]
    public void EveryUserScopedSettingHasAMatchingConfigurationEntry()
    {
        var declared = typeof(Settings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(x => x.GetCustomAttributes().Any(a => a.GetType().Name == "UserScopedSettingAttribute"))
            .Select(x => x.Name)
            .ToList();

        var configured = ReadConfiguredSettingNames();

        var missing = declared.Except(configured, StringComparer.Ordinal).ToList();
        var stale = configured.Except(declared, StringComparer.Ordinal).ToList();

        Assert.AreEqual(0, missing.Count, "Settings declared in code but missing from app.config: " + string.Join(", ", missing));
        Assert.AreEqual(0, stale.Count, "Entries left in app.config with no matching setting: " + string.Join(", ", stale));
    }

    private static List<string> ReadConfiguredSettingNames()
    {
        var configPath = typeof(Settings).Assembly.Location + ".config";
        Assert.IsTrue(File.Exists(configPath), "Configuration file not found: " + configPath);

        var section = XDocument.Load(configPath).Root
            ?.Element("userSettings")
            ?.Element("CodeJanitor.Properties.Settings");

        Assert.IsNotNull(section, "userSettings section not found in " + configPath);

        return section.Elements("setting")
            .Select(x => (string)x.Attribute("name"))
            .Where(x => !string.IsNullOrEmpty(x))
            .ToList();
    }
}
