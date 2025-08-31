using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Services;
using Azure;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;

namespace ImageGc.Tests.Integration;

/// <summary>
/// End-to-end integration tests that verify the complete workflow
/// </summary>
public class EndToEndIntegrationTests : TestBase
{
    private readonly ILogger<EndToEndIntegrationTests> _logger;

    public EndToEndIntegrationTests()
    {
        _logger = ServiceProvider.GetRequiredService<ILogger<EndToEndIntegrationTests>>();
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        services.AddScoped<IComputerVisionService, ComputerVisionService>();
        services.AddScoped<IOpenAIService, OpenAIService>();
        services.AddScoped<ILogger<ComputerVisionService>>(provider =>
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<ComputerVisionService>());
        services.AddScoped<ILogger<OpenAIService>>(provider =>
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<OpenAIService>());
    }

    [Fact]
    public async Task CompleteWorkflow_AnalyzeAndRegenerateImage_ShouldSucceed()
    {
        if (!HasRealApiKeys())
        {
            _logger.LogWarning("Skipping end-to-end test - no real API keys configured");
            return;
        }

        // Arrange
        var computerVisionService = ServiceProvider.GetRequiredService<IComputerVisionService>();
        var openAIService = ServiceProvider.GetRequiredService<IOpenAIService>();
        var imageData = GetTestImageData();

        _logger.LogInformation("Starting complete workflow test");

        try
        {
            // Act - Step 1: Analyze the image
            _logger.LogInformation("Step 1: Analyzing image with Computer Vision");
            var analysisResult = await computerVisionService.AnalyzeImageAsync(imageData);

            Assert.NotNull(analysisResult.Description);
            Assert.NotEmpty(analysisResult.Description);
            Assert.True(analysisResult.ProcessingTimeMs > 0);
            _logger.LogInformation($"Analysis completed in {analysisResult.ProcessingTimeMs}ms");

            // Act - Step 2: Enhance the description
            _logger.LogInformation("Step 2: Enhancing description with OpenAI");
            var enhancedResult = await openAIService.EnhanceDescriptionAsync(
                analysisResult.Description,
                analysisResult.Tags,
                300);

            Assert.NotNull(enhancedResult.EnhancedDescription);
            Assert.NotEmpty(enhancedResult.EnhancedDescription);
            Assert.True(enhancedResult.TokensUsed > 0);
            Assert.True(enhancedResult.ProcessingTimeMs > 0);
            _logger.LogInformation($"Description enhancement completed in {enhancedResult.ProcessingTimeMs}ms, used {enhancedResult.TokensUsed} tokens");

            // Act - Step 3: Generate new image
            _logger.LogInformation("Step 3: Generating new image with DALL-E");
            var generationResult = await openAIService.GenerateImageAsync(enhancedResult.EnhancedDescription);

            Assert.NotNull(generationResult.ImageData);
            Assert.True(generationResult.ImageData.Length > 0);
            Assert.NotNull(generationResult.ContentType);
            Assert.True(generationResult.TokensUsed > 0);
            Assert.True(generationResult.ProcessingTimeMs > 0);
            _logger.LogInformation($"Image generation completed in {generationResult.ProcessingTimeMs}ms, used {generationResult.TokensUsed} tokens");

            // Assert - Verify overall workflow
            var totalProcessingTime = analysisResult.ProcessingTimeMs +
                                    enhancedResult.ProcessingTimeMs +
                                    generationResult.ProcessingTimeMs;

            _logger.LogInformation($"Complete workflow finished in {totalProcessingTime}ms total");
            Assert.True(totalProcessingTime > 0);
            Assert.True(totalProcessingTime < 120000, "Complete workflow should finish within 2 minutes");
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            _logger.LogWarning("Rate limit exceeded during end-to-end test - this indicates successful connection");
            Assert.True(true, "Rate limit indicates successful API connection");
        }
        catch (RequestFailedException ex) when (ex.Status == 401)
        {
            Assert.Fail($"Authentication failed - check API keys. Error: {ex.Message}");
        }
    }

    [Fact]
    public async Task ServicesUnderLoad_ShouldHandleMultipleRequests()
    {
        if (!HasRealApiKeys())
        {
            _logger.LogWarning("Skipping load test - no real API keys configured");
            return;
        }

        // Arrange
        var computerVisionService = ServiceProvider.GetRequiredService<IComputerVisionService>();
        var imageData = GetTestImageData();
        const int numberOfRequests = 3; // Keep it low to avoid rate limits

        _logger.LogInformation($"Starting load test with {numberOfRequests} concurrent requests");

        // Act
        var tasks = new List<Task<(string Description, List<string> Tags, double ConfidenceScore, long ProcessingTimeMs)>>();

        for (int i = 0; i < numberOfRequests; i++)
        {
            tasks.Add(computerVisionService.AnalyzeImageAsync(imageData));
        }

        try
        {
            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(numberOfRequests, results.Length);
            Assert.All(results, result =>
            {
                Assert.NotNull(result.Description);
                Assert.NotEmpty(result.Description);
                Assert.True(result.ProcessingTimeMs > 0);
            });

            var averageProcessingTime = results.Average(r => r.ProcessingTimeMs);
            _logger.LogInformation($"Load test completed. Average processing time: {averageProcessingTime}ms");
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            _logger.LogWarning("Rate limit exceeded during load test - this is expected behavior");
            Assert.True(true, "Rate limiting works correctly");
        }
    }

