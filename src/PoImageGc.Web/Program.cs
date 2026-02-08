using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;
using Microsoft.ApplicationInsights.Extensibility;
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

var builder = WebApplication.CreateBuilder(args);

// ─── Azure Key Vault ────────────────────────────────────────────────
// Key Vault integration for all environments; secrets mapped via KeyVaultSecretNameMapping
var keyVaultEndpoint = builder.Configuration["AZURE_KEY_VAULT_ENDPOINT"];
if (!string.IsNullOrEmpty(keyVaultEndpoint))
{
    var credential = new DefaultAzureCredential();
    var secretClient = new SecretClient(new Uri(keyVaultEndpoint), credential);
    builder.Configuration.AddAzureKeyVault(secretClient, new KeyVaultSecretNameMapping());
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
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
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

// Application Insights SDK (works alongside OpenTelemetry for rich .NET telemetry)
builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddSingleton<Microsoft.ApplicationInsights.TelemetryClient>();

// ─── Health checks ──────────────────────────────────────────────────
// Verifies connectivity to all external API dependencies
builder.Services.AddHealthChecks();

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

// MemeGeneratorService — Platform Adapter pattern: Windows-only System.Drawing
if (OperatingSystem.IsWindows())
{
    builder.Services.AddScoped<IMemeGeneratorService, MemeGeneratorService>();
}
else
{
    builder.Services.AddScoped<IMemeGeneratorService, NullMemeGeneratorService>();
}

var app = builder.Build();

// ─── Middleware pipeline ────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();
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

/// <summary>
/// Maps Key Vault secret names to configuration keys.
/// Implements the Adapter pattern to bridge Key Vault naming convention
/// (e.g., "ComputerVision-ApiKey") with .NET configuration keys (e.g., "ComputerVision:ApiKey").
/// </summary>
public class KeyVaultSecretNameMapping : KeyVaultSecretManager
{
    private static readonly Dictionary<string, string> SecretMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ComputerVision-ApiKey"] = "ComputerVision:ApiKey",
        ["ComputerVision-Endpoint"] = "ComputerVision:Endpoint",
        ["AzureOpenAI-ApiKey"] = "OpenAI:Key",
        ["AzureOpenAI-Endpoint"] = "OpenAI:Endpoint",
        ["AzureOpenAI-DeploymentName"] = "OpenAI:ChatCompletionsDeployment",
        ["AzureOpenAI-ImageEndpoint"] = "OpenAI:ImageEndpoint",
        ["AzureOpenAI-ImageKey"] = "OpenAI:ImageKey",
        ["ApplicationInsights-ConnectionString"] = "ApplicationInsights:ConnectionString",
        ["PoRedoImage-StorageConnectionString"] = "Storage:ConnectionString"
    };

    public override bool Load(SecretProperties secret)
    {
        // Only load secrets that are relevant to this application
        return SecretMappings.ContainsKey(secret.Name);
    }

    public override string GetKey(KeyVaultSecret secret)
    {
        // Map Key Vault secret name to configuration key
        return SecretMappings.TryGetValue(secret.Name, out var configKey) 
            ? configKey 
            : secret.Name.Replace("--", ":");
    }
}
