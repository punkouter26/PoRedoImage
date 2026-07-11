using Microsoft.Extensions.Options;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Web.Configuration;

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
            var provider = (config["ImageGen:Provider"] ?? "google").Trim().ToLowerInvariant();
            var key = provider is "hf" ? "huggingface" : provider;
            var p = pricing.Value.Providers.GetValueOrDefault(key);

            return Results.Ok(new AiPricingDto(
                ImageProvider: key,
                ImageProviderLabel: p?.Label ?? key,
                TextToImageUsd: p?.TextToImageUsd ?? 0m,
                ImageToImageUsd: p?.ImageToImageUsd ?? 0m,
                Currency: pricing.Value.Currency));
        })
        .WithName("GetAiPricing")
        .WithTags("Pricing")
        .WithSummary("Active image-generation provider + indicative per-image pricing for the UI estimate");

        return app;
    }
}
