using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Server.Services.HealthChecks;

/// <summary>
/// Health check for Azure OpenAI API connectivity.
/// Verifies the API endpoint and key are valid.
/// </summary>
public class OpenAIHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAIHealthCheck> _logger;

    public OpenAIHealthCheck(
        IConfiguration configuration,
        ILogger<OpenAIHealthCheck> logger)
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
            var endpoint = _configuration["OpenAI:Endpoint"];
            var apiKey = _configuration["OpenAI:ApiKey"];

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("OpenAI API credentials are not configured");
                return Task.FromResult(HealthCheckResult.Degraded("API credentials not configured"));
            }

            // Verify endpoint format
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            {
                _logger.LogError("OpenAI endpoint is not a valid URL: {Endpoint}", endpoint);
                return Task.FromResult(HealthCheckResult.Unhealthy($"Invalid endpoint URL: {endpoint}"));
            }

            _logger.LogInformation("OpenAI health check succeeded");
            return Task.FromResult(HealthCheckResult.Healthy("OpenAI API is configured"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Unable to verify OpenAI API",
                ex));
        }
    }
}
