using System.Text.RegularExpressions;
using PoRedoImage.Domain.Interfaces;

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
        var group = app.MapGroup("/api/diag")
            .WithTags("Diagnostics")
            .RequireAuthorization();

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
                ["KeyVault:Uri"] = configuration["KeyVault:Uri"],
                ["AZURE_KEY_VAULT_ENDPOINT"] = MaskValue(configuration["AZURE_KEY_VAULT_ENDPOINT"]),
                ["AzureAd:TenantId"] = configuration["AzureAd:TenantId"],
                ["AzureAd:ClientId"] = MaskValue(configuration["AzureAd:ClientId"]),
                ["ComputerVision:Endpoint"] = MaskValue(configuration["ComputerVision:Endpoint"]),
                ["ComputerVision:ApiKey"] = MaskValue(configuration["ComputerVision:ApiKey"]),
                ["ComputerVision:MinTagConfidence"] = configuration["ComputerVision:MinTagConfidence"],
                ["OpenAI:Endpoint"] = MaskValue(configuration["OpenAI:Endpoint"]),
                ["OpenAI:Key"] = MaskValue(configuration["OpenAI:Key"]),
                ["OpenAI:ChatCompletionsDeployment"] = configuration["OpenAI:ChatCompletionsDeployment"],
                ["ApplicationInsights:ConnectionString"] = MaskValue(configuration["ApplicationInsights:ConnectionString"]),
                ["Storage:ConnectionString"] = MaskValue(configuration["Storage:ConnectionString"])
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
