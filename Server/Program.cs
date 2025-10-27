using Microsoft.ApplicationInsights.Extensibility;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using Server.Services;
using Server.Services.HealthChecks;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

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

// Add services to the container.
builder.Services.AddApplicationInsightsTelemetry();

// Ensure TelemetryClient is properly registered
builder.Services.AddSingleton<Microsoft.ApplicationInsights.TelemetryClient>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add health checks with custom health check classes for external dependencies
builder.Services.AddHealthChecks()
    .AddCheck<AzureTableStorageHealthCheck>("AzureTableStorage")
    .AddCheck<ComputerVisionHealthCheck>("ComputerVision")
    .AddCheck<OpenAIHealthCheck>("OpenAI");

// Add CORS policy for development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Register Azure services
builder.Services.AddHttpClient();
builder.Services.AddScoped<IComputerVisionService, ComputerVisionService>();
builder.Services.AddScoped<IOpenAIService, OpenAIService>();

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

// Enable Swagger in all environments (including production)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ImageGc API V1");
    c.RoutePrefix = "swagger"; // Available at /swagger
});

// Enable CORS for all environments (required for Azure hosting)
app.UseCors("AllowAll");

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
