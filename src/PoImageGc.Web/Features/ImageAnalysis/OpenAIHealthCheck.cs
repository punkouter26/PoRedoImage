using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PoImageGc.Web.Features.ImageAnalysis;

/// <summary>
/// Health check that verifies connectivity to Azure OpenAI API.
/// Uses a lightweight HEAD request to confirm network reachability without
/// consuming tokens or incurring any cost.
/// </summary>
public sealed class OpenAIHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public OpenAIHealthCheck(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var endpoint = _configuration["OpenAI:Endpoint"];
        var apiKey = _configuration["OpenAI:Key"];

        if (string.IsNullOrEmpty(endpoint))
            return HealthCheckResult.Unhealthy("OpenAI:Endpoint is not configured");
        if (string.IsNullOrEmpty(apiKey))
            return HealthCheckResult.Unhealthy("OpenAI:Key is not configured");

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
                $"OpenAI endpoint reachable (HTTP {(int)response.StatusCode})");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("OpenAI endpoint is unreachable", ex);
        }
    }
}
