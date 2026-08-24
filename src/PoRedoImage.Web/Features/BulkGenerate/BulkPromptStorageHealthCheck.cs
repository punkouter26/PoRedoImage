using Azure.Data.Tables;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PoRedoImage.Web.Configuration;

namespace PoRedoImage.Web.Features.BulkGenerate;

/// <summary>
/// Reports whether Table Storage — which backs the User Images gallery, bulk-prompt persistence and
/// client vitals — is reachable.
/// </summary>
/// <remarks>
/// <para>
/// Storage is an OPTIONAL dependency: every feature that uses it degrades to an empty state and the
/// app is fully usable without it. So a missing or unreachable account is <see cref="HealthStatus.Degraded"/>,
/// never <see cref="HealthStatus.Unhealthy"/>. It used to be Unhealthy, which made <c>/health</c>
/// answer 503 on a working instance — a deployment hazard, because a 503 readiness probe takes an
/// otherwise-serving app out of rotation over a gallery that nobody was using yet.
/// </para>
/// <para>
/// The probe is also bounded. The default <see cref="TableServiceClient"/> retry policy is four
/// attempts with backoff, so a refused connection took ~13 seconds to report — longer than most
/// probe timeouts, which turns a degraded dependency into a timed-out health endpoint. A health
/// check asks one question ("can I reach it right now?") and a retry cannot change that answer.
/// </para>
/// <para>
/// Reads <see cref="StorageOptions"/> rather than <c>IConfiguration</c> directly. There were two
/// independent readers of <c>Storage:ConnectionString</c> and they disagreed at runtime — the
/// startup validator logged "not configured" while this check reported a live Azurite string in the
/// same process. One binding, one answer.
/// </para>
/// </remarks>
public class BulkPromptStorageHealthCheck : IHealthCheck
{
    /// <summary>How long the reachability probe may take before it is reported as degraded.</summary>
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly StorageOptions _options;

    public BulkPromptStorageHealthCheck(IOptions<StorageOptions> options)
    {
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connectionString = _options.ConnectionString;

        // Detect an UNRESOLVED Key Vault reference. App Service returns the literal
        // "@Microsoft.KeyVault(...)" string when the platform could not resolve the
        // reference (missing secret, missing role, or vault unreachable). That literal
        // is not a valid connection string, and treating it as a connection failure hides
        // the actual problem behind a parse error.
        if (!string.IsNullOrWhiteSpace(connectionString) &&
            connectionString.StartsWith("@Microsoft.KeyVault", StringComparison.Ordinal))
        {
            return HealthCheckResult.Degraded(
                "Storage:ConnectionString is an unresolved App Service Key Vault reference (Storage__ConnectionString). "
                + "Verify the secret 'PoRedoImage-StorageConnectionString' exists in 'kv-poshared' and the app's managed identity has 'Key Vault Secrets User' on the vault.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
            return HealthCheckResult.Degraded("Storage:ConnectionString is not configured; Table Storage is unavailable.");

        var prefix = connectionString[..Math.Min(20, connectionString.Length)];

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            // MaxRetries = 0: see the remarks. Retry.NetworkTimeout bounds a hung socket, the
            // linked CTS bounds everything else.
            var clientOptions = new TableClientOptions();
            clientOptions.Retry.MaxRetries = 0;
            clientOptions.Retry.NetworkTimeout = ProbeTimeout;

            var serviceClient = new TableServiceClient(connectionString, clientOptions);
            await serviceClient.GetPropertiesAsync(timeout.Token);
            return HealthCheckResult.Healthy("Azure Table Storage is reachable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded(
                $"Azure Table Storage did not answer within {ProbeTimeout.TotalSeconds:0}s (connection-string begins with '{prefix}…').");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded(
                $"Azure Table Storage is unreachable (connection-string begins with '{prefix}…'). "
                + "The gallery and prompt persistence are disabled; every other feature still works.",
                ex);
        }
    }
}
