using System.Text.RegularExpressions;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Web.Features.Shared;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Web.Features.Diagnostics;

/// <summary>
/// Minimal API endpoints for the diagnostics feature.
/// Exposes configuration values with middle characters masked for security.
/// Follows the Vertical Slice Architecture pattern — endpoint + logic co-located.
/// </summary>
public static partial class DiagnosticsEndpoints
{
    public static void MapDiagnosticsEndpoints(this WebApplication app)
    {
        // In Production this requires a Diagnostics:AdminEmails-listed identity (fail-closed); in
        // non-Production any authenticated user. Prevents arbitrary signed-in users from reading the
        // (masked) infra topology in prod. Policy behaviour lives in AuthServiceExtensions.
        var group = app.MapGroup("/api/diag")
            .WithTags("Diagnostics")
            .RequireAuthorization(AuthorizationPolicies.Diagnostics);

        group.MapGet("/", GetDiagnostics)
            .WithName("GetDiagnostics")
            .WithSummary("Get masked configuration values for diagnostics");

        // Anonymous, unauthenticated mock-status probe. The WASM client calls this at startup
        // (before any login) to learn whether the server is running mocked AI services, then
        // renders the "USING MOCK DATA" banner. Returns the IMockable reasons; empty in production.
        app.MapGet("/api/diag/mock-status", (IEnumerable<IMockable> mocks) =>
                Results.Ok(mocks.Select(m => m.MockReason)
                                .Where(r => !string.IsNullOrWhiteSpace(r))
                                .ToArray()))
            .WithName("GetMockStatus")
            .WithSummary("List active mock-service reasons (drives the client mock banner)")
            .AllowAnonymous();
    }

    private static async Task<IResult> GetDiagnostics(
        IConfiguration configuration,
        IWebHostEnvironment env,
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService healthCheckService)
    {
        var healthReport = await healthCheckService.CheckHealthAsync();

        var healthData = healthReport.Entries.ToDictionary(
            kvp => kvp.Key,
            kvp => new
            {
                Status = kvp.Value.Status.ToString(),
                Description = kvp.Value.Description,
                DurationMs = kvp.Value.Duration.TotalMilliseconds
            });

        var diagnostics = new Dictionary<string, object>
        {
            ["Environment"] = env.EnvironmentName,
            ["MachineName"] = Environment.MachineName,
            ["OSVersion"] = Environment.OSVersion.ToString(),
            ["DotNetVersion"] = Environment.Version.ToString(),
            ["ProcessId"] = Environment.ProcessId,
            ["Timestamp"] = DateTime.UtcNow.ToString("O"),
            ["Health"] = new
            {
                Status = healthReport.Status.ToString(),
                TotalDurationMs = healthReport.TotalDuration.TotalMilliseconds,
                Entries = healthData
            },
            ["Configuration"] = new Dictionary<string, string?>
            {
                [ConfigKeys.KeyVaultUri] = MaskValue(configuration[ConfigKeys.KeyVaultUri]),
                [ConfigKeys.AzureKeyVaultEndpoint] = MaskValue(configuration[ConfigKeys.AzureKeyVaultEndpoint]),
                [ConfigKeys.AzureAdTenantId] = MaskValue(configuration[ConfigKeys.AzureAdTenantId]),
                [ConfigKeys.AzureAdClientId] = MaskValue(configuration[ConfigKeys.AzureAdClientId]),
                [ConfigKeys.ComputerVisionEndpoint] = MaskValue(configuration[ConfigKeys.ComputerVisionEndpoint]),
                [ConfigKeys.ComputerVisionApiKey] = MaskValue(configuration[ConfigKeys.ComputerVisionApiKey]),
                [ConfigKeys.ComputerVisionMinTagConfidence] = configuration[ConfigKeys.ComputerVisionMinTagConfidence],
                [ConfigKeys.OpenAiEndpoint] = MaskValue(configuration[ConfigKeys.OpenAiEndpoint]),
                [ConfigKeys.OpenAiKey] = MaskValue(configuration[ConfigKeys.OpenAiKey]),
                [ConfigKeys.OpenAiChatCompletionsDeployment] = configuration[ConfigKeys.OpenAiChatCompletionsDeployment],
                [ConfigKeys.ApplicationInsightsConnectionString] = MaskValue(configuration[ConfigKeys.ApplicationInsightsConnectionString]),
                [ConfigKeys.StorageConnectionString] = MaskValue(configuration[ConfigKeys.StorageConnectionString])
            }
        };

        return Results.Ok(diagnostics);
    }

    /// <summary>
    /// Masks the middle portion of a value for security.
    /// Example: "sk-abcdef123456" → "sk-a*********3456"
    /// </summary>
    internal static string? MaskValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "(not set)";

        if (value.Length <= 8)
            return new string('*', value.Length);

        var visibleStart = Math.Min(4, value.Length / 4);
        var visibleEnd = Math.Min(4, value.Length / 4);
        var maskedLength = value.Length - visibleStart - visibleEnd;

        return string.Concat(
            value.AsSpan(0, visibleStart),
            new string('*', maskedLength),
            value.AsSpan(value.Length - visibleEnd));
    }
}
