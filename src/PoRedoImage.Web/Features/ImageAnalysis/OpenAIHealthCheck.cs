using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PoRedoImage.Web.Features.ImageAnalysis;

/// <summary>
/// Health check that verifies both connectivity and authentication for Azure OpenAI.
/// Sends a HEAD request with the configured API key so that auth failures (401/403)
/// are surfaced as Degraded rather than falsely reported as Healthy.
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
            return HealthCheckResult.Degraded("OpenAI:Endpoint is not configured (expected in local dev without Azure secrets)");
        if (string.IsNullOrEmpty(apiKey))
            return HealthCheckResult.Degraded("OpenAI:Key is not configured (expected in local dev without Azure secrets)");

        try
        {
            var client = _httpClientFactory.CreateClient("health");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            // Probe the deployments list endpoint — AzureOpenAIClient uses this path with api-key header.
            // GET returns 200 (list) or 404 when auth is valid, 401/403 when the key is wrong.
            var probe = endpoint.TrimEnd('/') + "/openai/deployments?api-version=2024-02-01";
            var request = new HttpRequestMessage(HttpMethod.Get, probe);
            request.Headers.TryAddWithoutValidation("api-key", apiKey);
            var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            var statusCode = (int)response.StatusCode;
            if (statusCode is 401 or 403)
                return HealthCheckResult.Degraded(
                    $"OpenAI endpoint reachable but authentication failed (HTTP {statusCode}). Check OpenAI:Key.");

            return HealthCheckResult.Healthy(
                $"OpenAI endpoint reachable (HTTP {statusCode})");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("OpenAI endpoint is unreachable", ex);
        }
    }
}
