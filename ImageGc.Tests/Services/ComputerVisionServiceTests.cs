using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Services;
using Azure;
using Azure.AI.Vision.ImageAnalysis;

namespace ImageGc.Tests.Services;

/// <summary>
/// Integration tests for Computer Vision Service API connections
/// </summary>
public class ComputerVisionServiceTests : TestBase
{
    private readonly IComputerVisionService _computerVisionService;

    public ComputerVisionServiceTests()
    {
        _computerVisionService = ServiceProvider.GetRequiredService<IComputerVisionService>();
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        services.AddScoped<IComputerVisionService, ComputerVisionService>();
        services.AddScoped<ILogger<ComputerVisionService>>(provider =>
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<ComputerVisionService>());
    }

    [Fact]
    public async Task AnalyzeImageAsync_WithValidImage_ShouldReturnDescription()
    {
        // Arrange
        var imageData = GetTestImageData();

        // Act
        var result = await _computerVisionService.AnalyzeImageAsync(imageData);

        // Assert
        Assert.NotNull(result.Description);
        Assert.NotEmpty(result.Description);
        Assert.True(result.ProcessingTimeMs > 0);
        Assert.True(result.ConfidenceScore >= 0 && result.ConfidenceScore <= 1);
    }

    [Fact]
    public async Task AnalyzeImageAsync_WithNullImage_ShouldThrowException()
    {
        // Arrange
        byte[]? imageData = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _computerVisionService.AnalyzeImageAsync(imageData!));
    }

    [Fact]
    public async Task AnalyzeImageAsync_WithEmptyImage_ShouldThrowException()
    {
        // Arrange
        var imageData = Array.Empty<byte>();

        // Act & Assert
        // The service now validates empty images before calling Azure API
        await Assert.ThrowsAsync<ArgumentException>(
            () => _computerVisionService.AnalyzeImageAsync(imageData));
    }

    [Fact]
    public void ComputerVisionService_Configuration_ShouldBeValid()
    {
        // Arrange & Act
        var endpoint = Configuration["ComputerVision:Endpoint"];
        var apiKey = Configuration["ComputerVision:ApiKey"];

        // Assert
        Assert.NotNull(endpoint);
        Assert.NotEmpty(endpoint);
        Assert.True(Uri.IsWellFormedUriString(endpoint, UriKind.Absolute));

        Assert.NotNull(apiKey);
        Assert.NotEmpty(apiKey);
    }

    /// <summary>
    /// Test connection to Computer Vision API endpoint
    /// </summary>
    [Fact]
    public async Task ComputerVisionService_ConnectionTest_ShouldConnect()
    {
        if (!HasRealApiKeys())
        {
            // Skip connection test if using placeholder keys
            return;
        }

        // Arrange
        var endpoint = Configuration["ComputerVision:Endpoint"];
        var apiKey = Configuration["ComputerVision:ApiKey"];

        // Act & Assert
        try
        {
            var client = new ImageAnalysisClient(new Uri(endpoint!), new AzureKeyCredential(apiKey!));
            var imageData = GetTestImageData();

            // Simple connection test - just try to analyze a small image
            using var stream = new MemoryStream(imageData);
            var result = await client.AnalyzeAsync(
                BinaryData.FromStream(stream),
                VisualFeatures.Caption | VisualFeatures.Tags);

            Assert.NotNull(result);
            Assert.NotNull(result.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 401)
        {
            Assert.Fail($"Authentication failed. Check Computer Vision API key. Error: {ex.Message}");
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            // Rate limit exceeded - this actually indicates successful connection
            Assert.True(true, "Connection successful but rate limited");
        }
        catch (Exception ex)
        {
            Assert.Fail($"Unexpected error connecting to Computer Vision API: {ex.Message}");
        }
    }

    [Fact]
    public async Task ComputerVisionService_PerformanceTest_ShouldCompleteWithinTimeout()
    {
        if (!HasRealApiKeys())
        {
            return; // Skip if no real API keys
        }

        // Arrange
        var imageData = GetTestImageData();
        var timeout = TimeSpan.FromSeconds(30);

        // Act
        var startTime = DateTime.UtcNow;

        try
        {
            using var cts = new CancellationTokenSource(timeout);
            var result = await _computerVisionService.AnalyzeImageAsync(imageData);
            var endTime = DateTime.UtcNow;

            // Assert
            Assert.True(endTime - startTime < timeout, "Operation should complete within timeout");
            Assert.True(result.ProcessingTimeMs > 0, "Processing time should be recorded");
        }
        catch (OperationCanceledException)
        {
            Assert.Fail($"Computer Vision API call timed out after {timeout.TotalSeconds} seconds");
        }
    }
}
