using CodeJanitor.Helpers;
using System.Configuration;

namespace CodeJanitor.Properties;

/// <summary>
/// This partial class instructs the <see cref="Settings"/> class to utilize the <see cref="CodeJanitorSettingsProvider"/>.
/// </summary>

[SettingsProvider(typeof(CodeJanitorSettingsProvider))]
public sealed partial class Settings
{
    /// <summary>
    /// Migrates legacy sensitive settings values to their protected equivalents.
    /// </summary>
    /// <returns>True when settings were changed, otherwise false.</returns>

    public bool MigrateSensitiveSettings()
    {
        var changed = false;

        if (string.IsNullOrWhiteSpace(Cleaning_AiXmlDocumentationApiKeyEncrypted) &&
            !string.IsNullOrWhiteSpace(Cleaning_AiXmlDocumentationApiKey))
        {
            Cleaning_AiXmlDocumentationApiKeyEncrypted = SecretProtectionHelper.ProtectForCurrentUser(Cleaning_AiXmlDocumentationApiKey);
            Cleaning_AiXmlDocumentationApiKey = string.Empty;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Updates application settings to reflect a more recent installation of the application.
    /// </summary>

    public override void Upgrade()
    {
        var oldSettingsProvider = new LocalFileSettingsProvider();
        var oldPropertyValues = oldSettingsProvider.GetPropertyValues(Context, Properties);

        foreach (SettingsPropertyValue oldPropertyValue in oldPropertyValues)
        {
            if (!Equals(this[oldPropertyValue.Name], oldPropertyValue.PropertyValue))
            {
                this[oldPropertyValue.Name] = oldPropertyValue.PropertyValue;
            }
        }

        MigrateSensitiveSettings();
    }
}
