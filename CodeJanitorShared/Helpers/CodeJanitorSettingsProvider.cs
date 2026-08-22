using System;
using System.Configuration;
using System.Xml;

namespace CodeJanitor.Helpers;

/// <summary>
/// This class handles customizing the settings persistance.
/// </summary>

public class CodeJanitorSettingsProvider : LocalFileSettingsProvider
{
    /// <summary>
    /// Returns the collection of setting property values for the specified application instance
    /// and settings property group.
    /// </summary>
    /// <param name="context">
    /// A <see cref="T:System.Configuration.SettingsContext"/> describing the current
    /// application usage.
    /// </param>
    /// <param name="properties">
    /// A <see cref="T:System.Configuration.SettingsPropertyCollection"/> containing the
    /// settings property group whose values are to be retrieved.
    /// </param>
    /// <returns>
    /// A <see cref="T:System.Configuration.SettingsPropertyValueCollection"/> containing the
    /// values for the specified settings property group.
    /// </returns>

    public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection properties)
    {
        try
        {
            var solutionSettingsPath = SettingsContextHelper.GetSolutionSettingsPath(context);
            var userSettingsPath = SettingsContextHelper.GetUserSettingsPath();
            var sectionName = GetSectionName(context);

            var userSettings = ReadSettingsFromFile(userSettingsPath, sectionName);
            var solutionSettings = ReadSettingsFromFile(solutionSettingsPath, sectionName);

            var propertyValues = MergeSettingsIntoPropertyValues(userSettings, solutionSettings, properties);

            return propertyValues;
        }
        catch (Exception ex)
        {
            OutputWindowHelper.ExceptionWriteLine("Unable to GetPropertyValues.", ex);

            throw;
        }
    }

    /// <summary>
    /// Sets the values of the specified group of property settings.
    /// </summary>
    /// <param name="context">
    /// A <see cref="T:System.Configuration.SettingsContext"/> describing the current
    /// application usage.
    /// </param>
    /// <param name="values">
    /// A <see cref="T:System.Configuration.SettingsPropertyValueCollection"/> representing the
    /// group of property settings to set.
    /// </param>

    public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection values)
    {
        try
        {
            var settingsPath = SettingsContextHelper.GetSolutionSettingsPath(context) ?? SettingsContextHelper.GetUserSettingsPath();
            var sectionName = GetSectionName(context);

            WriteSettingsToFile(settingsPath, sectionName, values);
        }
        catch (Exception ex)
        {
            OutputWindowHelper.ExceptionWriteLine("Unable to SetPropertyValues.", ex);

            throw;
        }
    }

    /// <summary>
    /// Exports every current value of the specified settings into a CodeJanitor.config-format
    /// file at the specified path, for sharing rules between machines/teams.
    /// </summary>
    /// <param name="settings">The settings instance to export.</param>
    /// <param name="filePath">The destination file path.</param>

    public static void ExportSettingsToFile(ApplicationSettingsBase settings, string filePath)
    {
        var sectionName = GetSectionName(settings.Context);
        var values = settings.PropertyValues;

        foreach (SettingsPropertyValue value in values)
        {
            // Force every property to be written, not just ones flagged as changed.
            value.IsDirty = true;
        }

        WriteSettingsToFile(filePath, sectionName, values);
    }

    /// <summary>
    /// Imports values from a CodeJanitor.config-format file at the specified path into the
    /// specified settings instance, then saves them as the new user settings.
    /// </summary>
    /// <param name="settings">The settings instance to update.</param>
    /// <param name="filePath">The source file path.</param>

    public static void ImportSettingsFromFile(ApplicationSettingsBase settings, string filePath)
    {
        var sectionName = GetSectionName(settings.Context);
        var fileSettings = ReadSettingsFromFile(filePath, sectionName);

        foreach (SettingsProperty property in settings.Properties)
        {
            var element = fileSettings.Get(property.Name);
            if (element is not null)
            {
                settings[property.Name] = new SettingsPropertyValue(property)
                {
                    SerializedValue = element.Value.ValueXml.InnerText,
                    Deserialized = false,
                }.PropertyValue;
            }
        }

        settings.Save();
    }

    /// <summary>
    /// Gets the name of the section where settings will be located based on the specified <see cref="SettingsContext"/>.
    /// </summary>
    /// <param name="context">
    /// A <see cref="T:System.Configuration.SettingsContext"/> describing the current
    /// application usage.
    /// </param>
    /// <returns>The section name, otherwise null.</returns>

    private static string GetSectionName(SettingsContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        return context["GroupName"]?.ToString();
    }

    /// <summary>
    /// Gets the <see cref="Configuration"/> at the specified path.
    /// </summary>
    /// <param name="path">The path to the settings file.</param>
    /// <returns>The <see cref="Configuration"/> object.</returns>

    private static Configuration GetConfiguration(string path)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));

        var fileMap = new ExeConfigurationFileMap { ExeConfigFilename = path };
        var config = ConfigurationManager.OpenMappedExeConfiguration(fileMap, ConfigurationUserLevel.None);

        return config;
    }

    /// <summary>
    /// Gets the <see cref="SettingElementCollection"/> for the specified section name within
    /// the specified configuration.
    /// </summary>
    /// <param name="config">The <see cref="Configuration"/> object.</param>
    /// <param name="sectionName">The settings section name.</param>
    /// <returns>
    /// A <see cref="SettingElementCollection"/> for the section, or an empty section if not found.
    /// </returns>

    private static SettingElementCollection GetSettingElementCollection(Configuration config, string sectionName)
    {
        var userSettings = config.GetSectionGroup("userSettings");
        if (userSettings is null)
        {
            userSettings = new UserSettingsGroup();
            config.SectionGroups.Add("userSettings", userSettings);
        }

        if (!(userSettings.Sections.Get(sectionName) is ClientSettingsSection section))
        {
            section = new ClientSettingsSection();
            userSettings.Sections.Add(sectionName, section);
        }

        return section.Settings;
    }

    /// <summary>
    /// Reads settings from a settings file at the specified path.
    /// </summary>
    /// <param name="path">The settings file path.</param>
    /// <param name="sectionName">The name of the settings section.</param>
    /// <returns>A collection representing the settings, otherwise an empty collection.</returns>

    private static SettingElementCollection ReadSettingsFromFile(string path, string sectionName)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(sectionName))
            {
                var config = GetConfiguration(path);
                var settings = GetSettingElementCollection(config, sectionName);

                return settings;
            }
        }
        catch (Exception ex)
        {
            OutputWindowHelper.ExceptionWriteLine("Unable to read settings.", ex);
        }

        return new SettingElementCollection();
    }

    /// <summary>
    /// Merges the specified user and solution settings into a new <see cref="SettingsPropertyValueCollection"/>.
    /// </summary>
    /// <param name="userSettings">The user settings.</param>
    /// <param name="solutionSettings">The solution settings.</param>
    /// <param name="properties">The setting properties collection.</param>
    /// <returns>A merged <see cref="SettingsPropertyValueCollection"/>.</returns>

    private static SettingsPropertyValueCollection MergeSettingsIntoPropertyValues(SettingElementCollection userSettings, SettingElementCollection solutionSettings, SettingsPropertyCollection properties)
    {
        var values = new SettingsPropertyValueCollection();

        foreach (SettingsProperty property in properties)
        {
            var value = new SettingsPropertyValue(property);

            ApplySettingToValue(value, userSettings);
            ApplySettingToValue(value, solutionSettings);

            value.IsDirty = false;

            values.Add(value);
        }

        return values;
    }

    /// <summary>
    /// Checks the specified settings collection to see if it has a serialized value that should
    /// be applied to the specified <see cref="SettingsPropertyValue"/>.
    /// </summary>
    /// <param name="value">An individual settings property value.</param>
    /// <param name="settings">A collection representing the settings.</param>

    private static void ApplySettingToValue(SettingsPropertyValue value, SettingElementCollection settings)
    {
        var setting = settings.Get(value.Name);
        if (setting is not null)
        {
            value.SerializedValue = setting.Value.ValueXml.InnerText;

            // Mark the value as not deserialized, which will trigger a deserialization of the SerializedValue into the PropertyValue.
            value.Deserialized = false;
        }
    }

    /// <summary>
    /// Writes settings to a settings file at the specified path.
    /// </summary>
    /// <param name="path">The settings file path.</param>
    /// <param name="sectionName">The name of the settings section.</param>
    /// <param name="values">
    /// A <see cref="T:System.Configuration.SettingsPropertyValueCollection"/> representing the
    /// group of property settings to set.
    /// </param>

    private static void WriteSettingsToFile(string path, string sectionName, SettingsPropertyValueCollection values)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(sectionName))
            {
                var config = GetConfiguration(path);
                var settings = GetSettingElementCollection(config, sectionName);

                UpdateSettingsFromPropertyValues(settings, values);

                config.Save();
            }
        }
        catch (Exception ex)
        {
            OutputWindowHelper.ExceptionWriteLine("Unable to write settings.", ex);
        }
    }

    /// <summary>
    /// Updates the <see cref="SettingElementCollection"/> from the <see cref="SettingsPropertyValueCollection"/>.
    /// </summary>
    /// <param name="settings">A collection representing the settings.</param>
    /// <param name="values">
    /// A <see cref="T:System.Configuration.SettingsPropertyValueCollection"/> representing the
    /// group of property settings to set.
    /// </param>

    private static void UpdateSettingsFromPropertyValues(SettingElementCollection settings, SettingsPropertyValueCollection values)
    {
        foreach (SettingsPropertyValue value in values)
        {
            if (value.IsDirty)
            {
                var element = settings.Get(value.Name);
                if (element is null)
                {
                    // Note: We only support string serialization for brevity of implementation.
                    element = new SettingElement(value.Name, SettingsSerializeAs.String);
                    settings.Add(element);
                }

                element.SerializeAs = SettingsSerializeAs.String;
                element.Value.ValueXml = CreateXmlValue(value.SerializedValue);
            }
        }
    }

    /// <summary>
    /// Creates an <see cref="XmlNode"/> containing the specified serialized value.
    /// </summary>
    /// <param name="serializedValue">The serialized value.</param>
    /// <returns>The <see cref="XmlNode"/>.</returns>

    private static XmlNode CreateXmlValue(object serializedValue)
    {
        var node = new XmlDocument().CreateElement("value");
        node.InnerText = serializedValue.ToString();

        return node;
    }
}
