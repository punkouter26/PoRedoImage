using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using PoRedoImage.Application.Configuration;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Web.Configuration;

/// <summary>
/// Composable host-bootstrap steps extracted from Program.cs so the entry point reads as a
/// short, ordered recipe. Call order matters: Key Vault must load before Serilog/OpenTelemetry
/// so <c>ApplicationInsights:ConnectionString</c> is available when telemetry is configured.
/// </summary>
public static class HostBootstrapExtensions
{
    /// <summary>
    /// Loads Azure Key Vault into configuration so secrets are present before Serilog and the
    /// option-binding validators run. Key Vault is the SINGLE source of truth for dev, test, and
    /// prod — see ADR-021 policy "real keys only, sourced from kv-poshared or App Settings"
    /// (the App Service <c>@Microsoft.KeyVault(...)</c> references also resolve from the same vault).
    ///
    /// Behaviour by configuration state:
    ///   - <c>KeyVault:Uri</c> empty AND <c>AZURE_KEY_VAULT_ENDPOINT</c> empty: KV is explicitly
    ///     disabled (e.g. integration tests, Mocks:UseMockAi flow). Proceed without it.
    ///   - <c>Mocks:UseMockAi=true</c>: KV is irrelevant; proceed without it (skip cleanly).
    ///   - KV endpoint set but load fails (auth/RBAC/timeout): fail-fast in EVERY environment —
    ///     not just Production. Dev previously warned-and-continued, which masked missing
    ///     <c>az login</c> / RBAC and surfaced as 401s at first AI call.
    /// </summary>
    public static WebApplicationBuilder AddPoRedoImageKeyVault(this WebApplicationBuilder builder)
    {
        var keyVaultEndpoint = builder.Configuration[ConfigKeys.KeyVaultUri]
            ?? builder.Configuration[ConfigKeys.AzureKeyVaultEndpoint];

        // Explicit opt-outs: never throw on missing endpoint when dev "really" wants offline,
        // either by configuring the mock mode or by leaving the endpoint empty (test fixtures).
        if (string.IsNullOrEmpty(keyVaultEndpoint))
        {
            Log.Information(
                "Key Vault endpoint not configured (KeyVault:Uri / AZURE_KEY_VAULT_ENDPOINT empty). " +
                "Skipping KV load; secrets must come from another provider or Mocks:UseMockAi must be true.");
            return builder;
        }
        if (ConfigValue.Bool(builder.Configuration, ConfigKeys.MocksUseMockAi))
        {
            Log.Information(
                "Mocks:UseMockAi=true — skipping Key Vault load (real AI services are not wired).");
            return builder;
        }

        try
        {
            // In App Service only the managed identity matters. Excluding the dev/interactive probes
            // (which can each add seconds while failing) keeps the cold-start token fast — a slow first
            // token previously yielded an empty Key Vault load and crash-looped the app. Secrets are
            // also bound as App Service Key Vault references (see infra/main.bicep) so config is
            // populated even if this provider is slow; this is defence in depth.
            var credential = builder.Environment.IsDevelopment()
                ? new DefaultAzureCredential()
                : new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ExcludeInteractiveBrowserCredential = true,
                    ExcludeVisualStudioCredential = true,
                    ExcludeAzureCliCredential = true,
                    ExcludeAzurePowerShellCredential = true,
                    ExcludeAzureDeveloperCliCredential = true,
                });

            // ReloadInterval: re-fetch secrets every 30 minutes for secret rotation without restart.
            builder.Configuration.AddAzureKeyVault(
                new Uri(keyVaultEndpoint),
                credential,
                new AzureKeyVaultConfigurationOptions
                {
                    Manager = new KeyVaultSecretNameMapping(),
                    ReloadInterval = TimeSpan.FromMinutes(30)
                });

            Log.Information("Key Vault configuration loaded from {Endpoint}", keyVaultEndpoint);

