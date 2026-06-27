using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PoRedoImage.Web.Features.Diagnostics;

/// <summary>
/// Health check that verifies the Key Vault (PoShared) is reachable and readable.
/// Attempts a lightweight GetProperties call on a known secret without fetching the value.
/// </summary>
public sealed class KeyVaultHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public KeyVaultHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Resolve the endpoint the same way Program.cs does (KeyVault:Uri is set in Development,
        // AZURE_KEY_VAULT_ENDPOINT in Production), so dev correctly reports Key Vault as active and
        // confirms it is serving real secrets via DefaultAzureCredential (az login / VS).
        var endpoint = _configuration["KeyVault:Uri"] ?? _configuration["AZURE_KEY_VAULT_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(endpoint))
            return HealthCheckResult.Degraded("No Key Vault endpoint configured (KeyVault:Uri / AZURE_KEY_VAULT_ENDPOINT); Key Vault is skipped.");

        try
        {
            var credential = new Azure.Identity.DefaultAzureCredential();
            var client = new SecretClient(new Uri(endpoint), credential);

            // List up to 1 secret property — no value fetched, cheapest possible probe.
            await foreach (var _ in client.GetPropertiesOfSecretsAsync(cancellationToken).WithCancellation(cancellationToken))
            {
                break;
            }

            return HealthCheckResult.Healthy($"Key Vault reachable at {endpoint}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Key Vault unreachable at {endpoint}", ex);
        }
    }
}
