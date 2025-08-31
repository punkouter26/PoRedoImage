using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Services;
using Azure.AI.OpenAI;
using Azure;

namespace ImageGc.Tests.Services;

/// <summary>
/// Integration tests for OpenAI Service API connections
/// </summary>
public class OpenAIServiceTests : TestBase
{
    private readonly IOpenAIService _openAIService;

    public OpenAIServiceTests()
    {
        _openAIService = ServiceProvider.GetRequiredService<IOpenAIService>();
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        services.AddScoped<IOpenAIService, OpenAIService>();
        services.AddScoped<ILogger<OpenAIService>>(provider =>
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<OpenAIService>());
    }

    [Fact]
    public async Task EnhanceDescriptionAsync_WithValidInput_ShouldReturnEnhancedDescription()
    {
        if (!HasRealApiKeys())
        {
            // Skip test if no real API keys are configured
            return;
        }

        // Arrange
        var basicDescription = "A simple test image";
        var tags = new List<string> { "test", "image", "simple" };
        const int targetLength = 200;

        // Act
        var result = await _openAIService.EnhanceDescriptionAsync(basicDescription, tags, targetLength);

        // Assert
        Assert.NotNull(result.EnhancedDescription);
        Assert.NotEmpty(result.EnhancedDescription);
        Assert.True(result.TokensUsed > 0);
        Assert.True(result.ProcessingTimeMs > 0);
    }

    [Fact]
    public async Task GenerateImageAsync_WithValidDescription_ShouldReturnImageData()
    {
        if (!HasRealApiKeys())
        {
            // Skip test if no real API keys are configured
            return;
        }

        // Arrange
        var description = "A simple red circle on a white background";

        // Act
        var result = await _openAIService.GenerateImageAsync(description);

        // Assert
        Assert.NotNull(result.ImageData);
        Assert.True(result.ImageData.Length > 0);
        Assert.NotNull(result.ContentType);
        Assert.True(result.TokensUsed > 0);
        Assert.True(result.ProcessingTimeMs > 0);
    }

