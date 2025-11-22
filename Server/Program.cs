using Microsoft.ApplicationInsights.Extensibility;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using Server.Services;
using Server.Services.HealthChecks;
using System.Net;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Configure Azure Key Vault for all environments
// Local development uses DefaultAzureCredential which falls back to Azure CLI/Visual Studio credentials
// Production uses Managed Identity
var keyVaultEndpoint = builder.Configuration["AZURE_KEY_VAULT_ENDPOINT"] 
    ?? "https://kv-poredoimage.vault.azure.net/";

if (!string.IsNullOrEmpty(keyVaultEndpoint))
{
    try
    {
        // Use Managed Identity in Azure, Azure CLI credentials locally
        var credential = new DefaultAzureCredential();
        builder.Configuration.AddAzureKeyVault(new Uri(keyVaultEndpoint), credential);
        
        Log.Information("Key Vault configured: {KeyVaultEndpoint} (Environment: {Environment})", 
            keyVaultEndpoint, builder.Environment.EnvironmentName);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Failed to configure Key Vault. Falling back to appsettings.json");
    }
}
else
{
    Log.Warning("AZURE_KEY_VAULT_ENDPOINT not configured. Secrets will be read from appsettings.json");
}

// Get Application Insights connection string for Serilog
var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];

// Configure Serilog with structured logging and Application Insights sink
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "ImageGc")
    .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
    .Enrich.WithMachineName()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        path: "log.txt",
        shared: true,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.ApplicationInsights(
        connectionString: appInsightsConnectionString,
        telemetryConverter: TelemetryConverter.Traces)
    .CreateLogger();

builder.Host.UseSerilog();

// Configure OpenTelemetry for modern telemetry and metrics
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("PoRedoImage")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
            ["service.version"] = "1.0.0"
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter("PoRedoImage.Api") // Custom metrics meter
        .AddOtlpExporter())
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());

// Add services to the container.
builder.Services.AddApplicationInsightsTelemetry();

// Ensure TelemetryClient is properly registered
builder.Services.AddSingleton<Microsoft.ApplicationInsights.TelemetryClient>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// Add OpenAPI with Scalar documentation
builder.Services.AddOpenApi();

// Add health checks with custom health check classes for external dependencies
builder.Services.AddHealthChecks()
    .AddCheck<AzureTableStorageHealthCheck>("AzureTableStorage")
    .AddCheck<ComputerVisionHealthCheck>("ComputerVision")
    .AddCheck<OpenAIHealthCheck>("OpenAI");

// Register Azure services
builder.Services.AddHttpClient();
builder.Services.AddScoped<IComputerVisionService, ComputerVisionService>();
builder.Services.AddScoped<IOpenAIService, OpenAIService>();
builder.Services.AddScoped<IMemeGeneratorService, MemeGeneratorService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Map OpenAPI endpoint and enable Scalar UI
app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection(); // Enabled for proper HTTPS handling
app.UseBlazorFrameworkFiles(); // Serve Blazor WebAssembly static files
app.UseStaticFiles();

app.UseRouting();

app.MapControllers();
app.MapHealthChecks("/api/health");

app.MapFallbackToFile("index.html");

try
{
    Log.Information("Starting web host");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Make Program class public for testing
public partial class Program { }
