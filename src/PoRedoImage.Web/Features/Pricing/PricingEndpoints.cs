using Microsoft.Extensions.Options;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Web.Configuration;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Web.Features.Pricing;

/// <summary>
/// Exposes the active image-generation provider and its indicative per-image pricing so the client
/// can render "≈ $X per image" and a running session estimate. Vertical slice — endpoint co-located.
/// </summary>
public static class PricingEndpoints
{
    public static IEndpointRouteBuilder MapPricingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing", (IConfiguration config, IOptions<AiPricingOptions> pricing) =>
        {
            // Google is the only provider, but the configured value is still read rather than
            // hardcoded: an App Service setting left over from the HuggingFace era would otherwise
            // report a provider label that no longer has a price entry. Anything unrecognised
            // resolves to "google", which is what actually runs.
            var configured = (config[ConfigKeys.ImageGenProvider] ?? "google").Trim().ToLowerInvariant();
            var key = pricing.Value.Providers.ContainsKey(configured) ? configured : "google";
            var p = pricing.Value.Providers.GetValueOrDefault(key);

            return Results.Ok(new AiPricingDto(
                ImageProvider: key,
                ImageProviderLabel: p?.Label ?? key,
                TextToImageUsd: p?.TextToImageUsd ?? 0.039m,
                ImageToImageUsd: p?.ImageToImageUsd ?? 0.039m,
                Currency: pricing.Value.Currency,
                VisionAnalysisUsd: pricing.Value.VisionAnalysisUsd,
                TextReasoningUsd: pricing.Value.TextReasoningUsd,
                MusicGenerationUsd: pricing.Value.MusicGenerationUsd));
        })
        .WithName("GetAiPricing")
        .WithTags("Pricing")
        .WithSummary("Active AI providers + indicative pricing for the UI estimate");

        return app;
    }
}
