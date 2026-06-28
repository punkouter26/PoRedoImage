using Microsoft.Extensions.Diagnostics.HealthChecks;
using Azure.Data.Tables;

namespace PoRedoImage.Web.Features.BulkGenerate;

public class BulkPromptStorageHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public BulkPromptStorageHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration["Storage:ConnectionString"];

        // Detect an UNRESOLVED Key Vault reference. App Service returns the literal
        // "@Microsoft.KeyVault(...)" string when the platform could not resolve the
        // reference (missing secret, missing role, or vault unreachable). That literal
        // is not a valid connection string, and treating it as Unhealthy hides the
        // actual problem behind a parse error.
        if (!string.IsNullOrWhiteSpace(connectionString) &&
            connectionString.StartsWith("@Microsoft.KeyVault", StringComparison.Ordinal))
        {
            return HealthCheckResult.Degraded(
                "Storage:ConnectionString is an unresolved App Service Key Vault reference (Storage__ConnectionString). "
                + "Verify the secret 'PoRedoImage-StorageConnectionString' exists in 'kv-poshared' and the app's managed identity has 'Key Vault Secrets User' on the vault.");
        }
        if (string.IsNullOrWhiteSpace(connectionString))
            return HealthCheckResult.Degraded("Storage:ConnectionString is not configured; Table Storage is unavailable.");

        try
        {
            var serviceClient = new TableServiceClient(connectionString);
            await serviceClient.GetPropertiesAsync(cancellationToken);
            return HealthCheckResult.Healthy("Azure Table Storage is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Azure Table Storage is unreachable (connection-string begins with '{connectionString[..Math.Min(20, connectionString.Length)]}…').", ex);
        }
    }
}
