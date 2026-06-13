using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace PoRedoImage.Web.Configuration;

/// <summary>
/// Strongly-typed binding for the <c>OpenAI:</c> configuration section. Replaces the
/// 6× duplicated <c>IConfiguration["OpenAI:Key"]</c> magic strings across
/// <see cref="Infrastructure.Services.AzureOpenAiService"/>, health checks, and
/// diagnostics (R3 in Po2Logic refactor queue).
/// <para>
/// C# 14 primary constructor + <c>ValidateOnStart</c> via .NET 10's
/// <see cref="Microsoft.Extensions.Hosting.HostApplicationBuilder"/> gives us
/// fail-fast configuration validation at boot — no more 401 surprises at first call.
/// </para>
/// </summary>
public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    [Required, Url]
    public string Endpoint { get; init; } = string.Empty;

    [Required]
    public string Key { get; init; } = string.Empty;

    [Required]
    public string ChatCompletionsDeployment { get; init; } = "gpt-4o";
}

/// <summary>
/// Bind OpenAI options + ensure dev/test environments can boot with empty values
/// (Production validates the [Required] fields; Development does not).
/// </summary>
public sealed class OpenAiOptionsValidator : IValidateOptions<OpenAiOptions>
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<OpenAiOptionsValidator> _logger;

    public OpenAiOptionsValidator(IWebHostEnvironment env, ILogger<OpenAiOptionsValidator> logger)
    {
        _env = env;
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, OpenAiOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Endpoint))
            failures.Add("OpenAI:Endpoint is not configured. Set it via Key Vault (PoRedoImage-OpenAI-Endpoint) or appsettings.Development.json user-secrets.");
        if (string.IsNullOrWhiteSpace(options.Key))
            failures.Add("OpenAI:Key is not configured. Set it via Key Vault (PoRedoImage-OpenAI-ApiKey) or `dotnet user-secrets set \"OpenAI:Key\"`.");
        if (string.IsNullOrWhiteSpace(options.ChatCompletionsDeployment))
            failures.Add("OpenAI:ChatCompletionsDeployment is not configured.");

        if (failures.Count == 0) return ValidateOptionsResult.Success;

        // In Development we warn rather than fail — the app should still boot so the
        // user can use the GUEST mode UI and see actionable error messages per-endpoint.
        if (_env.IsDevelopment())
        {
            foreach (var f in failures)
                _logger.LogWarning("OpenAI configuration: {Failure}", f);
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(failures);
    }
}
