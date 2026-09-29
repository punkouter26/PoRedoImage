using Microsoft.Extensions.Options;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Web.Configuration;

namespace PoRedoImage.Web.Features.Pricing;

/// <summary>
/// Exposes indicative per-action pricing so the client can render "≈ $X per image" and a running
/// session estimate. Vertical slice — endpoint co-located.
/// </summary>
public static class PricingEndpoints
{
    public static IEndpointRouteBuilder MapPricingEndpoints(this IEndpointRouteBuilder app)
    {
        // The single source of every price the client shows: AiPricingOptions (appsettings) is
        // bound here and nowhere else, so an estimate can never disagree with a hardcoded copy.
        app.MapGet("/api/pricing", (IOptions<AiPricingOptions> pricing) =>
        {
            var p = pricing.Value;
            return Results.Ok(new AiPricingDto(
                ImageLabel: p.ImageLabel,
                ImageGenerationUsd: p.ImageGenerationUsd,
                Currency: p.Currency,
                VisionAnalysisUsd: p.VisionAnalysisUsd,
                TextReasoningUsd: p.TextReasoningUsd,
                MusicGenerationUsd: p.MusicGenerationUsd,
                VideoGenerationUsd: p.VideoGenerationUsd));
        })
        .WithName("GetAiPricing")
        .WithTags("Pricing")
        .WithSummary("Active AI providers + indicative pricing for the UI estimate");

        return app;
    }
}
