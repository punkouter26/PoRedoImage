using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;
using Microsoft.ApplicationInsights.Extensibility;
using PoImageGc.Web.Components;
using PoImageGc.Web.Features.ImageAnalysis;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (OpenTelemetry, health checks, resilience)
builder.AddServiceDefaults();

// Configure Azure Key Vault for production with secret name mapping
var keyVaultEndpoint = builder.Configuration["AZURE_KEY_VAULT_ENDPOINT"];
if (!string.IsNullOrEmpty(keyVaultEndpoint) && builder.Environment.IsProduction())
{
    var credential = new DefaultAzureCredential();
    var secretClient = new SecretClient(new Uri(keyVaultEndpoint), credential);
    builder.Configuration.AddAzureKeyVault(secretClient, new KeyVaultSecretNameMapping());
}

// Get Application Insights connection string for Serilog
var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
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

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add OpenAPI with Scalar documentation
builder.Services.AddOpenApi();

// Add Application Insights
builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddSingleton<Microsoft.ApplicationInsights.TelemetryClient>();

// Add HttpClient
builder.Services.AddHttpClient();

// Register Feature services (Vertical Slice)
builder.Services.AddScoped<IComputerVisionService, ComputerVisionService>();
builder.Services.AddScoped<IOpenAIService, OpenAIService>();

// MemeGeneratorService is Windows-only due to System.Drawing dependency
if (OperatingSystem.IsWindows())
{
    builder.Services.AddScoped<IMemeGeneratorService, MemeGeneratorService>();
}
else
{
    builder.Services.AddScoped<IMemeGeneratorService, NullMemeGeneratorService>();
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();
app.UseAntiforgery();

// Map OpenAPI endpoint and enable Scalar UI
app.MapOpenApi();
app.MapScalarApiReference();

// Map default Aspire endpoints (health checks)
app.MapDefaultEndpoints();

// Map Minimal API endpoints (Vertical Slice)
app.MapImageAnalysisEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Make Program class accessible to integration tests
public partial class Program { }

/// <summary>
/// Maps Key Vault secret names to configuration keys for this application.
/// Key Vault secrets use format: "PoRedoImage-SecretName" or "AzureOpenAI-SecretName"
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
