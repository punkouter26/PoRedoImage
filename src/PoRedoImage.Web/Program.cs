using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PoRedoImage.Infrastructure;
using PoRedoImage.Web.Components;
using PoRedoImage.Web.Configuration;
using PoRedoImage.Web.Features.Auth;
using PoRedoImage.Web.Features.BulkGenerate;
using PoRedoImage.Web.Features.CaptionBattle;
using PoRedoImage.Web.Features.Diagnostics;
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

// ─── Azure Key Vault ────────────────────────────────────────────────
// Load FIRST so ApplicationInsights:ConnectionString is available when Serilog is configured.
// Secrets mapped via KeyVaultSecretNameMapping.
var keyVaultEndpoint = builder.Configuration["KeyVault:Uri"] ?? builder.Configuration["AZURE_KEY_VAULT_ENDPOINT"];
if (!string.IsNullOrEmpty(keyVaultEndpoint))
{
    try
    {
        var credential = new DefaultAzureCredential();
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

        // In Development, Key Vault would override appsettings.Development.json values (KV is the last
        // provider added). Pin Azurite back so local storage works regardless of what KV provides.
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

// ─── Serilog ────────────────────────────────────────────────────────
// Structured logging: Console in Development, Application Insights in Production
var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];

// In Azure App Service with Run-From-Package (OneDeploy), /home/site/wwwroot/ is read-only.
// Use /home/LogFiles/Application/ (writable) in production; use relative path in development.
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

// ─── OpenTelemetry → Azure Monitor ─────────────────────────────────
// Exports traces & metrics directly to Application Insights; no separate OTLP collector needed.
// When connection string is absent (local dev / test), instrumentation is still active but
// telemetry is not exported anywhere — zero cost, zero failures.
var otelBuilder = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("PoRedoImage", serviceVersion: "1.0.0"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation());

var appInsightsCs = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsightsCs))
{
    otelBuilder.UseAzureMonitor(options => options.ConnectionString = appInsightsCs);
}

// ─── Core services ──────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// Register Radzen services on the server so SSR pre-rendering can resolve
// Radzen-injected properties on Client WASM components (e.g. NotificationService).
builder.Services.AddRadzenComponents();

builder.Services.AddOpenApi();

// HTTP client factory (used by health checks)
builder.Services.AddHttpClient();

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
builder.Services.AddPoRedoImageInfrastructure();

// Scoped: persists the active uploaded image across feature pages for the lifetime of the circuit
builder.Services.AddScoped<PoRedoImage.Web.Components.Shared.ImageSessionService>();

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
app.UseHttpsRedirection();

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
    .AddInteractiveServerRenderMode()
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
