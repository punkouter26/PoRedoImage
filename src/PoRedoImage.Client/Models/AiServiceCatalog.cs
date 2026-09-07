using PoRedoImage.Client.LocalAi;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Client.Models;

/// <summary>
/// The provider options offered per capability — the single source the picker renders from.
/// </summary>
/// <remarks>
/// Browser entries are derived from <see cref="LocalModelRegistry"/> rather than restated, so that
/// registry remains the one catalog of browser models (NET_RULES §5) and download sizes cannot drift
/// between the two.
/// </remarks>
public static class AiServiceCatalog
{
    public const string CategoryRemote = "Remote";
    public const string CategoryBrowser = "Web Browser";
    public const string CategoryOllama = "Ollama";

    private static AiProviderOption BrowserOption(string id, LocalCapability capability)
    {
        var model = LocalModelRegistry.DefaultFor(capability)
            ?? throw new InvalidOperationException($"No local model registered for {capability}.");

        return new AiProviderOption(
            id,
            model.DisplayName,
            CategoryBrowser,
            $"~{model.ApproxDownloadMb} MB first run, then free",
            ExecutesInBrowser: true);
    }

    private static readonly Dictionary<AiCapability, IReadOnlyList<AiProviderOption>> Catalog = new()
    {
        [AiCapability.AnalyzeImage] =
        [
            new(AiProviderIds.AzureComputerVision, "Azure Computer Vision", CategoryRemote, "Fastest, uses your API quota"),
            new(AiProviderIds.AzureOpenAiVision, "Azure OpenAI vision", CategoryRemote, "Best descriptions, one call"),
            new(AiProviderIds.GeminiVision, "Google Gemini Vision", CategoryRemote, "Multimodal flash, ~$0.0003"),
            BrowserOption(AiProviderIds.BrowserFlorence2, LocalCapability.Vision),
            new(AiProviderIds.OllamaVision, "Ollama", CategoryOllama, "Local service", DevOnly: true),
        ],

        // One entry, and that is the honest count. There used to be a second, "Gemini Imagen 3 Fast
        // — Google fast tier, ~$0.020/image". It never ran: the fast tier is constructed only when
        // Google:Imagen3FastModel is configured, and that key was set nowhere — not appsettings, not
        // infra/main.bicep, not Key Vault — so ImageGenerationRouter always fell through to the
        // standard service. Picking it billed the standard ~$0.039 rate while the label promised
        // half that. A priced choice that silently resolves to the other option is worse than no
        // choice, so it is gone until the fast model is actually configured.
        [AiCapability.GenerateImage] =
        [
            new(AiProviderIds.GeminiImagen3, "Gemini Imagen 3", CategoryRemote, "Google, ~$0.039/image"),
        ],

        // Browser-local enhancement is implemented now: the client writes the image-generation
        // prompt on-device and the server skips its own call (ImageAnalysisRequest
        // .PrecomputedEnhancedPrompt). Qwen2.5-0.5B was already in LocalModelRegistry and wired to
        // nothing before this.
        [AiCapability.EnhanceDescription] =
        [
            new(AiProviderIds.AzureOpenAi, "Azure OpenAI", CategoryRemote, "Fastest, uses your API quota"),
            BrowserOption(AiProviderIds.BrowserQwen25, LocalCapability.Text),
        ],

        [AiCapability.SceneDetail] =
        [
            new(AiProviderIds.AzureComputerVision, "Azure Computer Vision", CategoryRemote, "Only provider configured"),
        ],

        [AiCapability.CreateAudio] =
        [
            new(AiProviderIds.GoogleLyria, "Google Lyria 3", CategoryRemote, "Only provider configured"),
        ],
    };

    /// <summary>Human label for a capability, used as the row heading.</summary>
    public static string LabelFor(AiCapability capability) => capability switch
    {
        AiCapability.AnalyzeImage => "Analyze image",
        AiCapability.GenerateImage => "Generate image",
        AiCapability.EnhanceDescription => "Enhance description & captions",
        AiCapability.SceneDetail => "Scene detail (OCR)",
        AiCapability.CreateAudio => "Create audio",
        _ => capability.ToString(),
    };

    /// <summary>Every capability, in the order the picker renders them.</summary>
    public static IReadOnlyList<AiCapability> All { get; } =
    [
        AiCapability.AnalyzeImage,
        AiCapability.GenerateImage,
        AiCapability.EnhanceDescription,
        AiCapability.SceneDetail,
        AiCapability.CreateAudio,
    ];

    /// <summary>Options offered for a capability, including any marked dev-only.</summary>
    public static IReadOnlyList<AiProviderOption> OptionsFor(AiCapability capability) => Catalog[capability];

    /// <summary>
    /// Options offered for a capability in a given environment. Outside Development the
    /// <see cref="AiProviderOption.DevOnly"/> entries are withheld, because they name a backend the
    /// visitor's machine cannot reach. Never returns empty: a capability whose every option is
    /// dev-only keeps its full list rather than rendering a selector with nothing in it.
    /// </summary>
    public static IReadOnlyList<AiProviderOption> OptionsFor(AiCapability capability, bool includeDevOnly)
    {
        var all = Catalog[capability];
        if (includeDevOnly) return all;

        var offered = all.Where(o => !o.DevOnly).ToArray();
        return offered.Length > 0 ? offered : all;
    }

    /// <summary>The default option — the first registered, which is the preferred one.</summary>
    public static AiProviderOption DefaultFor(AiCapability capability) => Catalog[capability][0];

    /// <summary>Looks up an option by capability and id, or null when unknown.</summary>
    public static AiProviderOption? Find(AiCapability capability, string? id) =>
        Catalog[capability].FirstOrDefault(o => o.Id == id);
}
