namespace PoRedoImage.Shared.DTOs;

/// <summary>
/// Estimated per-image pricing for the currently-active image-generation provider, surfaced to the
/// client so the UI can show "≈ $X per image" and a running session estimate as the user generates.
/// Prices are indicative list prices from config (AiPricing section), not billed amounts.
/// </summary>
public record AiPricingDto(
    string ImageProvider,
    string ImageProviderLabel,
    decimal TextToImageUsd,
    decimal ImageToImageUsd,
    string Currency);
