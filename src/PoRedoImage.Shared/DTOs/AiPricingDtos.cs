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
    decimal MusicGenerationUsd = 0.040m,
    // Veo 3.1 Lite at 720p is $0.05/sec and every clip is 8 seconds, so one render is $0.40 —
    // an order of magnitude above any other action here, which is exactly why it is metered.
    decimal VideoGenerationUsd = 0.40m);

