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
/// Bind OpenAI options + fail-fast when real services are wired but the required keys are missing.
/// Dev policy: <c>Mocks:UseMockAi=true</c> skips validation entirely (real services aren't registered);
/// otherwise we require real keys in every environment — Dev included — so the app never silently
/// degrades to a half-broken state (calling real Azure OpenAI and 401'ing on first request).
/// </summary>
public sealed class OpenAiOptionsValidator : IValidateOptions<OpenAiOptions>
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAiOptionsValidator> _logger;

    public OpenAiOptionsValidator(
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger<OpenAiOptionsValidator> logger)
    {
        _env = env;
        _configuration = configuration;
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, OpenAiOptions options)
    {
        // If real services aren't wired (mock mode forced), the bound options are never consumed.
        // Skipping the check here keeps the offline / CI path bootable while still producing loud,
        // actionable failures when someone WANTS real AI but forgot the keys.
        if (_configuration.GetValue<bool>("Mocks:UseMockAi"))
        {
            _logger.LogInformation(
                "Mocks:UseMockAi=true — OpenAI key validation skipped. Real Azure OpenAI is not wired.");
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Endpoint))
            failures.Add("OpenAI:Endpoint is not configured. Set it via Key Vault (PoRedoImage-OpenAI-Endpoint) or `dotnet user-secrets set \"OpenAI:Endpoint\" --project src/PoRedoImage.Web`.");
        if (string.IsNullOrWhiteSpace(options.Key))
            failures.Add("OpenAI:Key is not configured. Set it via Key Vault (PoRedoImage-OpenAI-ApiKey) or `dotnet user-secrets set \"OpenAI:Key\" --project src/PoRedoImage.Web`.");
        if (string.IsNullOrWhiteSpace(options.ChatCompletionsDeployment))
            failures.Add("OpenAI:ChatCompletionsDeployment is not configured (defaulted to gpt-5.4-nano in appsettings.json — verify it matches your Azure OpenAI deployment).");

        if (failures.Count == 0) return ValidateOptionsResult.Success;

        // Real services are wired but keys are missing — fail-fast in EVERY environment, including
        // Development. Silent warn-and-continue in Dev previously masked missing-key setups and only
        // surfaced as a 401 on the first AI call. To run fully offline, set Mocks:UseMockAi=true.
        var envLabel = _env.IsDevelopment() ? "Development" : _env.EnvironmentName;
        var offlineHint = _env.IsDevelopment()
            ? " To run fully offline with mock AI, set \"Mocks:UseMockAi\": true in appsettings.Development.json."
            : string.Empty;
        var combined = $"[{envLabel}] OpenAI configuration invalid: {string.Join(" | ", failures)}{offlineHint}";

        if (_env.IsDevelopment())
            _logger.LogError(combined);
        else
            _logger.LogCritical(combined);

        return ValidateOptionsResult.Fail(failures);
    }
}
