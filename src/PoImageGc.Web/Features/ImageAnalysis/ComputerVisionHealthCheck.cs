using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PoImageGc.Web.Features.ImageAnalysis;

/// <summary>
/// Health check that verifies connectivity to Azure Computer Vision API.
/// Uses a lightweight HEAD request to confirm network reachability without
/// consuming API quota or incurring cost.
/// </summary>
public sealed class ComputerVisionHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public ComputerVisionHealthCheck(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var endpoint = _configuration["ComputerVision:Endpoint"];
        var apiKey = _configuration["ComputerVision:ApiKey"] ?? _configuration["ComputerVision:Key"];

        if (string.IsNullOrEmpty(endpoint))
            return HealthCheckResult.Unhealthy("ComputerVision:Endpoint is not configured");
        if (string.IsNullOrEmpty(apiKey))
            return HealthCheckResult.Unhealthy("ComputerVision:ApiKey is not configured");

        try
        {
            var client = _httpClientFactory.CreateClient("health");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            // HEAD to the base endpoint: any HTTP response (incl. 401/403) confirms reachability
            var request = new HttpRequestMessage(HttpMethod.Head, endpoint);
            var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            return HealthCheckResult.Healthy(
                $"ComputerVision endpoint reachable (HTTP {(int)response.StatusCode})");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("ComputerVision endpoint is unreachable", ex);
        }
    }
}
