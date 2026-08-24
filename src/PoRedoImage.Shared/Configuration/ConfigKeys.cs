namespace PoRedoImage.Shared.Configuration;

/// <summary>
/// Every configuration key the solution reads, in one place (§1 "Zero magic strings").
/// </summary>
/// <remarks>
/// These were previously 74 string literals spread across the Web and Infrastructure projects, where
/// a typo produced a silent null rather than a compile error and drift went unnoticed — the
/// <see cref="ComputerVisionApiKey"/> / <see cref="ComputerVisionKeyLegacy"/> pair below is exactly
/// that kind of divergence, caught only because it was collected here.
/// <para>
/// Lives in Shared because both Web (a Web SDK project) and Infrastructure (a plain SDK project)
/// need it, and Infrastructure cannot reference Web. Options classes still exist for the sections
/// with validation and hot-reload needs (<c>OpenAiOptions</c>, <c>StorageOptions</c>,
/// <c>AiPricingOptions</c>); these constants serve the remaining direct
/// <c>IConfiguration["..."]</c> reads, which are deliberate — several re-read on every call to pick
/// up rotated Key Vault secrets on singleton services.
/// </para>
/// </remarks>
public static class ConfigKeys
{
    /// <summary>Key Vault endpoint, read directly by the host bootstrapper before DI exists.</summary>
    public const string KeyVaultUri = "KeyVault:Uri";

    /// <summary>Key Vault endpoint under its App Service environment-variable name.</summary>
    public const string AzureKeyVaultEndpoint = "AZURE_KEY_VAULT_ENDPOINT";

    // ── Application Insights ────────────────────────────────────────────
    public const string ApplicationInsightsConnectionString = "ApplicationInsights:ConnectionString";
    public const string ApplicationInsightsStagingConnectionString = "ApplicationInsights:StagingConnectionString";
    public const string ApplicationInsightsSamplingRatio = "ApplicationInsights:SamplingRatio";

    /// <summary>Platform-injected connection string (App Service sets this automatically).</summary>
    public const string AppInsightsConnectionStringEnv = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    /// <summary>Deprecated platform-injected instrumentation key; still read as a last resort.</summary>
    public const string AppInsightsInstrumentationKeyEnv = "APPINSIGHTS_INSTRUMENTATIONKEY";

    // ── Entra ID / AzureAd ──────────────────────────────────────────────
    public const string AzureAdAllowedTenantIds = "AzureAd:AllowedTenantIds";
    public const string AzureAdCallbackPath = "AzureAd:CallbackPath";
    public const string AzureAdClientId = "AzureAd:ClientId";
    public const string AzureAdClientSecret = "AzureAd:ClientSecret";
    public const string AzureAdSignedOutCallbackPath = "AzureAd:SignedOutCallbackPath";
    public const string AzureAdTenantId = "AzureAd:TenantId";

    // ── Azure Computer Vision ───────────────────────────────────────────
    public const string ComputerVisionApiKey = "ComputerVision:ApiKey";

    /// <summary>
    /// Legacy alias for <see cref="ComputerVisionApiKey"/>. Read sites use
    /// <c>ApiKey ?? Key</c> so pre-existing configuration under either spelling keeps working; the Key Vault mapping and
    /// <c>infra/main.bicep</c> only ever provision the <c>ApiKey</c> form.
    /// </summary>
    public const string ComputerVisionKeyLegacy = "ComputerVision:Key";

    public const string ComputerVisionEndpoint = "ComputerVision:Endpoint";
    public const string ComputerVisionMinTagConfidence = "ComputerVision:MinTagConfidence";

    // ── Diagnostics ─────────────────────────────────────────────────────
    public const string DiagnosticsAdminEmails = "Diagnostics:AdminEmails";

    // ── Google (Gemini / Imagen) ────────────────────────────────────────
    public const string GoogleApiKey = "Google:ApiKey";
    public const string GoogleImagen3Model = "Google:Imagen3Model";

    /// <summary>
    /// Lyria music model id. Lyria performs supplied lyrics, unlike instrumental text-to-audio
    /// models. Defaults to the 30-second clip model.
    /// </summary>
    public const string GoogleLyriaModel = "Google:LyriaModel";

    // ── Ollama (local, dev only) ────────────────────────────────────────
    public const string OllamaEndpoint = "Ollama:Endpoint";
    public const string OllamaVisionModel = "Ollama:VisionModel";

    /// <summary>
    /// Names a local model for free-form reasoning. Setting it switches <c>IChatCompletionService</c>
    /// from Azure OpenAI to Ollama for the whole process — see <c>OllamaChatCompletionService</c>
    /// for why that is a deployment choice rather than a per-request one.
    /// </summary>
    public const string OllamaChatModel = "Ollama:ChatModel";

    // ── Azure OpenAI ────────────────────────────────────────────────────
    public const string OpenAiChatCompletionsDeployment = "OpenAI:ChatCompletionsDeployment";
    public const string OpenAiEndpoint = "OpenAI:Endpoint";
    public const string OpenAiKey = "OpenAI:Key";

    // ── Storage ─────────────────────────────────────────────────────────
    public const string StorageConnectionString = "Storage:ConnectionString";

    // ── Vision ──────────────────────────────────────────────────────────
    /// <summary>
    /// Consult a second vision model and merge its answer into the scene snapshot. Off by default:
    /// it roughly doubles the cost of the vision step for a marginal gain once OCR supplies facts.
    /// </summary>
    public const string VisionSecondOpinion = "Vision:SecondOpinion";

    // ── Feature flags ───────────────────────────────────────────────────
    /// <summary>
    /// Historic image-generation provider switch. Google Gemini/Imagen is now the only provider, so
    /// nothing reads this: the key is kept only so an existing App Service setting or Key Vault
    /// reference does not fail configuration binding after the HuggingFace removal (2026-08).
    /// </summary>
    public const string ImageGenProvider = "ImageGen:Provider";

    public const string MocksUseMockAi = "Mocks:UseMockAi";
    public const string AuthEnableFakeAuth = "Auth:EnableFakeAuth";
}
