using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PoRedoImage.Infrastructure;
using PoRedoImage.Shared.Json;
using PoRedoImage.Web.Components;
using PoRedoImage.Web.Configuration;
using PoRedoImage.Web.Features.Auth;
using PoRedoImage.Web.Features.BulkGenerate;
using PoRedoImage.Web.Features.CaptionBattle;
using PoRedoImage.Web.Features.Diagnostics;
using PoRedoImage.Web.Features.Idempotency;
using PoRedoImage.Web.Features.ImageAnalysis;
using PoRedoImage.Web.Features.MemeTemplates;
using PoRedoImage.Web.Features.StyleDirector;
using PoRedoImage.Web.Features.UserImages;
using Radzen;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using Scalar.AspNetCore;

// ─── Bootstrap logger ───────────────────────────────────────────────
// Captures startup/Key Vault failures before the full Serilog pipeline is ready.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{

    var builder = WebApplication.CreateBuilder(args);

    // ─── BOMB-2: Bound request body size (Po2Logic mitigation) ──────────────────
    // Default Kestrel limit is 30 MB. We allow 25 MB for image uploads (matches the
    // 20 MB client-side cap + JSON envelope overhead). Prevents 50 MB base64 payloads
    // from OOM-killing the ACA pod.
    const int MaxRequestBodyBytes = 25 * 1024 * 1024;
    builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = MaxRequestBodyBytes);
    builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = MaxRequestBodyBytes);

    // ─── Azure Key Vault ────────────────────────────────────────────────
    // Load FIRST so ApplicationInsights:ConnectionString is available when Serilog is configured.
    // Secrets mapped via KeyVaultSecretNameMapping.
    var keyVaultEndpoint = builder.Configuration["KeyVault:Uri"] ?? builder.Configuration["AZURE_KEY_VAULT_ENDPOINT"];
    if (!string.IsNullOrEmpty(keyVaultEndpoint))
    {
        try
        {
            // In App Service only the managed identity matters. Excluding the dev/
            // interactive probes (which can each add seconds while failing) keeps the
            // cold-start token fast — a slow first token previously yielded an empty
            // Key Vault load and crash-looped the container. Secrets are also bound
            // as App Service Key Vault references (see infra/main.bicep) so config is
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
            var secretManager = new KeyVaultSecretNameMapping();

            // ReloadInterval: re-fetch secrets every 30 minutes for secret rotation without restart.
            builder.Configuration.AddAzureKeyVault(
                new Uri(keyVaultEndpoint),
                credential,
                new AzureKeyVaultConfigurationOptions
                {
                    Manager = secretManager,
                    ReloadInterval = TimeSpan.FromMinutes(30)
                });

            Log.Information("Key Vault configuration loaded from {Endpoint}", keyVaultEndpoint);

            // In Development, Key Vault would override appsettings.Development.json values (KV is the
            // last provider added). Pin Storage back to Azurite (Docker) regardless of what KV provides.
            // The chat deployment name is no longer pinned here: it is no longer a Key Vault secret
            // (see KeyVaultSecretNameMapping), so appsettings.json is the single source of truth for it.
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

            // In production fail-fast if key vault cannot be read, otherwise continue in Dev.
            if (!builder.Environment.IsDevelopment())
            {
                Log.Fatal(ex, "Key Vault startup validation failed, terminating app startup.");
                throw;
            }
        }
    }

    // ─── Application Insights connection string resolution (§8) ─────────
    // Resolution order: APPLICATIONINSIGHTS_CONNECTION_STRING → APPINSIGHTS_INSTRUMENTATIONKEY
    // → hardcoded staging fallback. Resolved once and shared by Serilog + OpenTelemetry below.
    var appInsightsConnectionString = ResolveAppInsightsConnectionString(builder.Configuration);

    static string? ResolveAppInsightsConnectionString(IConfiguration config)
    {
        var resolved = ResolveRaw(config);

        // Telemetry misconfiguration must NEVER crash the app. The Azure Monitor / App Insights SDKs
        // throw "Connection String Invalid: InstrumentationKey is required." when handed a value that
        // has no InstrumentationKey — which previously fail-fasted the whole host in Production and
        // drove it into a cold-start crash loop (which in turn exhausted the F1 daily CPU quota and
        // disabled the site). Treat a present-but-malformed connection string as "no telemetry":
        // export is skipped (instrumentation still runs locally) and the app starts normally.
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
            // 1. Full connection string (env var or config key, e.g. App Service setting / Key Vault).
            var connectionString = config["APPLICATIONINSIGHTS_CONNECTION_STRING"]
                ?? config["ApplicationInsights:ConnectionString"];
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return connectionString;
            }

            // 2. Legacy instrumentation key → synthesize a connection string.
            var instrumentationKey = config["APPINSIGHTS_INSTRUMENTATIONKEY"];
            if (!string.IsNullOrWhiteSpace(instrumentationKey))
            {
                return $"InstrumentationKey={instrumentationKey}";
            }

            // 3. Hardcoded staging fallback. Populate "ApplicationInsights:StagingConnectionString"
            //    (appsettings or Key Vault) to keep telemetry flowing if the primary sources are absent;
            //    null disables export rather than fabricating a credential.
            return config["ApplicationInsights:StagingConnectionString"];
        }
    }

    // ─── Serilog ────────────────────────────────────────────────────────
    // Structured logging: Console in Development, Application Insights in Production

    // In Azure App Service with Run-From-Package (OneDeploy), /home/site/wwwroot/ is read-only.
    // Use /home/LogFiles/Application/ (writable) in production; use relative path in development.
    // On Azure Container Apps (ACA) the file system is ephemeral and only stdout is collected —
    // fall back to console-only there. Detected via the presence of the CONTAINER_APP_NAME env var.
    var isContainerApps = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CONTAINER_APP_NAME"));
    var logFilePath = builder.Environment.IsDevelopment()
        ? "logs/poredoimage-.log"
        : isContainerApps
            ? null  // stdout-only in ACA — no file sink
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
        .WriteTo.Conditional(_ => !string.IsNullOrEmpty(logFilePath), sink => sink
            .File(
                path: logFilePath!,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate:
                    "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj} {Properties:j}{NewLine}{Exception}"))
        .WriteTo.Conditional(_ => !string.IsNullOrEmpty(appInsightsConnectionString),
            sink => sink.ApplicationInsights(appInsightsConnectionString!, TelemetryConverter.Traces))
        .CreateLogger();

    builder.Host.UseSerilog();

    // ─── OpenTelemetry → Azure Monitor ─────────────────────────────────
    // Exports traces & metrics directly to Application Insights; no separate OTLP collector needed.
    // When connection string is absent (local dev / test), instrumentation is still active but
    // telemetry is not exported anywhere — zero cost, zero failures.
    // cloud_RoleName resolved via reflection from the real entry assembly so the App Insights
    // "Cloud role name" is the actual app name and never the unknown_service:dotnet default (§8).
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

    if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
    {
        // Sampling profiles (§8): full fidelity (100%) in Dev/Test so nothing is dropped while
        // debugging; a configurable ceiling (default 10%) in Production to cap trace volume. The
        // ratio is read from ApplicationInsights:SamplingRatio so it can be tuned without a redeploy.
        // Live Metrics (QuickPulse) stays unsampled at 100% regardless.
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
        // after UseAzureMonitor so this SetSampler wins. Exceptions thrown mid-request are still
        // captured at 100% via the unsampled Serilog → App Insights sink above (defence in depth).
        builder.Services.ConfigureOpenTelemetryTracerProvider((_, tracerProvider) =>
            tracerProvider.SetSampler(new ErrorPreservingSampler(samplingRatio)));
    }

    // ─── Core services ──────────────────────────────────────────────────
    // Global Interactive WebAssembly (§1 BFF model): the whole UI runs in the browser; this
    // project is the API/BFF host only. AddAuthenticationStateSerialization flows the cookie-
    // authenticated principal (claims only, never tokens) to the WASM AuthenticationStateProvider.
    builder.Services.AddRazorComponents()
        .AddInteractiveWebAssemblyComponents()
        .AddAuthenticationStateSerialization();

    // Register Radzen services on the server so SSR pre-rendering can resolve
    // Radzen-injected properties on Client WASM components (e.g. NotificationService).
    builder.Services.AddRadzenComponents();

    builder.Services.AddOpenApi();

    // ─── JSON source-gen (PoNetCaching §7) ──────────────────────────────────────
    // Wire the shared DTO JsonSerializerContext into the minimal-API serializer so
    // request/response serialization is reflection-free and trim-safe. The default
    // resolver stays first in the chain so anonymous types (e.g. health-check
    // responses) and framework types fall back to reflection.
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.TypeInfoResolver = JsonTypeInfoResolver.Combine(
            new DefaultJsonTypeInfoResolver(),
            new SharedJsonContext());
        options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

    // ─── Strongly-typed options (Po2Logic R3) ──────────────────────────────────
    // Options-pattern binding with IValidateOptions<T> gives us hot-reloadable config
    // (Key Vault rotates every 30 min) AND startup-time validation for required fields.
    builder.Services.AddOptions<OpenAiOptions>()
        .Bind(builder.Configuration.GetSection(OpenAiOptions.SectionName))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<OpenAiOptions>, OpenAiOptionsValidator>();

    builder.Services.AddOptions<StorageOptions>()
        .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();

    // Fail-fast secret validator (Po2Logic F7)
    builder.Services.AddHostedService<StartupSecretValidator>();

    // HTTP client factory (used by health checks)
    builder.Services.AddHttpClient();

    // ─── Idempotency (Po2Logic R5 / F6) ─────────────────────────────────────────
    // IMemoryCache backs the de-dup; IEndpointFilter applied to Write endpoints via
    // [IdempotencyRequired] marker attribute. 24h TTL prevents replays across days.
    builder.Services.AddMemoryCache();
    builder.Services.AddScoped<IdempotencyKeyFilter>();

    // ─── Rate limiting ──────────────────────────────────────────────────
    // Protect costly AI endpoints: 10 requests/minute per authenticated user (falls back to IP).
    // Returns HTTP 429 when the limit is exceeded.
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        // Partition by user ID so one user's bulk batch cannot starve other users on the same IP
        options.AddPolicy("ai-endpoints", context =>
        {
            var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous";
            return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 2
            });
        });
    });

    // ─── Health checks ──────────────────────────────────────────────────
    // Named checks verify connectivity to Computer Vision and OpenAI endpoints
    builder.Services.AddHealthChecks()
        .AddCheck<KeyVaultHealthCheck>("key-vault", tags: ["ready"])
        .AddCheck<ComputerVisionHealthCheck>("computer-vision", tags: ["ready"])
        .AddCheck<OpenAIHealthCheck>("openai", tags: ["ready"])
        .AddCheck<BulkPromptStorageHealthCheck>("table-storage", tags: ["ready"])
        .AddCheck<Imagen3HealthCheck>("imagen3", tags: ["ready"]);

    // ─── HTTP client ────────────────────────────────────────────────────
    builder.Services.AddScoped(sp =>
    {
        var nav = sp.GetRequiredService<NavigationManager>();
        return new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(4),
            BaseAddress = new Uri(nav.BaseUri)
        };
    });

    // ─── Feature services (Onion Architecture — Infrastructure layer wires all services) ──
    // DI registration follows Dependency Inversion Principle (SOLID-D)
    builder.Services.AddPoRedoImageInfrastructure(builder.Configuration);

    // ImageSessionService is a client-side (WASM) concern now — registered in the Client host.

    // ─── Authentication & Authorization ─────────────────────────────────
    builder.Services.AddPoRedoImageAuth(builder.Configuration, builder.Environment);

    var app = builder.Build();

    // ─── Middleware pipeline ────────────────────────────────────────────
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    // Only apply the "pretty error page" re-execute for browser (non-API) paths.
    // API requests must keep their original 4xx/5xx status codes so clients
    // don't receive a 302 redirect to the login page instead of a real 401.
    app.UseWhen(
        ctx => !ctx.Request.Path.StartsWithSegments("/api"),
        branch => branch.UseStatusCodePagesWithReExecute("/not-found"));

    // HTTPS redirect is skipped in Development so the E2E suite (default base URL
    // http://localhost:5000) gets real status codes instead of a 307 to https — the dev cert
    // already secures :5001 for interactive use. Production/Staging keep the redirect + HSTS.
    if (!app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }

    // Pushes CorrelationId, UserId, and SessionId into Serilog LogContext for every request
    app.UseMiddleware<RequestContextMiddleware>();

    // Structured request logging: one entry per request with timing and status
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        opts.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? string.Empty);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("CorrelationId",
                httpContext.Response.Headers["X-Correlation-ID"].FirstOrDefault() ?? string.Empty);
        };
    });

    app.UseRateLimiter();
    app.UseAntiforgery();
    app.UseAuthentication();
    app.UseAuthorization();

    // OpenAPI + Scalar API documentation
    app.MapOpenApi();
    app.MapScalarApiReference();

    // Health check endpoints
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                Status = report.Status.ToString(),
                Duration = report.TotalDuration.TotalMilliseconds,
                Entries = report.Entries.Select(e => new
                {
                    e.Key,
                    Status = e.Value.Status.ToString(),
                    Duration = e.Value.Duration.TotalMilliseconds,
                    e.Value.Description
                })
            });
        }
    });
    app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = _ => false });

    // Minimal API endpoints (Vertical Slice)
    app.MapAuthEndpoints();
    app.MapImageAnalysisEndpoints();
    app.MapDiagnosticsEndpoints();
    app.MapBulkGenerateEndpoints();
    app.MapUserImageEndpoints();
    app.MapMemeTemplateEndpoints();
    app.MapCaptionBattleEndpoints();
    app.MapStyleDirectorEndpoints();

    // Redirect /favicon.ico → /favicon.png so browsers don't get a 404.
    app.MapGet("/favicon.ico", () => Results.Redirect("/favicon.png", permanent: true))
        .ExcludeFromDescription();

    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(typeof(PoRedoImage.Client._Imports).Assembly);

    app.Run();

}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "PoRedoImage terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Make Program class accessible to integration tests
public partial class Program { }