            // In Development, Key Vault would override appsettings.Development.json values (KV is the
            // last provider added). Pin Storage back to Azurite (Docker) regardless of what KV provides.
            if (builder.Environment.IsDevelopment())
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [ConfigKeys.StorageConnectionString] = "UseDevelopmentStorage=true"
                });
            }
        }
        catch (Exception ex)
        {
            // KV is configured but unreachable — fail-fast in EVERY environment. Soft-warn-and-continue
            // in Dev previously masked the most common local-setup failure modes: missing `az login`
            // and missing 'Key Vault Secrets User' RBAC. Both produce an empty config that surfaces
            // as 401 on first AI call.
            var envLabel = builder.Environment.IsDevelopment() ? "Development" : builder.Environment.EnvironmentName;
            var msg =
                $"[{envLabel}] Key Vault at {keyVaultEndpoint} is unreachable or your account " +
                "lacks access. Real services are wired but no secrets loaded; aborting startup " +
                "(keys must come from KV or App Settings — see infra/main.bicep). " +
                "Self-heal: 1) run 'az login', 2) ensure your account has 'Key Vault Secrets User' " +
                "on kv-poshared (SCRIPTS/setup.ps1 prints the exact 'az role assignment create' " +
                "command when RBAC is missing). " +
                "Run fully offline with Mocks:UseMockAi=true instead.";

            Log.Error(ex, msg);
            throw new InvalidOperationException(msg, ex);
        }

        return builder;
    }

    /// <summary>
    /// Configures the production Serilog pipeline (Console + rolling file + Application Insights) and
    /// wires it into the host. Returns the resolved Application Insights connection string so the
    /// caller can pass the same value to <see cref="AddPoRedoImageTelemetry"/> (resolved once).
    /// </summary>
    public static string? ConfigurePoRedoImageSerilog(this WebApplicationBuilder builder)
    {
        var appInsightsConnectionString = ResolveAppInsightsConnectionString(builder.Configuration);

        // In Azure App Service with Run-From-Package (OneDeploy), /home/site/wwwroot/ is read-only.
        // Use /home/LogFiles/Application/ (writable) in production; a relative path in development.
        var logFilePath = builder.Environment.IsDevelopment()
            ? "logs/poredoimage-.log"
            : "/home/LogFiles/Application/poredoimage-.log";

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "PoRedoImage")
            .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
            .Enrich.WithMachineName()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                // A daily roll alone bounds nothing: one day of traffic produced a 12.5 MB file
                // locally before static-asset requests were dropped to Verbose. Cap each file and
                // let it roll within the day so a burst cannot fill the App Service LogFiles share.
                fileSizeLimitBytes: 32L * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate:
                    "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.Conditional(_ => !string.IsNullOrEmpty(appInsightsConnectionString),
                sink => sink.ApplicationInsights(appInsightsConnectionString!, TelemetryConverter.Traces))
            .CreateLogger();

        builder.Host.UseSerilog();

        return appInsightsConnectionString;
    }

    /// <summary>
    /// Wires OpenTelemetry tracing/metrics and, when an Application Insights connection string is
    /// present, the Azure Monitor exporter with an error-preserving sampler. When the connection
    /// string is absent (local dev/test) instrumentation still runs but nothing is exported.
    /// </summary>
    public static WebApplicationBuilder AddPoRedoImageTelemetry(
        this WebApplicationBuilder builder, string? appInsightsConnectionString)
    {
        // cloud_RoleName resolved from the real entry assembly so the App Insights "Cloud role name"
        // is the actual app name and never the unknown_service:dotnet default.
        var cloudRoleName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "PoRedoImage";
        var assemblyVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";

        var otelBuilder = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(cloudRoleName, serviceVersion: assemblyVersion))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        if (string.IsNullOrWhiteSpace(appInsightsConnectionString))
        {
            return builder;
        }

        // Sampling: full fidelity (100%) in Dev/Test; a configurable ceiling (default 10%) in
        // Production, read from ApplicationInsights:SamplingRatio so it can be tuned without redeploy.
        // The production value is also set EXPLICITLY in infra/main.bicep (ApplicationInsights__SamplingRatio)
        // so the budget is auditable in the portal instead of an invisible code default.
        //
        // Telemetry budget intent (heartbeat + exceptions retained, noise dropped):
        //   • Metrics (incl. the SDK heartbeat / availability signal) flow through the metrics pipeline
        //     above and are NOT subject to this trace sampler — heartbeat is never thinned out.
        //   • Exceptions: Serilog exports logged exceptions at 100% (separate from traces), and the
        //     ErrorPreservingSampler below never drops error-bearing spans (status >= 500 / error=true).
        //   • No keep-warm/availability web-test pinger is added: it would wake the F1 app and burn the
        //     60-min/day CPU budget (see ADR-015). Up/down visibility comes from the heartbeat metric.
        var samplingRatio = builder.Environment.IsDevelopment()
            ? 1.0
            : ConfigValue.Double(builder.Configuration, ConfigKeys.ApplicationInsightsSamplingRatio) ?? 0.1;

        otelBuilder.UseAzureMonitor(options =>
        {
            options.ConnectionString = appInsightsConnectionString;
            options.SamplingRatio = (float)samplingRatio;
        });

        // Override the distro's blanket fixed-ratio sampler with one that drops routine noise at the
        // same ratio but NEVER drops error-bearing spans (status ≥ 500 / error=true). Registered
        // after UseAzureMonitor so this SetSampler wins.
        builder.Services.ConfigureOpenTelemetryTracerProvider((_, tracerProvider) =>
            tracerProvider.SetSampler(new ErrorPreservingSampler(samplingRatio)));

        return builder;
    }

    /// <summary>
    /// Resolution order: APPLICATIONINSIGHTS_CONNECTION_STRING → APPINSIGHTS_INSTRUMENTATIONKEY →
    /// staging fallback. A present-but-malformed string (no InstrumentationKey) is treated as "no
    /// telemetry" rather than crashing the host — a malformed value previously fail-fasted Production
    /// into a cold-start crash loop.
    /// </summary>
    private static string? ResolveAppInsightsConnectionString(IConfiguration config)
    {
        var resolved = ResolveRaw(config);

        if (!string.IsNullOrWhiteSpace(resolved) &&
            !resolved.Contains("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("Application Insights connection string is present but malformed "
                + "(no InstrumentationKey). Telemetry export is disabled; the app will start normally. "
                + "Fix the ApplicationInsights:ConnectionString value to restore telemetry.");
            return null;
        }

        return resolved;

        static string? ResolveRaw(IConfiguration config)
        {
            var connectionString = config[ConfigKeys.AppInsightsConnectionStringEnv]
                ?? config[ConfigKeys.ApplicationInsightsConnectionString];
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return connectionString;
            }

            var instrumentationKey = config[ConfigKeys.AppInsightsInstrumentationKeyEnv];
            if (!string.IsNullOrWhiteSpace(instrumentationKey))
            {
                return $"InstrumentationKey={instrumentationKey}";
            }

            return config[ConfigKeys.ApplicationInsightsStagingConnectionString];
        }
    }
}
