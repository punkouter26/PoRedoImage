using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Web.Features.BulkGenerate;

public class Imagen3HealthCheck : IHealthCheck
{
    private readonly IImageGenerationService _imagen3;

    public Imagen3HealthCheck(IImageGenerationService imagen3)
    {
        _imagen3 = imagen3;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_imagen3.IsConfigured)
        {
            // Degraded (not Unhealthy) — missing config is expected in dev when
            // the user has not provided Google:ApiKey. Still functional, just
            // with image generation disabled.
            return Task.FromResult(HealthCheckResult.Degraded(
                "Gemini image generation not configured (Google:ApiKey missing). Image generation is unavailable."));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Gemini image generation configured (Google AI Studio REST API)."));
    }
}
