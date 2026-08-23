using System;
using System.Collections.ObjectModel;
using System.Linq;
using CodeJanitor.Helpers;
using CodeJanitor.Logic.Ai;
using CodeJanitor.Properties;

namespace CodeJanitor.UI.Dialogs.Options.XmlDoc;

/// <summary>
/// The view model for the AI XML documentation options page.
/// </summary>
public sealed class XmlDocViewModel : OptionsPageViewModel
{
    private bool _isTestingAiXmlDocumentationConnection;
    private DelegateCommand _testAiXmlDocumentationConnectionCommand;
    private DelegateCommand _detectCopilotCommand;
    private DelegateCommand _fetchCopilotModelsCommand;
    private ObservableCollection<string> _availableCopilotModels;
    private string _copilotStatusText;

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
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Ai_Provider, x => AiProvider),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Ai_CustomEndpointUrl, x => CustomEndpointUrl),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Ai_CustomApiKeyEncrypted, x => CustomApiKeyEncryptedStore),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Ai_CustomApiKeyHeader, x => CustomApiKeyHeader),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Ai_CustomModel, x => CustomModel),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Ai_CopilotEndpointUrl, x => CopilotEndpointUrl),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Ai_CopilotApiKeyEncrypted, x => CopilotApiKeyEncryptedStore),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Ai_CopilotApiKeyHeader, x => CopilotApiKeyHeader),
            new SettingToOptionMapping<string, string>(x => ActiveSettings.Ai_CopilotModel, x => CopilotModel),
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
    public override string Header => "AI Assistant";

    /// <summary>
    /// Gets or sets the AI provider backend (Custom or GitHubCopilot).
    /// </summary>
    public string AiProvider
    {
        get => GetPropertyValue<string>() ?? "Custom";
        set
        {
            if (SetPropertyValue(value))
            {
                RaisePropertyChanged(nameof(IsCustomProvider));
                RaisePropertyChanged(nameof(IsGitHubCopilotProvider));
                RaisePropertyChanged(nameof(IsAiXmlDocumentationEndpointConfigured));
                OnAiXmlDocumentationConfigurationChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether Custom OpenAI-compatible backend is selected.
    /// </summary>
    public bool IsCustomProvider
    {
        get => string.Equals(AiProvider, "Custom", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                AiProvider = "Custom";
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether GitHub Copilot backend is selected.
    /// </summary>
    public bool IsGitHubCopilotProvider
    {
        get => string.Equals(AiProvider, "GitHubCopilot", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                AiProvider = "GitHubCopilot";
            }
        }
    }

    /// <summary>
    /// Gets or sets the detected status of GitHub Copilot.
    /// </summary>
    public string CopilotStatusText
    {
        get => _copilotStatusText ?? "Click 'Detect Copilot' to check local Visual Studio connection.";
        set
        {
            if (_copilotStatusText != value)
            {
                _copilotStatusText = value;
                RaisePropertyChanged(nameof(CopilotStatusText));
            }
        }
    }

    /// <summary>
    /// Command to detect active GitHub Copilot connection and auto-configure settings.
    /// </summary>
    public DelegateCommand DetectCopilotCommand => _detectCopilotCommand ?? (_detectCopilotCommand = new DelegateCommand(OnDetectCopilotCommandExecuted));

    /// <summary>
    /// Command to dynamically fetch available models from the GitHub Copilot API.
    /// </summary>
    public DelegateCommand FetchCopilotModelsCommand => _fetchCopilotModelsCommand ?? (_fetchCopilotModelsCommand = new DelegateCommand(OnFetchCopilotModelsCommandExecuted));

    /// <summary>
    /// Gets the collection of available models for GitHub Copilot.
    /// </summary>
    public ObservableCollection<string> AvailableCopilotModels
    {
        get
        {
            if (_availableCopilotModels is null)
            {
                _availableCopilotModels = new ObservableCollection<string>(GitHubCopilotDetector.SupportedCopilotModels);
            }

            return _availableCopilotModels;
        }
    }

    /// <summary>
    /// Gets or sets the custom AI endpoint URL.
    /// </summary>
    public string CustomEndpointUrl
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
    /// Gets or sets the custom protected persisted API key value.
    /// </summary>
    public string CustomApiKeyEncryptedStore
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the custom API key.
    /// </summary>
    public string CustomApiKey
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
    /// Gets or sets the custom API key header name.
    /// </summary>
    public string CustomApiKeyHeader
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
    /// Gets or sets the custom model name.
    /// </summary>
    public string CustomModel
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the Copilot endpoint URL.
    /// </summary>
    public string CopilotEndpointUrl
    {
        get => GetPropertyValue<string>() ?? GitHubCopilotDetector.DefaultCopilotEndpoint;
        set
        {
            if (SetPropertyValue(value))
            {
                OnAiXmlDocumentationConfigurationChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the Copilot protected persisted API key value.
    /// </summary>
    public string CopilotApiKeyEncryptedStore
    {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the Copilot API key or GitHub token.
    /// </summary>
    public string CopilotApiKey
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
    /// Gets or sets the Copilot API key header name.
    /// </summary>
    public string CopilotApiKeyHeader
    {
        get => GetPropertyValue<string>() ?? "Authorization";
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// Gets or sets the Copilot model name.
    /// </summary>
    public string CopilotModel
    {
        get => GetPropertyValue<string>() ?? GitHubCopilotDetector.DefaultCopilotModel;
        set => SetPropertyValue(value);
    }

    /// <summary>
    /// List of standard models supported by GitHub Copilot.
    /// </summary>
    public string[] SupportedCopilotModels => GitHubCopilotDetector.SupportedCopilotModels;

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
    public bool IsAiXmlDocumentationEndpointConfigured =>
        IsCustomProvider
            ? OpenAiCompatibleClient.IsEndpointConfigured(CustomEndpointUrl)
            : OpenAiCompatibleClient.IsEndpointConfigured(CopilotEndpointUrl);

    /// <summary>
    /// Loads settings and decrypts API key.
    /// </summary>
    public override void LoadSettings()
    {
        base.LoadSettings();

        var decryptedCustomKey = SecretProtectionHelper.UnprotectForCurrentUser(CustomApiKeyEncryptedStore);
        SetPropertyValue(decryptedCustomKey, nameof(CustomApiKey));

        var decryptedCopilotKey = SecretProtectionHelper.UnprotectForCurrentUser(CopilotApiKeyEncryptedStore);
        SetPropertyValue(decryptedCopilotKey, nameof(CopilotApiKey));

        // Migration from legacy settings if custom profile is empty
        if (string.IsNullOrWhiteSpace(CustomEndpointUrl) &&
            !string.IsNullOrWhiteSpace(ActiveSettings.Cleaning_AiXmlDocumentationEndpointUrl) &&
            !GitHubCopilotDetector.IsCopilotEndpoint(ActiveSettings.Cleaning_AiXmlDocumentationEndpointUrl))
        {
            CustomEndpointUrl = ActiveSettings.Cleaning_AiXmlDocumentationEndpointUrl;
            CustomApiKeyHeader = string.IsNullOrWhiteSpace(ActiveSettings.Cleaning_AiXmlDocumentationApiKeyHeader) ? "Authorization" : ActiveSettings.Cleaning_AiXmlDocumentationApiKeyHeader;
            CustomModel = ActiveSettings.Cleaning_AiXmlDocumentationModel;
            var legacyKey = SecretProtectionHelper.UnprotectForCurrentUser(ActiveSettings.Cleaning_AiXmlDocumentationApiKeyEncrypted);
            if (string.IsNullOrWhiteSpace(legacyKey) && !string.IsNullOrWhiteSpace(ActiveSettings.Cleaning_AiXmlDocumentationApiKey))
            {
                legacyKey = ActiveSettings.Cleaning_AiXmlDocumentationApiKey;
            }
            CustomApiKey = legacyKey;
        }

        if (string.IsNullOrWhiteSpace(CustomApiKeyHeader))
        {
            CustomApiKeyHeader = "Authorization";
        }

        if (string.IsNullOrWhiteSpace(CopilotEndpointUrl))
        {
            CopilotEndpointUrl = GitHubCopilotDetector.DefaultCopilotEndpoint;
        }

        if (string.IsNullOrWhiteSpace(CopilotApiKeyHeader))
        {
            CopilotApiKeyHeader = "Authorization";
        }

        if (string.IsNullOrWhiteSpace(CopilotModel))
        {
            CopilotModel = GitHubCopilotDetector.DefaultCopilotModel;
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
    /// Encrypts the API keys for the current user and persists all settings.
    /// </summary>
    public override void SaveSettings()
    {
        CustomApiKeyEncryptedStore = SecretProtectionHelper.ProtectForCurrentUser(CustomApiKey);
        CopilotApiKeyEncryptedStore = SecretProtectionHelper.ProtectForCurrentUser(CopilotApiKey);

        ActiveSettings.Ai_CustomEndpointUrl = CustomEndpointUrl;
        ActiveSettings.Ai_CustomApiKeyEncrypted = CustomApiKeyEncryptedStore;
        ActiveSettings.Ai_CustomApiKeyHeader = CustomApiKeyHeader;
        ActiveSettings.Ai_CustomModel = CustomModel;

        ActiveSettings.Ai_CopilotEndpointUrl = CopilotEndpointUrl;
        ActiveSettings.Ai_CopilotApiKeyEncrypted = CopilotApiKeyEncryptedStore;
        ActiveSettings.Ai_CopilotApiKeyHeader = CopilotApiKeyHeader;
        ActiveSettings.Ai_CopilotModel = CopilotModel;

        // Synchronize active execution settings
        if (IsCustomProvider)
        {
            AiXmlDocumentationEndpointUrl = CustomEndpointUrl;
            AiXmlDocumentationApiKeyEncryptedStore = CustomApiKeyEncryptedStore;
            AiXmlDocumentationApiKeyHeader = CustomApiKeyHeader;
            AiXmlDocumentationModel = CustomModel;
        }
        else
        {
            AiXmlDocumentationEndpointUrl = CopilotEndpointUrl;
            AiXmlDocumentationApiKeyEncryptedStore = CopilotApiKeyEncryptedStore;
            AiXmlDocumentationApiKeyHeader = CopilotApiKeyHeader;
            AiXmlDocumentationModel = CopilotModel;
        }

        ActiveSettings.Cleaning_AiXmlDocumentationApiKey = string.Empty;
        base.SaveSettings();
    }

    /// <summary>
    /// utes the AI XML documentation connection test by validating the configured endpoint asynchronously via the joinable task factory, updating the connection status message and can-execute state before, during, and after the test based on whether an endpoint URL is configured and the success of the validation call.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
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

        var endpointUrl = IsCustomProvider ? CustomEndpointUrl : CopilotEndpointUrl;
        var apiKey = IsCustomProvider ? CustomApiKey : CopilotApiKey;
        var apiKeyHeader = IsCustomProvider ? CustomApiKeyHeader : CopilotApiKeyHeader;
        var model = IsCustomProvider ? CustomModel : CopilotModel;
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

    /// <summary>
    /// ApplyCopilotDefaults populates empty or whitespace CopilotEndpointUrl, CopilotApiKeyHeader, and CopilotModel fields with their respective default values, then invokes OnDetectCopilotCommandExecuted with a null argument to trigger a detection cycle.
    /// </summary>
    private void ApplyCopilotDefaults()
    {
        if (string.IsNullOrWhiteSpace(CopilotEndpointUrl))
        {
            CopilotEndpointUrl = GitHubCopilotDetector.DefaultCopilotEndpoint;
        }

        if (string.IsNullOrWhiteSpace(CopilotApiKeyHeader))
        {
            CopilotApiKeyHeader = "Authorization";
        }

        if (string.IsNullOrWhiteSpace(CopilotModel))
        {
            CopilotModel = GitHubCopilotDetector.DefaultCopilotModel;
        }

        OnDetectCopilotCommandExecuted(null);
    }

    /// <summary>
    /// Executes the GitHub Copilot detection routine, updates the status text, auto-populates the endpoint URL, model, and API key from detection results when empty and Copilot is active, then triggers a fetch of available Copilot models.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    private void OnDetectCopilotCommandExecuted(object parameter)
    {
        var detection = GitHubCopilotDetector.DetectCopilotStatus();
        CopilotStatusText = detection.StatusDescription;

        if (detection.IsActive)
        {
            if (string.IsNullOrWhiteSpace(CopilotEndpointUrl))
            {
                CopilotEndpointUrl = detection.RecommendedEndpoint;
            }

            if (string.IsNullOrWhiteSpace(CopilotModel))
            {
                CopilotModel = detection.RecommendedModel;
            }

            if (!string.IsNullOrEmpty(detection.DetectedToken) && string.IsNullOrWhiteSpace(CopilotApiKey))
            {
                CopilotApiKey = detection.DetectedToken;
            }
        }

        OnFetchCopilotModelsCommandExecuted(null);
    }

    /// <summary>
    /// etches available Copilot models asynchronously (auto-detecting the API token when missing), updates the AvailableCopilotModels collection only if changed, and preserves the previous selection or falls back to the default model.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    private void OnFetchCopilotModelsCommandExecuted(object parameter)
    {
        var previousSelection = CopilotModel;
        var token = CopilotApiKey;
        if (string.IsNullOrWhiteSpace(token))
        {
            var detected = GitHubCopilotDetector.DetectCopilotStatus();
            token = detected.DetectedToken;
        }

        Package.JoinableTaskFactory.RunAsync(async delegate
        {
            var models = await GitHubCopilotDetector.FetchCopilotModelsAsync(token);
            await Package.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (!AvailableCopilotModels.SequenceEqual(models))
            {
                AvailableCopilotModels.Clear();
                foreach (var model in models)
                {
                    AvailableCopilotModels.Add(model);
                }
            }

            if (!string.IsNullOrWhiteSpace(previousSelection) && AvailableCopilotModels.Contains(previousSelection))
            {
                CopilotModel = previousSelection;
            }
            else if (string.IsNullOrWhiteSpace(CopilotModel) || !AvailableCopilotModels.Contains(CopilotModel))
            {
                CopilotModel = AvailableCopilotModels.FirstOrDefault() ?? GitHubCopilotDetector.DefaultCopilotModel;
            }
        });
    }

    /// <summary>
    /// AiXmlDocumentationConfigurationChanged updates AiXmlDocumentationConnectionStatus with an appropriate instructional message based on whether the AI XML documentation endpoint is configured, and raises a property change notification for IsAiXmlDocumentationEndpointConfigured.
    /// </summary>
    private void OnAiXmlDocumentationConfigurationChanged()
    {
        AiXmlDocumentationConnectionStatus = IsAiXmlDocumentationEndpointConfigured
            ? "Click 'Test Connection' to verify connectivity."
            : "Set a valid endpoint URL (e.g. http://192.168.1.52:20128/v1 or https://api.openai.com/v1).";

        RaisePropertyChanged(nameof(IsAiXmlDocumentationEndpointConfigured));
    }
}
