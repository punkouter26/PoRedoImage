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

        var msg = $"Missing required AI configuration: {string.Join(", ", missing.Select(m => m.Item1))}. " +
                  "Set them via user-secrets, Key Vault, or appsettings.Development.json. " +
                  "In production this would block startup; in development the app continues with reduced features.";

        if (_env.IsProduction())
        {
            _logger.LogCritical(msg);
            throw new InvalidOperationException(msg);
        }
        else
        {
            _logger.LogWarning(msg);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
