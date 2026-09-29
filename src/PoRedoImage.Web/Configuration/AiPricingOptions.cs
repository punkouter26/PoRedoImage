namespace PoRedoImage.Web.Configuration;

/// <summary>
/// Binds the <c>AiPricing</c> config section — indicative per-action list prices per service,
/// surfaced to the client so the UI can show cost estimates. Not billed amounts; purely informational.
/// </summary>
public sealed class AiPricingOptions
{
    public const string SectionName = "AiPricing";

    public string Currency { get; set; } = "USD";
    public decimal VisionAnalysisUsd { get; set; } = 0.001m;
    public decimal TextReasoningUsd { get; set; } = 0.0015m;
    public decimal MusicGenerationUsd { get; set; } = 0.040m;

    /// <summary>
    /// One 8-second Veo 3.1 Lite clip at 720p: 8 × $0.05/sec. Update together with
    /// <c>Google:VeoModel</c> — a tier change moves this by up to 8×.
    /// </summary>
    public decimal VideoGenerationUsd { get; set; } = 0.40m;

    /// <summary>Label shown next to the per-image price (there is one image provider).</summary>
    public string ImageLabel { get; set; } = "Gemini 2.5 Flash Image";

    /// <summary>One generated image, text-to-image or image-to-image (Gemini prices both the same).</summary>
    public decimal ImageGenerationUsd { get; set; } = 0.039m;
}
