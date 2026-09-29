namespace PoRedoImage.Shared.DTOs;

/// <summary>
/// Estimated pricing for AI services across capabilities, surfaced to the
/// client so the UI can show running session totals as different AI services are used.
/// Prices are indicative list prices from config (AiPricing section), not billed amounts.
/// </summary>
public sealed record AiPricingDto(
    string ImageLabel,
    decimal ImageGenerationUsd,
    string Currency,
    decimal VisionAnalysisUsd,
    decimal TextReasoningUsd,
    decimal MusicGenerationUsd,
    decimal VideoGenerationUsd);

