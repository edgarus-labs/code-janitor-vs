using CodeJanitor.Logic.Cleaning;
using CodeJanitor.Helpers;
using CodeJanitor.Properties;
using CodeJanitor.UI.Enumerations;
using System;

namespace CodeJanitor.UI.Dialogs.Options.Cleaning
{
    /// <summary>
    /// The view model for cleaning update options.
    /// </summary>
    public class CleaningUpdateViewModel : OptionsPageViewModel
    {
        private static readonly TimeSpan SuccessfulConnectionCacheDuration = TimeSpan.FromMinutes(30);
        private static string _lastSuccessfulConnectionFingerprint;
        private static DateTime _lastSuccessfulConnectionUtc;

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="CleaningUpdateViewModel" /> class.
        /// </summary>
        /// <param name="package">The hosting package.</param>
        /// <param name="activeSettings">The active settings.</param>
        public CleaningUpdateViewModel(CodeJanitorPackage package, Settings activeSettings)
            : base(package, activeSettings)
        {
            Mappings = new SettingsToOptionsList(ActiveSettings, this)
            {
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_UpdateAccessorsToBothBeSingleLineOrMultiLine, x => UpdateAccessorsToBothBeSingleLineOrMultiLine),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_UpdateEndRegionDirectives, x => UpdateEndRegionDirectives),
                new SettingToOptionMapping<int, HeaderPosition>(x => ActiveSettings.Cleaning_UpdateFileHeader_HeaderPosition, x => HeaderPosition),
                new SettingToOptionMapping<int, HeaderUpdateMode>(x => ActiveSettings.Cleaning_UpdateFileHeader_HeaderUpdateMode, x => HeaderUpdateMode),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderCPlusPlus, x => UpdateFileHeaderCPlusPlus),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderCSharp, x => UpdateFileHeaderCSharp),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderCSS, x => UpdateFileHeaderCSS),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderFSharp, x => UpdateFileHeaderFSharp),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderHTML, x => UpdateFileHeaderHTML),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderJavaScript, x => UpdateFileHeaderJavaScript),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderJSON, x => UpdateFileHeaderJSON),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderLESS, x => UpdateFileHeaderLESS),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderPHP, x => UpdateFileHeaderPHP),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderPowerShell, x => UpdateFileHeaderPowerShell),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderR, x => UpdateFileHeaderR),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderSCSS, x => UpdateFileHeaderSCSS),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderTypeScript, x => UpdateFileHeaderTypeScript),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderVB, x => UpdateFileHeaderVisualBasic),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderXAML, x => UpdateFileHeaderXAML),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_UpdateFileHeaderXML, x => UpdateFileHeaderXML),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_UpdateSingleLineMethods, x => UpdateSingleLineMethods),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_ConvertToFileScopedNamespace, x => ConvertToFileScopedNamespace),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_ConvertToVarWhenApparent, x => ConvertToVarWhenApparent),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_ConvertToCollectionExpressions, x => ConvertToCollectionExpressions),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_ReuseJsonSerializerOptionsForCA1869, x => ReuseJsonSerializerOptionsForCA1869),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_SimplifySingleStatementLambdas, x => SimplifySingleStatementLambdas),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_MakeFieldsReadonlyWhenSafe, x => MakeFieldsReadonlyWhenSafe),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_SealClassesWhenSafe, x => SealClassesWhenSafe),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_InsertBlankLineBeforeReturnAndThrowStatements, x => InsertBlankLineBeforeReturnAndThrowStatements),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_FormatRazorComponents, x => FormatRazorComponents),
                new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_RazorAttributeWrapThreshold, x => RazorAttributeWrapThreshold),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_AiXmlDocumentationEnabled, x => AiXmlDocumentationEnabled),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_AiXmlDocumentationEndpointUrl, x => AiXmlDocumentationEndpointUrl),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_AiXmlDocumentationApiKeyEncrypted, x => AiXmlDocumentationApiKeyEncryptedStore),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_AiXmlDocumentationApiKeyHeader, x => AiXmlDocumentationApiKeyHeader),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_AiXmlDocumentationModel, x => AiXmlDocumentationModel),
                new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_AiXmlDocumentationTimeoutSeconds, x => AiXmlDocumentationTimeoutSeconds),
                new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_AiXmlDocumentationMaxMethodsPerFile, x => AiXmlDocumentationMaxMethodsPerFile),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_AiXmlDocumentationPreviewChanges, x => AiXmlDocumentationPreviewChanges),
                new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_AiXmlDocumentationMaxRequestsPerCleanup, x => AiXmlDocumentationMaxRequestsPerCleanup),
                new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_AiXmlDocumentationMaxInputCharsPerMethod, x => AiXmlDocumentationMaxInputCharsPerMethod),
                new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_AiXmlDocumentationMaxTokensPerRequest, x => AiXmlDocumentationMaxTokensPerRequest),
                new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_AiXmlDocumentationMaxEstimatedTokensPerCleanup, x => AiXmlDocumentationMaxEstimatedTokensPerCleanup),
                new SettingToOptionMapping<int, int>(x => ActiveSettings.Cleaning_AiXmlDocumentationGlobalTimeoutSeconds, x => AiXmlDocumentationGlobalTimeoutSeconds),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_AiXmlDocumentationAllowDeterministicFallback, x => AiXmlDocumentationAllowDeterministicFallback),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_AiXmlDocumentationIgnoreGeneratedCode, x => AiXmlDocumentationIgnoreGeneratedCode),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_AiXmlDocumentationIgnoreObsolete, x => AiXmlDocumentationIgnoreObsolete),
                new SettingToOptionMapping<bool, bool>(x => ActiveSettings.Cleaning_AiXmlDocumentationIgnoreTestMethods, x => AiXmlDocumentationIgnoreTestMethods),
                new SettingToOptionMapping<string, string>(x => ActiveSettings.Cleaning_AiXmlDocumentationIgnorePattern, x => AiXmlDocumentationIgnorePattern),
            };
        }

        #endregion Constructors

        #region Overrides of OptionsPageViewModel

        /// <summary>
        /// Gets the header.
        /// </summary>
        public override string Header => Resources.CleaningUpdateViewModel_Update;

        #endregion Overrides of OptionsPageViewModel

        #region Options

        /// <summary>
        /// Gets or sets the position of the file header.
        /// </summary>
        public HeaderPosition HeaderPosition
        {
            get { return GetPropertyValue<HeaderPosition>(); }
            set { SetPropertyValue(value); }
        }

        #endregion Overrides of OptionsPageViewModel

        #region Options

        /// <summary>
        /// Gets or sets the position of the file header.
        /// </summary>
        public HeaderUpdateMode HeaderUpdateMode
        {
            get { return GetPropertyValue<HeaderUpdateMode>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the flag indicating if accessors should be updated to both be single line
        /// or multi line.
        /// </summary>
        public bool UpdateAccessorsToBothBeSingleLineOrMultiLine
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the flag indicating if end region directives should be updated.
        /// </summary>
        public bool UpdateEndRegionDirectives
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the file header that should be at the top of C++ files.
        /// </summary>
        public string UpdateFileHeaderCPlusPlus
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the file header that should be at the top of C# files.
        /// </summary>
        public string UpdateFileHeaderCSharp
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the file header that should be at the top of CSS files.
        /// </summary>
        public string UpdateFileHeaderCSS
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the file header that should be at the top of F# files.
        /// </summary>
        public string UpdateFileHeaderFSharp
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the file header that should be at the top of HTML files.
        /// </summary>
        public string UpdateFileHeaderHTML
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the file header that should be at the top of JavaScript files.
        /// </summary>
        public string UpdateFileHeaderJavaScript
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the file header that should be at the top of JSON files.
        /// </summary>
        public string UpdateFileHeaderJSON
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the file header that should be at the top of LESS files.
        /// </summary>
        public string UpdateFileHeaderLESS
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the file header that should be at the top of PHP files.
        /// </summary>
        public string UpdateFileHeaderPHP
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the file header that should be at the top of PowerShell files.
        /// </summary>
        public string UpdateFileHeaderPowerShell
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the file header that should be at the top of R files.
        /// </summary>
        public string UpdateFileHeaderR
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the file header that should be at the top of SCSS files.
        /// </summary>
        public string UpdateFileHeaderSCSS
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the file header that should be at the top of TypeScript files.
        /// </summary>
        public string UpdateFileHeaderTypeScript
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the file header that should be at the top of VB files.
        /// </summary>
        public string UpdateFileHeaderVisualBasic
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the file header that should be at the top of XAML files.
        /// </summary>
        public string UpdateFileHeaderXAML
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the file header that should be at the top of XML files.
        /// </summary>
        public string UpdateFileHeaderXML
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the flag indicating if single line methods should be updated.
        /// </summary>
        public bool UpdateSingleLineMethods
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the flag indicating if block-scoped namespaces should be converted to file-scoped.
        /// </summary>
        public bool ConvertToFileScopedNamespace
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the flag indicating if local variable declarations should be converted to
        /// <c>var</c> when the type is apparent from the right-hand side.
        /// </summary>
        public bool ConvertToVarWhenApparent
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the flag indicating if <c>List&lt;T&gt;</c> and array initializations
        /// should be converted to the C# 12 collection expression syntax.
        /// </summary>
        public bool ConvertToCollectionExpressions
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the flag indicating if direct <c>new JsonSerializerOptions()</c>
        /// allocations in <c>JsonSerializer.*</c> calls should be replaced with <c>null</c>
        /// (conservative CA1869-focused optimization).
        /// </summary>
        public bool ReuseJsonSerializerOptionsForCA1869
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the flag indicating if single-statement lambda block bodies should be
        /// simplified to expression bodies (including removing unnecessary <c>return</c>).
        /// </summary>
        public bool SimplifySingleStatementLambdas
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the flag indicating if fields provably never written outside their
        /// constructor should have the <c>readonly</c> modifier added.
        /// </summary>
        public bool MakeFieldsReadonlyWhenSafe
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the flag indicating if classes provably not derived from within the same
        /// file should have the <c>sealed</c> modifier added. This is a single-file heuristic and
        /// changes the API surface, so it defaults to disabled.
        /// </summary>
        public bool SealClassesWhenSafe
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the flag indicating if a blank line should be inserted before
        /// <c>return</c> and <c>throw</c> statements that are preceded by other statements within
        /// the same block, to visually separate the exit/failure path from the preceding logic.
        /// </summary>
        public bool InsertBlankLineBeforeReturnAndThrowStatements
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the flag indicating if safe Razor component formatting should run for
        /// <c>.razor</c> files during cleanup.
        /// </summary>
        public bool FormatRazorComponents
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the number of attributes a Razor tag may keep inline before it is wrapped.
        /// </summary>
        public int RazorAttributeWrapThreshold
        {
            get { return GetPropertyValue<int>(); }
            set
            {
                if (value >= 0)
                {
                    SetPropertyValue(value);
                }
            }
        }

        /// <summary>
        /// Gets or sets the flag indicating if AI-assisted XML documentation generation should run
        /// during C# cleanup.
        /// </summary>
        public bool AiXmlDocumentationEnabled
        {
            get { return GetPropertyValue<bool>(); }
            set
            {
                if (IsEnabledAiXmlDocumentationEnabled)
                {
                    SetPropertyValue(value);
                }
                else if (value)
                {
                    SetPropertyValue(false);
                }
            }
        }

        /// <summary>
        /// Gets or sets the OpenAI-compatible endpoint URL for XML documentation generation.
        /// </summary>
        public string AiXmlDocumentationEndpointUrl
        {
            get { return GetPropertyValue<string>(); }
            set
            {
                if (SetPropertyValue(value))
                {
                    InvalidateAiXmlDocumentationAvailability();
                }
            }
        }

        /// <summary>
        /// Gets or sets the protected persisted API key value.
        /// </summary>
        public string AiXmlDocumentationApiKeyEncryptedStore
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the API key for the OpenAI-compatible endpoint.
        /// </summary>
        public string AiXmlDocumentationApiKey
        {
            get { return GetPropertyValue<string>(); }
            set
            {
                if (SetPropertyValue(value))
                {
                    InvalidateAiXmlDocumentationAvailability();
                }
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of methods per file processed by AI XML documentation.
        /// </summary>
        public int AiXmlDocumentationMaxMethodsPerFile
        {
            get { return GetPropertyValue<int>(); }
            set
            {
                if (value > 0)
                {
                    SetPropertyValue(value);
                }
            }
        }

        public bool AiXmlDocumentationPreviewChanges
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        public int AiXmlDocumentationMaxRequestsPerCleanup
        {
            get { return GetPropertyValue<int>(); }
            set
            {
                if (value > 0)
                {
                    SetPropertyValue(value);
                }
            }
        }

        public int AiXmlDocumentationMaxInputCharsPerMethod
        {
            get { return GetPropertyValue<int>(); }
            set
            {
                if (value > 0)
                {
                    SetPropertyValue(value);
                }
            }
        }

        public int AiXmlDocumentationMaxTokensPerRequest
        {
            get { return GetPropertyValue<int>(); }
            set
            {
                if (value > 0)
                {
                    SetPropertyValue(value);
                }
            }
        }

        public int AiXmlDocumentationMaxEstimatedTokensPerCleanup
        {
            get { return GetPropertyValue<int>(); }
            set
            {
                if (value > 0)
                {
                    SetPropertyValue(value);
                }
            }
        }

        public int AiXmlDocumentationGlobalTimeoutSeconds
        {
            get { return GetPropertyValue<int>(); }
            set
            {
                if (value > 0)
                {
                    SetPropertyValue(value);
                }
            }
        }

        public bool AiXmlDocumentationAllowDeterministicFallback
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        public bool AiXmlDocumentationIgnoreGeneratedCode
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        public bool AiXmlDocumentationIgnoreObsolete
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        public bool AiXmlDocumentationIgnoreTestMethods
        {
            get { return GetPropertyValue<bool>(); }
            set { SetPropertyValue(value); }
        }

        public string AiXmlDocumentationIgnorePattern
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the API key header name. Defaults to Authorization.
        /// </summary>
        public string AiXmlDocumentationApiKeyHeader
        {
            get { return GetPropertyValue<string>(); }
            set
            {
                if (SetPropertyValue(value))
                {
                    InvalidateAiXmlDocumentationAvailability();
                }
            }
        }

        /// <summary>
        /// Gets or sets the model (or deployment) name sent to the endpoint.
        /// </summary>
        public string AiXmlDocumentationModel
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets or sets the request timeout in seconds for AI calls.
        /// </summary>
        public int AiXmlDocumentationTimeoutSeconds
        {
            get { return GetPropertyValue<int>(); }
            set
            {
                if (value > 0)
                {
                    SetPropertyValue(value);
                }
            }
        }

        #endregion Options

        #region AI XML Documentation Connection

        private bool _aiXmlDocumentationConnectionSucceeded;
        private DelegateCommand _testAiXmlDocumentationConnectionCommand;

        /// <summary>
        /// Gets the test-connection command for the AI XML documentation endpoint.
        /// </summary>
        public DelegateCommand TestAiXmlDocumentationConnectionCommand => _testAiXmlDocumentationConnectionCommand
            ?? (_testAiXmlDocumentationConnectionCommand = new DelegateCommand(OnTestAiXmlDocumentationConnectionCommandExecuted));

        /// <summary>
        /// Gets a short status text for the endpoint connectivity test.
        /// </summary>
        public string AiXmlDocumentationConnectionStatus
        {
            get { return GetPropertyValue<string>(); }
            set { SetPropertyValue(value); }
        }

        /// <summary>
        /// Gets a flag indicating whether endpoint URL and key are configured.
        /// </summary>
        public bool IsAiXmlDocumentationEndpointConfigured => OpenAiCompatibleClient.IsEndpointConfigured(
            AiXmlDocumentationEndpointUrl,
            AiXmlDocumentationApiKey);

        /// <summary>
        /// Gets a flag indicating if AI XML documentation generation can be enabled.
        /// </summary>
        public bool IsEnabledAiXmlDocumentationEnabled => IsAiXmlDocumentationEndpointConfigured && _aiXmlDocumentationConnectionSucceeded;

        public override void LoadSettings()
        {
            base.LoadSettings();

            var decryptedKey = SecretProtectionHelper.UnprotectForCurrentUser(AiXmlDocumentationApiKeyEncryptedStore);
            if (string.IsNullOrWhiteSpace(decryptedKey) && !string.IsNullOrWhiteSpace(ActiveSettings.Cleaning_AiXmlDocumentationApiKey))
            {
                decryptedKey = ActiveSettings.Cleaning_AiXmlDocumentationApiKey;
            }

            AiXmlDocumentationApiKey = decryptedKey;

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

            if (AiXmlDocumentationMaxEstimatedTokensPerCleanup <= 0)
            {
                AiXmlDocumentationMaxEstimatedTokensPerCleanup = 8000;
            }

            if (AiXmlDocumentationGlobalTimeoutSeconds <= 0)
            {
                AiXmlDocumentationGlobalTimeoutSeconds = 60;
            }

            _aiXmlDocumentationConnectionSucceeded = IsCachedConnectionSuccess();
            AiXmlDocumentationConnectionStatus = !IsAiXmlDocumentationEndpointConfigured
                ? "Set endpoint URL and API key to enable test."
                : (_aiXmlDocumentationConnectionSucceeded ? "Using cached successful connection test." : "Not tested in this session.");

            RaisePropertyChanged(nameof(IsAiXmlDocumentationEndpointConfigured));
            RaisePropertyChanged(nameof(IsEnabledAiXmlDocumentationEnabled));
        }

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
                _aiXmlDocumentationConnectionSucceeded = false;
                AiXmlDocumentationEnabled = false;
                AiXmlDocumentationConnectionStatus = "Endpoint URL or API key is missing/invalid.";
                RaisePropertyChanged(nameof(IsEnabledAiXmlDocumentationEnabled));
                return;
            }

            string message;
            _aiXmlDocumentationConnectionSucceeded = AiXmlDocumentationLogic.TryValidateConnection(
                AiXmlDocumentationEndpointUrl,
                AiXmlDocumentationApiKey,
                AiXmlDocumentationApiKeyHeader,
                AiXmlDocumentationModel,
                AiXmlDocumentationTimeoutSeconds,
                out message);

            if (!_aiXmlDocumentationConnectionSucceeded)
            {
                AiXmlDocumentationEnabled = false;
                ClearCachedConnectionSuccess();
            }
            else
            {
                CacheSuccessfulConnection();
            }

            AiXmlDocumentationConnectionStatus = _aiXmlDocumentationConnectionSucceeded
                ? "Connection successful."
                : $"Connection failed: {message}";

            RaisePropertyChanged(nameof(IsEnabledAiXmlDocumentationEnabled));
        }

        private void InvalidateAiXmlDocumentationAvailability()
        {
            ClearCachedConnectionSuccess();
            _aiXmlDocumentationConnectionSucceeded = false;
            AiXmlDocumentationEnabled = false;
            AiXmlDocumentationConnectionStatus = IsAiXmlDocumentationEndpointConfigured
                ? "Configuration changed. Please test connection."
                : "Set endpoint URL and API key to enable test.";
            RaisePropertyChanged(nameof(IsAiXmlDocumentationEndpointConfigured));
            RaisePropertyChanged(nameof(IsEnabledAiXmlDocumentationEnabled));
        }

        private string GetConnectionFingerprint()
        {
            return string.Join("|",
                AiXmlDocumentationEndpointUrl ?? string.Empty,
                AiXmlDocumentationApiKeyHeader ?? string.Empty,
                AiXmlDocumentationModel ?? string.Empty,
                AiXmlDocumentationTimeoutSeconds,
                (AiXmlDocumentationApiKey ?? string.Empty).GetHashCode());
        }

        private bool IsCachedConnectionSuccess()
        {
            if (!IsAiXmlDocumentationEndpointConfigured)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_lastSuccessfulConnectionFingerprint))
            {
                return false;
            }

            if (!string.Equals(_lastSuccessfulConnectionFingerprint, GetConnectionFingerprint(), StringComparison.Ordinal))
            {
                return false;
            }

            return DateTime.UtcNow - _lastSuccessfulConnectionUtc <= SuccessfulConnectionCacheDuration;
        }

        private void CacheSuccessfulConnection()
        {
            _lastSuccessfulConnectionFingerprint = GetConnectionFingerprint();
            _lastSuccessfulConnectionUtc = DateTime.UtcNow;
        }

        private void ClearCachedConnectionSuccess()
        {
            _lastSuccessfulConnectionFingerprint = null;
            _lastSuccessfulConnectionUtc = DateTime.MinValue;
        }

        #endregion AI XML Documentation Connection
    }
}