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
            return HealthCheckResult.Unhealthy("Azure Table Storage is unreachable.", ex);
        }
    }
}
