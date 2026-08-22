using CodeJanitor.Helpers;
using CodeJanitor.Logic.Ai;
using CodeJanitor.Properties;

namespace CodeJanitor.UI.Dialogs.Options.XmlDoc;

/// <summary>
/// The view model for the AI XML documentation options page.
/// </summary>
public class XmlDocViewModel : OptionsPageViewModel
{
    private bool _isTestingAiXmlDocumentationConnection;
    private DelegateCommand _testAiXmlDocumentationConnectionCommand;

    /// <summary>
    /// Initializes a new instance of the <see cref="XmlDocViewModel"/> class.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="activeSettings">The active settings.</param>
    public XmlDocViewModel(CodeJanitorPackage package, Settings activeSettings)
        : base(package, activeSettings)
    {
        Mappings = new SettingsToOptionsList(ActiveSettings, this)
        {
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_AiXmlDocumentationEnabled, x => AiXmlDocumentationEnabled),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_AiXmlDocumentationEndpointUrl, x => AiXmlDocumentationEndpointUrl),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_AiXmlDocumentationApiKeyEncrypted, x => AiXmlDocumentationApiKeyEncryptedStore),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_AiXmlDocumentationApiKeyHeader, x => AiXmlDocumentationApiKeyHeader),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_AiXmlDocumentationModel, x => AiXmlDocumentationModel),
            new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_AiXmlDocumentationTimeoutSeconds, x => AiXmlDocumentationTimeoutSeconds),
            new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_AiXmlDocumentationMaxMethodsPerFile, x => AiXmlDocumentationMaxMethodsPerFile),
            new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_AiXmlDocumentationMaxParallelFiles, x => AiXmlDocumentationMaxParallelFiles),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_AiXmlDocumentationPreviewChanges, x => AiXmlDocumentationPreviewChanges),
            new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_AiXmlDocumentationMaxRequestsPerCleanup, x => AiXmlDocumentationMaxRequestsPerCleanup),
            new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_AiXmlDocumentationMaxInputCharsPerMethod, x => AiXmlDocumentationMaxInputCharsPerMethod),
            new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_AiXmlDocumentationMaxTokensPerRequest, x => AiXmlDocumentationMaxTokensPerRequest),
            new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_AiXmlDocumentationContextWindowTokens, x => AiXmlDocumentationContextWindowTokens),
            new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_AiXmlDocumentationMaxEstimatedTokensPerCleanup, x => AiXmlDocumentationMaxEstimatedTokensPerCleanup),
            new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_AiXmlDocumentationGlobalTimeoutSeconds, x => AiXmlDocumentationGlobalTimeoutSeconds),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_AiXmlDocumentationAllowDeterministicFallback, x => AiXmlDocumentationAllowDeterministicFallback),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_AiXmlDocumentationIgnoreGeneratedCode, x => AiXmlDocumentationIgnoreGeneratedCode),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_AiXmlDocumentationIgnoreObsolete, x => AiXmlDocumentationIgnoreObsolete),
            new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_AiXmlDocumentationIgnoreTestMethods, x => AiXmlDocumentationIgnoreTestMethods),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_AiXmlDocumentationIgnorePattern, x => AiXmlDocumentationIgnorePattern),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Ai_TestFramework, x => AiTestFramework),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Ai_MockingLibrary, x => AiMockingLibrary),
        };
    }

    /// <summary>
    /// Gets the header for the options page.
    /// </summary>
    public override string Header => "XML Documentation";

    /// <summary>
    /// Gets or sets a value indicating whether AI-assisted XML documentation is enabled.
    /// </summary>
    public bool AiXmlDocumentationEnabled
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the OpenAI-compatible endpoint URL for XML documentation generation.
    /// </summary>
    public string AiXmlDocumentationEndpointUrl
    {
        get => GetPropertyValue<string>();
        set
        {
            if (SetPropertyValue(value))
            {
                OnAiXmlDocumentationConfigurationChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the protected persisted API key value.
    /// </summary>
    public string AiXmlDocumentationApiKeyEncryptedStore
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the API key for the OpenAI-compatible endpoint.
    /// </summary>
    public string AiXmlDocumentationApiKey
    {
        get => GetPropertyValue<string>();
        set
        {
            if (SetPropertyValue(value))
            {
                OnAiXmlDocumentationConfigurationChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the custom header name for the API key.
    /// </summary>
    public string AiXmlDocumentationApiKeyHeader
    {
        get => GetPropertyValue<string>();
        set
        {
            if (SetPropertyValue(value))
            {
                OnAiXmlDocumentationConfigurationChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the model (or deployment) name sent to the endpoint.
    /// </summary>
    public string AiXmlDocumentationModel
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the request timeout in seconds for AI calls.
    /// </summary>
    public int AiXmlDocumentationTimeoutSeconds
    {
        get => GetPropertyValue<int>();
        set
        {
            if (value > 0)
            {
                SetPropertyValue(value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the maximum number of methods per file processed by AI XML documentation.
    /// </summary>
    public int AiXmlDocumentationMaxMethodsPerFile
    {
        get => GetPropertyValue<int>();
        set
        {
            if (value > 0)
            {
                SetPropertyValue(value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the maximum number of files processed in parallel during XML documentation generation.
    /// </summary>
    public int AiXmlDocumentationMaxParallelFiles
    {
        get => GetPropertyValue<int>();
        set
        {
            if (value > 0)
            {
                SetPropertyValue(value);
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether preview changes should be enabled.
    /// </summary>
    public bool AiXmlDocumentationPreviewChanges
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the maximum number of AI requests per run.
    /// </summary>
    public int AiXmlDocumentationMaxRequestsPerCleanup
    {
        get => GetPropertyValue<int>();
        set
        {
            if (value > 0)
            {
                SetPropertyValue(value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the maximum input characters per method.
    /// </summary>
    public int AiXmlDocumentationMaxInputCharsPerMethod
    {
        get => GetPropertyValue<int>();
        set
        {
            if (value > 0)
            {
                SetPropertyValue(value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the maximum output tokens per request.
    /// </summary>
    public int AiXmlDocumentationMaxTokensPerRequest
    {
        get => GetPropertyValue<int>();
        set
        {
            if (value > 0)
            {
                SetPropertyValue(value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the minimum context window size in tokens.
    /// </summary>
    public int AiXmlDocumentationContextWindowTokens
    {
        get => GetPropertyValue<int>();
        set
        {
            if (value > 0)
            {
                SetPropertyValue(value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the maximum estimated tokens per run.
    /// </summary>
    public int AiXmlDocumentationMaxEstimatedTokensPerCleanup
    {
        get => GetPropertyValue<int>();
        set
        {
            if (value > 0)
            {
                SetPropertyValue(value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the global timeout in seconds for a documentation run.
    /// </summary>
    public int AiXmlDocumentationGlobalTimeoutSeconds
    {
        get => GetPropertyValue<int>();
        set
        {
            if (value > 0)
            {
                SetPropertyValue(value);
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether deterministic fallback is allowed on AI failure.
    /// </summary>
    public bool AiXmlDocumentationAllowDeterministicFallback
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether compiler-generated code is ignored.
    /// </summary>
    public bool AiXmlDocumentationIgnoreGeneratedCode
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether [Obsolete] methods are ignored.
    /// </summary>
    public bool AiXmlDocumentationIgnoreObsolete
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether unit test methods are ignored.
    /// </summary>
    public bool AiXmlDocumentationIgnoreTestMethods
    {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the regex pattern for members to ignore.
    /// </summary>
    public string AiXmlDocumentationIgnorePattern
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets the test-connection command for the AI XML documentation endpoint.
    /// </summary>
    public DelegateCommand TestAiXmlDocumentationConnectionCommand => _testAiXmlDocumentationConnectionCommand
        ?? (_testAiXmlDocumentationConnectionCommand = new DelegateCommand(
            OnTestAiXmlDocumentationConnectionCommandExecuted,
            parameter => !_isTestingAiXmlDocumentationConnection));

    /// <summary>
    /// Gets a short status text for the endpoint connectivity test.
    /// </summary>
    public string AiXmlDocumentationConnectionStatus
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the target unit test framework (xUnit / NUnit / MSTest).
    /// </summary>
    public string AiTestFramework
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the target mocking library (Moq / NSubstitute).
    /// </summary>
    public string AiMockingLibrary
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets a flag indicating whether endpoint URL is configured.
    /// </summary>
    public bool IsAiXmlDocumentationEndpointConfigured => OpenAiCompatibleClient.IsEndpointConfigured(AiXmlDocumentationEndpointUrl);

    /// <summary>
    /// Loads settings and decrypts API key.
    /// </summary>
    public override void LoadSettings()
    {
        base.LoadSettings();

        var decryptedKey = SecretProtectionHelper.UnprotectForCurrentUser(AiXmlDocumentationApiKeyEncryptedStore);
        if (string.IsNullOrWhiteSpace(decryptedKey) && !string.IsNullOrWhiteSpace(ActiveSettings.Cleaning_AiXmlDocumentationApiKey))
        {
            decryptedKey = ActiveSettings.Cleaning_AiXmlDocumentationApiKey;
        }

        SetPropertyValue(decryptedKey, nameof(AiXmlDocumentationApiKey));

        if (string.IsNullOrWhiteSpace(AiXmlDocumentationApiKeyHeader))
        {
            AiXmlDocumentationApiKeyHeader = "Authorization";
        }

        if (AiXmlDocumentationTimeoutSeconds <= 0)
        {
            AiXmlDocumentationTimeoutSeconds = 30;
        }

        if (AiXmlDocumentationMaxMethodsPerFile <= 0)
        {
            AiXmlDocumentationMaxMethodsPerFile = 25;
        }

        if (AiXmlDocumentationMaxParallelFiles <= 0)
        {
            AiXmlDocumentationMaxParallelFiles = 4;
        }

        if (AiXmlDocumentationMaxRequestsPerCleanup <= 0)
        {
            AiXmlDocumentationMaxRequestsPerCleanup = 25;
        }

        if (AiXmlDocumentationMaxInputCharsPerMethod <= 0)
        {
            AiXmlDocumentationMaxInputCharsPerMethod = 2500;
        }

        if (AiXmlDocumentationMaxTokensPerRequest <= 0)
        {
            AiXmlDocumentationMaxTokensPerRequest = 256;
        }

        if (AiXmlDocumentationContextWindowTokens <= 0)
        {
            AiXmlDocumentationContextWindowTokens = 131072;
        }

        if (AiXmlDocumentationMaxEstimatedTokensPerCleanup <= 0)
        {
            AiXmlDocumentationMaxEstimatedTokensPerCleanup = 8000;
        }

        if (AiXmlDocumentationGlobalTimeoutSeconds <= 0)
        {
            AiXmlDocumentationGlobalTimeoutSeconds = 60;
        }

        if (string.IsNullOrWhiteSpace(AiTestFramework))
        {
            AiTestFramework = "xUnit";
        }

        if (string.IsNullOrWhiteSpace(AiMockingLibrary))
        {
            AiMockingLibrary = "Moq";
        }

        AiXmlDocumentationConnectionStatus = !IsAiXmlDocumentationEndpointConfigured
            ? "Set a valid endpoint URL (e.g. http://192.168.1.52:20128/v1 or https://api.openai.com/v1)."
            : "Click 'Test Connection' to verify connectivity.";

        RaisePropertyChanged(nameof(IsAiXmlDocumentationEndpointConfigured));
    }

    /// <summary>
    /// Encrypts the API key for the current user and persists all settings.
    /// </summary>
    public override void SaveSettings()
    {
        AiXmlDocumentationApiKeyEncryptedStore = SecretProtectionHelper.ProtectForCurrentUser(AiXmlDocumentationApiKey);
        ActiveSettings.Cleaning_AiXmlDocumentationApiKey = string.Empty;
        base.SaveSettings();
    }

    private void OnTestAiXmlDocumentationConnectionCommandExecuted(object parameter)
    {
        if (!IsAiXmlDocumentationEndpointConfigured)
        {
            AiXmlDocumentationConnectionStatus = "Endpoint URL is missing or invalid.";
            return;
        }

        _isTestingAiXmlDocumentationConnection = true;
        TestAiXmlDocumentationConnectionCommand.RaiseCanExecuteChanged();
        AiXmlDocumentationConnectionStatus = $"Testing connection (timeout: {AiXmlDocumentationTimeoutSeconds}s)...";

        var endpointUrl = AiXmlDocumentationEndpointUrl;
        var apiKey = AiXmlDocumentationApiKey;
        var apiKeyHeader = AiXmlDocumentationApiKeyHeader;
        var model = AiXmlDocumentationModel;
        var timeoutSeconds = AiXmlDocumentationTimeoutSeconds;

        Package.JoinableTaskFactory.RunAsync(async delegate
        {
            var result = await AiXmlDocumentationLogic.ValidateConnectionAsync(
                endpointUrl,
                apiKey,
                apiKeyHeader,
                model,
                timeoutSeconds);

            await Package.JoinableTaskFactory.SwitchToMainThreadAsync();

            AiXmlDocumentationConnectionStatus = result.Succeeded
                ? "Connection successful."
                : $"Connection failed: {result.ErrorMessage}";

            _isTestingAiXmlDocumentationConnection = false;
            TestAiXmlDocumentationConnectionCommand.RaiseCanExecuteChanged();
        });
    }

    private void OnAiXmlDocumentationConfigurationChanged()
    {
        AiXmlDocumentationConnectionStatus = IsAiXmlDocumentationEndpointConfigured
            ? "Click 'Test Connection' to verify connectivity."
            : "Set a valid endpoint URL (e.g. http://192.168.1.52:20128/v1 or https://api.openai.com/v1).";

        RaisePropertyChanged(nameof(IsAiXmlDocumentationEndpointConfigured));
    }
}
