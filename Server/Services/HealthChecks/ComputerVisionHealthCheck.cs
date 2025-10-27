using Microsoft.Extensions.Diagnostics.HealthChecks;
using Azure.AI.Vision.ImageAnalysis;
using Azure;

namespace Server.Services.HealthChecks;

/// <summary>
/// Health check for Azure Computer Vision API connectivity.
/// Verifies the API endpoint and key are valid.
/// </summary>
public class ComputerVisionHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ComputerVisionHealthCheck> _logger;

    public ComputerVisionHealthCheck(
        IConfiguration configuration,
        ILogger<ComputerVisionHealthCheck> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = _configuration["ComputerVision:Endpoint"];
            var apiKey = _configuration["ComputerVision:ApiKey"];

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("Computer Vision API credentials are not configured");
                return Task.FromResult(HealthCheckResult.Degraded("API credentials not configured"));
            }

            // Verify endpoint format
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            {
                _logger.LogError("Computer Vision endpoint is not a valid URL: {Endpoint}", endpoint);
                return Task.FromResult(HealthCheckResult.Unhealthy($"Invalid endpoint URL: {endpoint}"));
            }

            _logger.LogInformation("Computer Vision health check succeeded");
            return Task.FromResult(HealthCheckResult.Healthy("Computer Vision API is configured"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Computer Vision health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Unable to verify Computer Vision API",
                ex));
        }
    }
}