    [Fact]
    public async Task EnhanceDescriptionAsync_WithNullDescription_ShouldThrowException()
    {
        // Arrange
        string? description = null;
        var tags = new List<string> { "test" };        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => _openAIService.EnhanceDescriptionAsync(description!, tags, 200));
    }

    [Fact]
    public async Task GenerateImageAsync_WithNullDescription_ShouldThrowException()
    {
        // Arrange
        string? description = null;        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => _openAIService.GenerateImageAsync(description!));
    }

    [Fact]
    public void OpenAIService_Configuration_ShouldBeValid()
    {
        // Arrange & Act
        var endpoint = Configuration["OpenAI:Endpoint"];
        var apiKey = Configuration["OpenAI:ApiKey"];
        var chatModel = Configuration["OpenAI:ChatModel"];
        var imageModel = Configuration["OpenAI:ImageModel"];

        // Assert
        Assert.NotNull(endpoint);
        Assert.NotEmpty(endpoint);
        Assert.True(Uri.IsWellFormedUriString(endpoint, UriKind.Absolute));

        Assert.NotNull(apiKey);
        Assert.NotEmpty(apiKey);

        Assert.NotNull(chatModel);
        Assert.NotEmpty(chatModel);

        Assert.NotNull(imageModel);
        Assert.NotEmpty(imageModel);
    }

    /// <summary>
    /// Test connection to OpenAI API endpoint
    /// </summary>
    [Fact]
    public async Task OpenAIService_ConnectionTest_ShouldConnect()
    {
        if (!HasRealApiKeys())
        {
            // Skip connection test if using placeholder keys
            return;
        }

        // Arrange
        var endpoint = Configuration["OpenAI:Endpoint"];
        var apiKey = Configuration["OpenAI:ApiKey"];

        // Act & Assert
        try
        {
            var client = new AzureOpenAIClient(new Uri(endpoint!), new AzureKeyCredential(apiKey!));

            // Simple connection test - try to get chat completions client
            var chatClient = client.GetChatClient(Configuration["OpenAI:ChatModel"]!);
            Assert.NotNull(chatClient);
            // Test with a simple completion request
            var chatMessages = new OpenAI.Chat.ChatMessage[]
            {
                OpenAI.Chat.ChatMessage.CreateSystemMessage("You are a helpful assistant."),
                OpenAI.Chat.ChatMessage.CreateUserMessage("Say 'test' if you can hear me.")
            };

            var result = await chatClient.CompleteChatAsync(chatMessages);
            Assert.NotNull(result);
            Assert.NotNull(result.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 401)
        {
            Assert.Fail($"Authentication failed. Check OpenAI API key. Error: {ex.Message}");
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            // Rate limit exceeded - this actually indicates successful connection
            Assert.True(true, "Connection successful but rate limited");
        }
        catch (Exception ex)
        {
            Assert.Fail($"Unexpected error connecting to OpenAI API: {ex.Message}");
        }
    }

    [Fact]
    public async Task OpenAIService_ChatCompletionPerformanceTest_ShouldCompleteWithinTimeout()
    {
        if (!HasRealApiKeys())
        {
            return; // Skip if no real API keys
        }

        // Arrange
        var basicDescription = "A test image";
        var tags = new List<string> { "test" };
        var timeout = TimeSpan.FromSeconds(60); // OpenAI can be slower than Computer Vision

        // Act
        var startTime = DateTime.UtcNow;

        try
        {
            using var cts = new CancellationTokenSource(timeout);
            var result = await _openAIService.EnhanceDescriptionAsync(basicDescription, tags, 100);
            var endTime = DateTime.UtcNow;

            // Assert
            Assert.True(endTime - startTime < timeout, "Operation should complete within timeout");
            Assert.True(result.ProcessingTimeMs > 0, "Processing time should be recorded");
        }
        catch (OperationCanceledException)
        {
            Assert.Fail($"OpenAI Chat Completion API call timed out after {timeout.TotalSeconds} seconds");
        }
    }

    [Fact]
    public async Task OpenAIService_ImageGenerationPerformanceTest_ShouldCompleteWithinTimeout()
    {
        if (!HasRealApiKeys())
        {
            return; // Skip if no real API keys
        }

        // Arrange
        var description = "A simple geometric shape";
        var timeout = TimeSpan.FromMinutes(2); // Image generation can take longer

        // Act
        var startTime = DateTime.UtcNow;

        try
        {
            using var cts = new CancellationTokenSource(timeout);
            var result = await _openAIService.GenerateImageAsync(description);
            var endTime = DateTime.UtcNow;

            // Assert
            Assert.True(endTime - startTime < timeout, "Operation should complete within timeout");
            Assert.True(result.ProcessingTimeMs > 0, "Processing time should be recorded");
        }
        catch (OperationCanceledException)
        {
            Assert.Fail($"OpenAI Image Generation API call timed out after {timeout.TotalSeconds} seconds");
        }
    }

    [Theory]
    [InlineData(100)]
    [InlineData(250)]
    [InlineData(500)]
    public async Task EnhanceDescriptionAsync_WithDifferentTargetLengths_ShouldRespectLength(int targetLength)
    {
        if (!HasRealApiKeys())
        {
            return; // Skip if no real API keys
        }

        // Arrange
        var basicDescription = "A colorful landscape";
        var tags = new List<string> { "landscape", "colorful", "nature" };

        // Act
        var result = await _openAIService.EnhanceDescriptionAsync(basicDescription, tags, targetLength);

        // Assert
        Assert.NotNull(result.EnhancedDescription);

        // Count words in the result (approximate check)
        var wordCount = result.EnhancedDescription.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        // Allow some variance in word count (±30%)
        var minWords = (int)(targetLength * 0.7);
        var maxWords = (int)(targetLength * 1.3);

        Assert.True(wordCount >= minWords && wordCount <= maxWords,
            $"Word count {wordCount} should be between {minWords} and {maxWords} for target {targetLength}");
    }
}
