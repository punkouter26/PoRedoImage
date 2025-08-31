using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Moq;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;

namespace ImageGc.Tests;

/// <summary>
/// Base class for integration tests providing common setup and configuration
/// </summary>
public abstract class TestBase : IDisposable
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly IConfiguration Configuration;

    protected TestBase()
    {
        // Build configuration
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json", optional: false)
            .AddEnvironmentVariables();

        Configuration = configBuilder.Build();

        // Build service collection
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(Configuration);
        services.AddLogging(builder => builder.AddConsole());

        // Mock IWebHostEnvironment for services that might depend on it
        var hostingEnvironmentMock = new Mock<IWebHostEnvironment>();
        hostingEnvironmentMock.Setup(e => e.ApplicationName).Returns("ImageGc.Tests");
        hostingEnvironmentMock.Setup(e => e.EnvironmentName).Returns("Development");
        services.AddSingleton(hostingEnvironmentMock.Object);

        // Configure a real TelemetryClient for testing, but disable sending telemetry
        var telemetryConfiguration = TelemetryConfiguration.CreateDefault();
        telemetryConfiguration.DisableTelemetry = true;
        services.AddSingleton(new TelemetryClient(telemetryConfiguration));
    }

    /// <summary>
    /// Gets a test image as byte array (generated programmatically)
    /// </summary>
    public static byte[] GetTestImageData()
    {
        // Generate a simple 100x100 pixel PNG image with a red background
        using var bitmap = new System.Drawing.Bitmap(100, 100);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);

        // Fill with red background
        graphics.Clear(System.Drawing.Color.Red);

        // Add a simple blue circle in the center
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Blue);
        graphics.FillEllipse(brush, 25, 25, 50, 50);

        // Convert to PNG bytes
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>
    /// Checks if configuration contains real API keys (not test placeholders)
    /// </summary>
    protected bool HasRealApiKeys()
    {
        var computerVisionKey = Configuration["ComputerVision:ApiKey"];
        var openAiKey = Configuration["OpenAI:ApiKey"];

        return !string.IsNullOrEmpty(computerVisionKey) &&
               !computerVisionKey.Contains("placeholder") &&
               !string.IsNullOrEmpty(openAiKey) &&
               !openAiKey.Contains("placeholder");
    }
    public virtual void Dispose()
    {
        if (ServiceProvider is IDisposable disposableServiceProvider)
        {
            disposableServiceProvider.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
