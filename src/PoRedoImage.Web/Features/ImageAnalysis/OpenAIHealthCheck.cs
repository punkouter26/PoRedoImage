using Microsoft.Extensions.Diagnostics.HealthChecks;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Web.Features.ImageAnalysis;

/// <summary>
/// Health check that verifies connectivity, routing AND authentication for Azure OpenAI.
/// <remarks>
/// Only HTTP 200 from the models list counts as Healthy. Everything else is classified explicitly.
/// The previous version treated every status except 401/403 as Healthy while probing a path this
/// resource answers with 404 — so it reported "Healthy (HTTP 404)" on every call and the
/// post-deploy smoke test that asserts on it could never have caught a broken deploy.
/// </remarks>
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
        var endpoint = _configuration[ConfigKeys.OpenAiEndpoint];
        var apiKey = _configuration[ConfigKeys.OpenAiKey];

        // Distinguish "secret not configured" / "App Service KV reference not yet resolved" /
        // "endpoint broken". In Production, an unresolved @Microsoft.KeyVault(...) reference
        // is the cold-start race where the app process started before the platform populated
        // the env var with the secret value. Surface as Degraded, NOT Unhealthy, so the
        // post-deploy smoke test can tell a deploy failure (Unhealthy = probe threw) from
        // a platform propagation delay (Degraded = config pending). See ADR-017 + ADR-026.
        if (string.IsNullOrWhiteSpace(endpoint) || IsUnresolvedKeyVaultReference(endpoint))
            return HealthCheckResult.Degraded("OpenAI:Endpoint is not configured. In Production this usually means the Key Vault reference (OpenAI__Endpoint) did not resolve — verify the secret 'PoRedoImage-OpenAI-Endpoint' exists in 'kv-poshared' and that the app's managed identity has 'Key Vault Secrets User' on the vault.");
        if (string.IsNullOrWhiteSpace(apiKey) || IsUnresolvedKeyVaultReference(apiKey))
            return HealthCheckResult.Degraded("OpenAI:Key is not configured. In Production this usually means the Key Vault reference (OpenAI__Key) did not resolve — same KV/MI checks as OpenAI:Endpoint.");

        // App Service Key Vault reference sentinel value — the platform returns the literal
        // reference string until the managed identity resolves it. Detecting this explicitly
        // is more robust than a string.IsNullOrWhiteSpace check, which passes for the
        // non-blank reference string and then lets the URL probe throw as Unhealthy.
        static bool IsUnresolvedKeyVaultReference(string s) =>
            s.StartsWith("@Microsoft.KeyVault(", StringComparison.OrdinalIgnoreCase);

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            // Probe the models list. The previous probe used /openai/deployments, which is a
            // control-plane path this Cognitive Services multi-service account does not serve: it
            // returns 404 no matter how healthy the resource is. Because the old classifier called
            // every non-401/403 status Healthy, the check reported "Healthy (HTTP 404)" forever and
            // could not fail. /openai/models is a data-plane path served by the same resource with
            // the same api-key header, and returns 200 when the key is valid.
            var probe = endpoint.TrimEnd('/') + "/openai/models?api-version=2024-02-01";
            var request = new HttpRequestMessage(HttpMethod.Get, probe);
            request.Headers.TryAddWithoutValidation("api-key", apiKey);
            var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            var statusCode = (int)response.StatusCode;

            if (statusCode == 200)
                return HealthCheckResult.Healthy("OpenAI endpoint reachable and key accepted.");

            if (statusCode is 401 or 403)
                return HealthCheckResult.Degraded(
                    $"OpenAI endpoint reachable but authentication failed (HTTP {statusCode}). Check OpenAI:Key.");

            if (statusCode == 404)
                return HealthCheckResult.Degraded(
                    $"OpenAI models route returned HTTP 404 for endpoint '{endpoint}'. The endpoint or api-version is wrong — description generation and Style Director reasoning will fall back to heuristics.");

            if (statusCode == 429)
                return HealthCheckResult.Degraded(
                    "OpenAI is rate limiting (HTTP 429). Description generation will fall back to tag-derived text.");

            if (statusCode >= 500)
                return HealthCheckResult.Degraded(
                    $"OpenAI returned a server error (HTTP {statusCode}).");

            return HealthCheckResult.Degraded(
                $"OpenAI probe returned an unexpected HTTP {statusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"OpenAI endpoint probe failed (endpoint='{endpoint}')", ex);
        }
    }
}
