using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PoRedoImage.Web.Features.ImageAnalysis;

/// <summary>
/// Health check that verifies both connectivity and authentication for Azure Computer Vision.
/// Sends a HEAD request with the configured API key so that auth failures (401/403)
/// are surfaced as Degraded rather than falsely reported as Healthy.
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

        if (string.IsNullOrWhiteSpace(endpoint))
            return HealthCheckResult.Degraded("ComputerVision:Endpoint is not configured. In Production this usually means the Key Vault reference (ComputerVision__Endpoint) did not resolve — verify the secret 'PoRedoImage-ComputerVision-Endpoint' exists in 'kv-poshared' and the app's managed identity has 'Key Vault Secrets User' on the vault.");
        if (string.IsNullOrWhiteSpace(apiKey))
            return HealthCheckResult.Degraded("ComputerVision:ApiKey is not configured. In Production this usually means the Key Vault reference (ComputerVision__ApiKey) did not resolve.");

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            // Probe the Azure AI Vision 4.0 endpoint path that the ImageAnalysisClient SDK uses.
            // GET returns 405 (Method Not Allowed) when auth is valid, 401/403 when the key is wrong.
            var probeUrl = endpoint.TrimEnd('/') + "/computervision/imageanalysis:analyze?api-version=2024-02-01&features=caption";
            var request = new HttpRequestMessage(HttpMethod.Get, probeUrl);
            request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", apiKey);
            var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            var statusCode = (int)response.StatusCode;
            if (statusCode is 401 or 403)
                return HealthCheckResult.Degraded(
                    $"ComputerVision endpoint reachable but authentication failed (HTTP {statusCode}). Check ComputerVision:ApiKey.");

            // 405 Method Not Allowed or any non-401/403 = auth is valid, endpoint is reachable
            return HealthCheckResult.Healthy(
                $"ComputerVision endpoint reachable (HTTP {statusCode})");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"ComputerVision endpoint probe failed (endpoint='{endpoint}')", ex);
        }
    }
}