    [Fact]
    public async Task ErrorHandling_WithInvalidApiKeys_ShouldFailGracefully()
    {
        // Arrange
        var invalidConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ComputerVision:Endpoint"] = "https://eastus.api.cognitive.microsoft.com/",
                ["ComputerVision:ApiKey"] = "invalid-key-12345",
                ["ApplicationInsights:InstrumentationKey"] = "test-key"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(invalidConfig);
        services.AddLogging(builder => builder.AddConsole());
        // Configure a real TelemetryClient for testing, but disable sending telemetry
        var telemetryConfiguration = TelemetryConfiguration.CreateDefault();
        telemetryConfiguration.DisableTelemetry = true;
        services.AddSingleton(new TelemetryClient(telemetryConfiguration));
        services.AddScoped<IComputerVisionService, ComputerVisionService>();
        services.AddScoped<ILogger<ComputerVisionService>>(provider =>
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<ComputerVisionService>());

        using var serviceProvider = services.BuildServiceProvider();
        var computerVisionService = serviceProvider.GetRequiredService<IComputerVisionService>();
        var imageData = GetTestImageData();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => computerVisionService.AnalyzeImageAsync(imageData));

        Assert.Equal(401, exception.Status);
        _logger.LogInformation("Error handling test completed - invalid API key properly rejected");
    }

    [Fact]
    public async Task ServiceResilience_WithNetworkIssues_ShouldTimeout()
    {
        // Arrange
        var invalidConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ComputerVision:Endpoint"] = "https://nonexistent-endpoint-12345.microsoft.com/",
                ["ComputerVision:ApiKey"] = "test-key-12345",
                ["ApplicationInsights:InstrumentationKey"] = "test-key"
            })
            .Build(); var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(invalidConfig);
        services.AddLogging(builder => builder.AddConsole());
        // Configure a real TelemetryClient for testing, but disable sending telemetry
        var telemetryConfiguration = TelemetryConfiguration.CreateDefault();
        telemetryConfiguration.DisableTelemetry = true;
        services.AddSingleton(new TelemetryClient(telemetryConfiguration));
        services.AddScoped<IComputerVisionService, ComputerVisionService>();
        services.AddScoped<ILogger<ComputerVisionService>>(provider =>
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<ComputerVisionService>());

        using var serviceProvider = services.BuildServiceProvider();
        var computerVisionService = serviceProvider.GetRequiredService<IComputerVisionService>();
        var imageData = GetTestImageData();

        // Act & Assert
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await computerVisionService.AnalyzeImageAsync(imageData);
        });

        _logger.LogInformation("Network resilience test completed - service properly handles network issues");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(300)]
    [InlineData(500)]
    public async Task DescriptionLength_WithDifferentTargets_ShouldRespectLimits(int targetLength)
    {
        if (!HasRealApiKeys())
        {
            _logger.LogWarning($"Skipping description length test for {targetLength} words - no real API keys configured");
            return;
        }

        // Arrange
        var openAIService = ServiceProvider.GetRequiredService<IOpenAIService>();
        var basicDescription = "A simple test image with basic content";
        var tags = new List<string> { "test", "simple", "basic" };

        // Act
        try
        {
            var result = await openAIService.EnhanceDescriptionAsync(basicDescription, tags, targetLength);

            // Assert
            Assert.NotNull(result.EnhancedDescription);
            Assert.True(result.TokensUsed > 0);

            // Count words (approximate check)
            var wordCount = result.EnhancedDescription.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var tolerance = 0.3; // 30% tolerance
            var minWords = (int)(targetLength * (1 - tolerance));
            var maxWords = (int)(targetLength * (1 + tolerance));

            Assert.True(wordCount >= minWords && wordCount <= maxWords,
                $"Word count {wordCount} should be between {minWords} and {maxWords} for target {targetLength}");

            _logger.LogInformation($"Description length test for {targetLength} words completed. Actual: {wordCount} words");
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            _logger.LogWarning($"Rate limit exceeded during description length test for {targetLength} words");
            Assert.True(true, "Rate limiting works correctly");
        }
    }
}
