using Microsoft.Extensions.Diagnostics.HealthChecks;
using Azure.Data.Tables;

namespace Server.Services.HealthChecks;

/// <summary>
/// Health check for Azure Table Storage connectivity.
/// Uses Custom IHealthCheck pattern to verify storage is accessible.
/// </summary>
public class AzureTableStorageHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AzureTableStorageHealthCheck> _logger;

    public AzureTableStorageHealthCheck(
        IConfiguration configuration,
        ILogger<AzureTableStorageHealthCheck> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionString = _configuration.GetConnectionString("AzureTableStorage");
            
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                _logger.LogWarning("Azure Table Storage connection string is not configured");
                return HealthCheckResult.Degraded("Connection string not configured");
            }

            var tableServiceClient = new TableServiceClient(connectionString);
            
            // Try to get account info to verify connectivity
            await tableServiceClient.GetPropertiesAsync(cancellationToken);
            
            _logger.LogInformation("Azure Table Storage health check succeeded");
            return HealthCheckResult.Healthy("Azure Table Storage is accessible");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Table Storage health check failed");
            return HealthCheckResult.Unhealthy(
                "Unable to connect to Azure Table Storage",
                ex);
        }
    }
}
