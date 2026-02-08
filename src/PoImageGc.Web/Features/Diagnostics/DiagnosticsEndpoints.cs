using System.Text.RegularExpressions;

namespace PoImageGc.Web.Features.Diagnostics;

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
            .WithTags("Diagnostics");

        group.MapGet("/", GetDiagnostics)
            .WithName("GetDiagnostics")
            .WithSummary("Get masked configuration values for diagnostics");
    }

    private static IResult GetDiagnostics(IConfiguration configuration, IWebHostEnvironment env)
    {
        var diagnostics = new Dictionary<string, object>
        {
            ["Environment"] = env.EnvironmentName,
            ["MachineName"] = Environment.MachineName,
            ["OSVersion"] = Environment.OSVersion.ToString(),
            ["DotNetVersion"] = Environment.Version.ToString(),
            ["ProcessId"] = Environment.ProcessId,
            ["Timestamp"] = DateTime.UtcNow.ToString("O"),
            ["Configuration"] = new Dictionary<string, string?>
            {
                ["AZURE_KEY_VAULT_ENDPOINT"] = MaskValue(configuration["AZURE_KEY_VAULT_ENDPOINT"]),
                ["ComputerVision:Endpoint"] = MaskValue(configuration["ComputerVision:Endpoint"]),
                ["ComputerVision:ApiKey"] = MaskValue(configuration["ComputerVision:ApiKey"]),
                ["ComputerVision:MinTagConfidence"] = configuration["ComputerVision:MinTagConfidence"],
                ["OpenAI:Endpoint"] = MaskValue(configuration["OpenAI:Endpoint"]),
                ["OpenAI:Key"] = MaskValue(configuration["OpenAI:Key"]),
                ["OpenAI:ChatCompletionsDeployment"] = configuration["OpenAI:ChatCompletionsDeployment"],
                ["OpenAI:ImageGenerationDeployment"] = configuration["OpenAI:ImageGenerationDeployment"],
                ["OpenAI:ImageEndpoint"] = MaskValue(configuration["OpenAI:ImageEndpoint"]),
                ["OpenAI:ImageKey"] = MaskValue(configuration["OpenAI:ImageKey"]),
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
