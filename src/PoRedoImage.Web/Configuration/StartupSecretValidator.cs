using PoRedoImage.Web.Features.Diagnostics;
using Microsoft.Extensions.Options;

namespace PoRedoImage.Web.Configuration;

/// <summary>
/// Runs once at host startup to fail-fast (Production) or warn loudly (Development)
/// when the minimum set of required secrets is missing. Replaces the silent
/// <c>catch (Exception ex) { Log.Warning(...); return; }</c> in
/// Program.cs that allowed empty OpenAI keys to reach the first request
/// (Po2Logic F7 mitigation).
/// </summary>
public sealed class StartupSecretValidator : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<StartupSecretValidator> _logger;

    public StartupSecretValidator(
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<StartupSecretValidator> logger)
    {
        _configuration = configuration;
        _env = env;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Mock-mode opt-out: real services aren't wired, so AI key validation is unnecessary.
        // Keeps the offline / CI path bootable while still producing loud, actionable failures
        // when someone WANTS real AI but forgot the keys.
        if (_configuration.GetValue<bool>("Mocks:UseMockAi"))
        {
            _logger.LogInformation(
                "Mocks:UseMockAi=true — AI secret validation skipped. Mock services wired; no live keys required.");
            return Task.CompletedTask;
        }

        var required = new[]
        {
            ("OpenAI:Endpoint",         _configuration["OpenAI:Endpoint"]),
            ("OpenAI:Key",              _configuration["OpenAI:Key"]),
            ("OpenAI:ChatCompletionsDeployment", _configuration["OpenAI:ChatCompletionsDeployment"]),
            ("Google:ApiKey",           _configuration["Google:ApiKey"]),
        };

        var missing = required.Where(kv => string.IsNullOrWhiteSpace(kv.Item2)).ToList();

        if (missing.Count == 0)
        {
            _logger.LogInformation("All required AI secrets present. Startup secret validation passed.");
            return Task.CompletedTask;
        }

        // Dev policy: real services were selected, so missing keys must hard-fail in EVERY
        // environment. The previous warn-and-continue in Development masked missing-key setups
        // until the first AI call returned 401. To run fully offline, set Mocks:UseMockAi=true.
        //
        // Sources of truth (see infra/main.bicep):
        //   - Production: App Settings referencing @Microsoft.KeyVault(...) — kv-poshared.
        //   - Development: Key Vault kv-poshared via DefaultAzureCredential (requires
        //     'az login' + 'Key Vault Secrets User' RBAC; setup.ps1 verifies both).
        // Only Mocks:UseMockAi=true permits booting without these.
        var offlineHint = _env.IsDevelopment()
            ? " To run fully offline with mock AI, set \"Mocks:UseMockAi\": true in appsettings.Development.json or via env var Mocks__UseMockAi=true."
            : string.Empty;

        var msg = $"Missing required AI configuration: {string.Join(", ", missing.Select(m => m.Item1))}. " +
                  "Dev, test, and prod all source keys from Azure Key Vault kv-poshared " +
                  "(po-aiservices-shared RG) or, in App Service, from the @Microsoft.KeyVault(...) " +
                  "Application Settings bound in infra/main.bicep. " +
                  "Key Vault secret names expected: PoRedoImage-OpenAI-Endpoint, PoRedoImage-OpenAI-ApiKey, " +
                  "PoRedoImage-Google-ApiKey, plus OpenAI:ChatCompletionsDeployment (literal app setting). " +
                  "Verify 'az login' succeeded and that your account has 'Key Vault Secrets User' on kv-poshared " +
                  "(SCRIPTS/setup.ps1 prints the exact self-heal command)." +
                  offlineHint;

        if (_env.IsProduction())
            _logger.LogCritical(msg);
        else
            _logger.LogError(msg);

        throw new InvalidOperationException(msg);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
