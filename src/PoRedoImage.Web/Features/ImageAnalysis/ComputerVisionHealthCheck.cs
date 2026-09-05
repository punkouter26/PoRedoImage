using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Web.Features.ImageAnalysis;

/// <summary>
/// Health check that verifies connectivity, routing AND authentication for Azure Computer Vision.
/// <remarks>
/// Probes with a deliberately empty <c>{}</c> POST to the analyze route. That is the only request
/// shape that distinguishes all three failure modes without submitting an image (so it processes
/// nothing and bills nothing):
/// <list type="bullet">
///   <item>HTTP 400 <c>InvalidRequest</c> — the route exists and the key was accepted. Healthy.</item>
///   <item>HTTP 401/403 — endpoint reachable, key rejected. Degraded.</item>
///   <item>HTTP 404 — wrong endpoint or wrong API version. Degraded.</item>
/// </list>
/// The previous implementation sent a GET and treated <em>every</em> status except 401/403 as
/// Healthy. On this account a GET to the analyze route returns 404 (the path is POST-only), so the
/// check reported "Healthy (HTTP 404)" unconditionally — it could not have failed, and the
/// post-deploy smoke test that asserts on it was equally incapable of catching a broken deploy.
/// </remarks>
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
        var endpoint = _configuration[ConfigKeys.ComputerVisionEndpoint];
        var apiKey = _configuration[ConfigKeys.ComputerVisionApiKey] ?? _configuration[ConfigKeys.ComputerVisionKeyLegacy];

        // See OpenAIHealthCheck: an unresolved @Microsoft.KeyVault(...) reference is the
        // cold-start race where the app process started before the platform populated
        // the env var. Surface as Degraded (config pending) so the smoke test can tell
        // it apart from a real Unhealthy (probe threw). See ADR-017 + ADR-026.
        if (string.IsNullOrWhiteSpace(endpoint) || IsUnresolvedKeyVaultReference(endpoint))
            return HealthCheckResult.Degraded("ComputerVision:Endpoint is not configured. In Production this usually means the Key Vault reference (ComputerVision__Endpoint) did not resolve — verify the secret 'PoRedoImage-ComputerVision-Endpoint' exists in 'kv-poshared' and the app's managed identity has 'Key Vault Secrets User' on the vault.");
        if (string.IsNullOrWhiteSpace(apiKey) || IsUnresolvedKeyVaultReference(apiKey))
            return HealthCheckResult.Degraded("ComputerVision:ApiKey is not configured. In Production this usually means the Key Vault reference (ComputerVision__ApiKey) did not resolve.");

        static bool IsUnresolvedKeyVaultReference(string s) =>
            s.StartsWith("@Microsoft.KeyVault(", StringComparison.OrdinalIgnoreCase);

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            // Empty JSON body: the service validates auth and routing before it looks for an image,
            // so this returns 400 InvalidRequest on success without analysing (or billing for) one.
            var probeUrl = endpoint.TrimEnd('/') + "/computervision/imageanalysis:analyze?api-version=2024-02-01&features=tags";
            using var request = new HttpRequestMessage(HttpMethod.Post, probeUrl)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", apiKey);
            var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            var statusCode = (int)response.StatusCode;

            // 400 is the success signal here — request rejected on content, not on identity.
            if (statusCode == 400)
                return HealthCheckResult.Healthy("ComputerVision endpoint reachable and key accepted.");

            if (statusCode is 401 or 403)
                return HealthCheckResult.Degraded(
                    $"ComputerVision endpoint reachable but authentication failed (HTTP {statusCode}). Check ComputerVision:ApiKey.");

            if (statusCode == 404)
                return HealthCheckResult.Degraded(
                    $"ComputerVision analyze route returned HTTP 404 for endpoint '{endpoint}'. The endpoint or api-version is wrong — image analysis will fail.");

            if (statusCode == 429)
                return HealthCheckResult.Degraded(
                    "ComputerVision is rate limiting (HTTP 429). Image analysis will fall back to degraded output.");

            if (statusCode >= 500)
                return HealthCheckResult.Degraded(
                    $"ComputerVision returned a server error (HTTP {statusCode}).");

            // 200 would mean the service accepted an empty body, which it should not; report it
            // rather than silently calling an unexpected shape healthy.
            return HealthCheckResult.Degraded(
                $"ComputerVision probe returned an unexpected HTTP {statusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"ComputerVision endpoint probe failed (endpoint='{endpoint}')", ex);
        }
    }
}
