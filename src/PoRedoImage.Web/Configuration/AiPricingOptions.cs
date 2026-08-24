namespace PoRedoImage.Web.Configuration;

/// <summary>
/// Binds the <c>AiPricing</c> config section — indicative per-action list prices per provider/service,
/// surfaced to the client so the UI can show cost estimates. Not billed amounts; purely informational.
/// </summary>
public sealed class AiPricingOptions
{
    public const string SectionName = "AiPricing";

    public string Currency { get; set; } = "USD";
    public decimal VisionAnalysisUsd { get; set; } = 0.001m;
    public decimal TextReasoningUsd { get; set; } = 0.0015m;
    public decimal MusicGenerationUsd { get; set; } = 0.040m;
    public Dictionary<string, ProviderPricing> Providers { get; set; } = [];

    public sealed class ProviderPricing
    {
        public string Label { get; set; } = string.Empty;
        public decimal TextToImageUsd { get; set; }
        public decimal ImageToImageUsd { get; set; }
    }
}
