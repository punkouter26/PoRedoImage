using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Web.Features.BulkGenerate;

public class Imagen3HealthCheck : IHealthCheck
{
    private readonly IImagen3Service _imagen3;

    public Imagen3HealthCheck(IImagen3Service imagen3)
    {
        _imagen3 = imagen3;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_imagen3.IsConfigured)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Gemini image generation not configured (Google:ApiKey missing). Image generation is unavailable."));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Gemini image generation configured (Google AI Studio REST API)."));
    }
}
