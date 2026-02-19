using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PoImageGc.Web.Components;
using PoImageGc.Web.Features.Diagnostics;
using PoImageGc.Web.Features.ImageAnalysis;
using PoImageGc.Web.Models;
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

var builder = WebApplication.CreateBuilder(args);

// ─── Azure Key Vault ────────────────────────────────────────────────
// Load FIRST so ApplicationInsights:ConnectionString is available when Serilog is configured.
// Secrets mapped via KeyVaultSecretNameMapping.
var keyVaultEndpoint = builder.Configuration["AZURE_KEY_VAULT_ENDPOINT"];
if (!string.IsNullOrEmpty(keyVaultEndpoint))
{
    try
    {
        var credential = new DefaultAzureCredential();
        var secretClient = new SecretClient(new Uri(keyVaultEndpoint), credential);
        builder.Configuration.AddAzureKeyVault(secretClient, new KeyVaultSecretNameMapping());
    }
    catch (Exception ex)
    {
        Log.Warning(ex,
            "Key Vault at {Endpoint} is unreachable; secrets will not be loaded. "
            + "Application Insights and other Key Vault-dependent features may be unavailable.",
            keyVaultEndpoint);
    }
}

// ─── Serilog ────────────────────────────────────────────────────────
// Structured logging: Console in Development, Application Insights in Production
var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "PoImageGc")
    .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
    .Enrich.WithMachineName()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/poredoimage-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate:
            "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.Conditional(_ => !string.IsNullOrEmpty(appInsightsConnectionString),
        sink => sink.ApplicationInsights(appInsightsConnectionString!, TelemetryConverter.Traces))
    .CreateLogger();

builder.Host.UseSerilog();

// ─── OpenTelemetry ──────────────────────────────────────────────────
// Global OpenTelemetry: traces + metrics exported to Application Insights via OTLP
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("PoImageGc", serviceVersion: "1.0.0"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation());

// If an OTLP exporter endpoint is configured, export telemetry there
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
if (!string.IsNullOrEmpty(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing.AddOtlpExporter())
        .WithMetrics(metrics => metrics.AddOtlpExporter());
}

// ─── Core services ──────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOpenApi();

// HTTP client factory (used by health checks)
builder.Services.AddHttpClient();

// ─── Health checks ──────────────────────────────────────────────────
// Named checks verify connectivity to Computer Vision and OpenAI endpoints
builder.Services.AddHealthChecks()
    .AddCheck<ComputerVisionHealthCheck>("computer-vision", tags: ["ready"])
    .AddCheck<OpenAIHealthCheck>("openai", tags: ["ready"]);

// ─── HTTP client ────────────────────────────────────────────────────
builder.Services.AddScoped(sp =>
{
    var httpClient = new HttpClient();
    var navigationManager = sp.GetRequiredService<NavigationManager>();
    httpClient.BaseAddress = new Uri(navigationManager.BaseUri);
    return httpClient;
});

// ─── Feature services (Vertical Slice Architecture) ─────────────────
builder.Services.AddScoped<IComputerVisionService, ComputerVisionService>();
builder.Services.AddScoped<IOpenAIService, OpenAIService>();

// MemeGeneratorService — cross-platform via SixLabors.ImageSharp (no longer Windows-only)
builder.Services.AddScoped<IMemeGeneratorService, MemeGeneratorService>();

var app = builder.Build();

// ─── Middleware pipeline ────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();

// Correlation ID must precede request logging so {CorrelationId} is in scope
app.UseMiddleware<CorrelationIdMiddleware>();

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

app.UseAntiforgery();

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
app.MapImageAnalysisEndpoints();
app.MapDiagnosticsEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Make Program class accessible to integration tests
public partial class Program { }
