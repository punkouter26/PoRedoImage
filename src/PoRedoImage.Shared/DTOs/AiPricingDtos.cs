namespace PoRedoImage.Shared.DTOs;

/// <summary>
/// Estimated pricing for AI services across capabilities, surfaced to the
/// client so the UI can show running session totals as different AI services are used.
/// Prices are indicative list prices from config (AiPricing section), not billed amounts.
/// </summary>
public sealed record AiPricingDto(
    string ImageProvider,
    string ImageProviderLabel,
    decimal TextToImageUsd,
    decimal ImageToImageUsd,
    string Currency,
    decimal VisionAnalysisUsd = 0.001m,
    decimal TextReasoningUsd = 0.0015m,
    decimal MusicGenerationUsd = 0.040m);

