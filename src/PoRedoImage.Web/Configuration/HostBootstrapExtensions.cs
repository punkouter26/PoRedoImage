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

namespace PoRedoImage.Web.Configuration;

/// <summary>
/// Composable host-bootstrap steps extracted from Program.cs so the entry point reads as a
/// short, ordered recipe. Call order matters: Key Vault must load before Serilog/OpenTelemetry
/// so <c>ApplicationInsights:ConnectionString</c> is available when telemetry is configured.
/// </summary>
public static class HostBootstrapExtensions
{
    /// <summary>
    /// Loads Azure Key Vault into configuration via Managed Identity (App Service) so secrets are
    /// present before Serilog/OpenTelemetry read them. Secret names are mapped by
    /// <see cref="KeyVaultSecretNameMapping"/>. Fails fast outside Development if the vault is
    /// unreachable; in Development it logs and continues, then pins Storage back to Azurite.
    /// </summary>
    public static WebApplicationBuilder AddPoRedoImageKeyVault(this WebApplicationBuilder builder)
    {
        var keyVaultEndpoint = builder.Configuration["KeyVault:Uri"]
            ?? builder.Configuration["AZURE_KEY_VAULT_ENDPOINT"];
        if (string.IsNullOrEmpty(keyVaultEndpoint))
        {
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
                    ["Storage:ConnectionString"] = "UseDevelopmentStorage=true"
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "Key Vault at {Endpoint} is unreachable or missing required secrets; secrets may not be loaded. "
                + "Application Insights and other Key Vault-dependent features may be unavailable.",
                keyVaultEndpoint);

            // In production fail-fast if Key Vault cannot be read, otherwise continue in Dev.
            if (!builder.Environment.IsDevelopment())
            {
                Log.Fatal(ex, "Key Vault startup validation failed, terminating app startup.");
                throw;
            }
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
        var samplingRatio = builder.Environment.IsDevelopment()
            ? 1.0
            : builder.Configuration.GetValue<double?>("ApplicationInsights:SamplingRatio") ?? 0.1;

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
            var connectionString = config["APPLICATIONINSIGHTS_CONNECTION_STRING"]
                ?? config["ApplicationInsights:ConnectionString"];
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return connectionString;
            }

            var instrumentationKey = config["APPINSIGHTS_INSTRUMENTATIONKEY"];
            if (!string.IsNullOrWhiteSpace(instrumentationKey))
            {
                return $"InstrumentationKey={instrumentationKey}";
            }

            return config["ApplicationInsights:StagingConnectionString"];
        }
    }
}
