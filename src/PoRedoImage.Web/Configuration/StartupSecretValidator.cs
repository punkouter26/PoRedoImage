using PoRedoImage.Web.Features.Diagnostics;
using Microsoft.Extensions.Options;
using PoRedoImage.Application.Configuration;
using PoRedoImage.Shared.Configuration;

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
        if (ConfigValue.Bool(_configuration, ConfigKeys.MocksUseMockAi))
        {
            _logger.LogInformation(
                "Mocks:UseMockAi=true — AI secret validation skipped. Mock services wired; no live keys required.");
            return Task.CompletedTask;
        }

        var required = new[]
        {
            ("OpenAI:Endpoint",         _configuration[ConfigKeys.OpenAiEndpoint]),
            ("OpenAI:Key",              _configuration[ConfigKeys.OpenAiKey]),
            ("OpenAI:ChatCompletionsDeployment", _configuration[ConfigKeys.OpenAiChatCompletionsDeployment]),
            ("Google:ApiKey",           _configuration[ConfigKeys.GoogleApiKey]),
        };

        var missing = required.Where(kv => string.IsNullOrWhiteSpace(kv.Item2)).ToList();

        if (missing.Count == 0)
        {
            _logger.LogInformation("All required AI secrets present. Validating AzureAd OIDC configuration…");
            ValidateOpenIdConnectConfig();
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

    /// <summary>
    /// Fails fast when the auth pipeline is going to register an OIDC scheme with a
    /// misconfigured <c>AzureAd:</c> section. A 401/500 on first click of "Sign in with Microsoft"
    /// is the most common production-deploy regression: the app boots, the AI keys check out, the
    /// first user hits the OIDC challenge, and the scheme registration 500s because ClientId is
    /// blank or CallbackPath collides with a real route. The dev-cookie-only branch (no ClientId
    /// in Development) and the FakeAuth branch are skipped — neither registers an OIDC scheme.
    ///
    /// Mirrors the AI-secret check above: fail-fast in every environment so the problem surfaces
    /// in <c>SCRIPTS/setup.ps1</c> / <c>deploy.yml</c> rather than in the browser.
    /// </summary>
    private void ValidateOpenIdConnectConfig()
    {
        // FakeAuth replaces the whole pipeline; nothing to validate.
        if (ConfigValue.Bool(_configuration, ConfigKeys.AuthEnableFakeAuth))
            return;

        var clientId = _configuration[ConfigKeys.AzureAdClientId];
        var hasOidc = !string.IsNullOrWhiteSpace(clientId);

        // Dev without a ClientId registers cookie-only auth (see AuthServiceExtensions) and is
        // explicitly supported by the project — skip the OIDC shape check.
        if (_env.IsDevelopment() && !hasOidc)
            return;

        var failures = new List<string>();

        if (!hasOidc || !Guid.TryParse(clientId, out _))
        {
            failures.Add(
                "AzureAd:ClientId is missing or not a parseable GUID. " +
                "Set it in Key Vault as PoRedoImage-AzureAd-ClientId (kv-poshared).");
        }

        var tenantId = _configuration[ConfigKeys.AzureAdTenantId];
        var isReservedTenant = tenantId is "common" or "consumers" or "organizations";
        var isGuidTenant = !string.IsNullOrWhiteSpace(tenantId) && Guid.TryParse(tenantId, out _);
        if (string.IsNullOrWhiteSpace(tenantId) || (!isReservedTenant && !isGuidTenant))
        {
            failures.Add(
                $"AzureAd:TenantId ('{tenantId ?? "<null>"}') is invalid. " +
                "Expected 'common' / 'consumers' / 'organizations' or a parseable tenant GUID. " +
                "The user spec mandates 'common' authority for all MS outlook accounts.");
        }

        var callbackPath = _configuration[ConfigKeys.AzureAdCallbackPath] ?? "/signin-oidc";
        if (callbackPath.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"AzureAd:CallbackPath ('{callbackPath}') collides with the /api/* route prefix. " +
                "An OIDC callback under /api would be intercepted by the API-aware auth redirect " +
                "and the OIDC handshake would never complete.");
        }

        if (failures.Count == 0)
        {
            _logger.LogInformation("AzureAd OIDC configuration valid (TenantId={TenantId}, CallbackPath={CallbackPath}).",
                tenantId, callbackPath);
            return;
        }

        var oidcMsg = "AzureAd OIDC configuration invalid: " + string.Join(" | ", failures);
        if (_env.IsProduction()) _logger.LogCritical(oidcMsg);
        else _logger.LogError(oidcMsg);
        throw new InvalidOperationException(oidcMsg);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
