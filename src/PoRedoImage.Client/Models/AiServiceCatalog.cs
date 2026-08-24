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
            // One call that returns a real caption AND tags. Azure CV's Caption feature is
            // region-limited and unavailable on the configured resource, so that backend always
            // falls back to joining its top tags — a keyword list, not a description. See
            // OpenAiVisionService.
            new(AiProviderIds.AzureOpenAiVision, "Azure OpenAI vision", CategoryRemote, "Best descriptions, one call"),
            BrowserOption(AiProviderIds.BrowserFlorence2, LocalCapability.Vision),
            new(AiProviderIds.OllamaVision, "Ollama", CategoryOllama, "Local service, dev only"),
        ],

        [AiCapability.GenerateImage] =
        [
            // Google is the only image-generation provider; HuggingFace was removed in 2026-08.
            new(AiProviderIds.GeminiImagen3, "Gemini Imagen 3", CategoryRemote, "Google, ~$0.039/image (only)"),
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

        // The Style Director agents reason through IChatCompletionService, which is Azure OpenAI —
        // the same deployment that reads images for the scene describer.
        [AiCapability.StyleDirector] =
        [
            new(AiProviderIds.AzureOpenAi, "Azure OpenAI", CategoryRemote, "Only provider configured"),
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
        AiCapability.StyleDirector => "Style Director",
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
        AiCapability.StyleDirector,
        AiCapability.SceneDetail,
        AiCapability.CreateAudio,
    ];

    /// <summary>Options offered for a capability.</summary>
    public static IReadOnlyList<AiProviderOption> OptionsFor(AiCapability capability) => Catalog[capability];

    /// <summary>The default option — the first registered, which is the preferred one.</summary>
    public static AiProviderOption DefaultFor(AiCapability capability) => Catalog[capability][0];

    /// <summary>Looks up an option by capability and id, or null when unknown.</summary>
    public static AiProviderOption? Find(AiCapability capability, string? id) =>
        Catalog[capability].FirstOrDefault(o => o.Id == id);
}
