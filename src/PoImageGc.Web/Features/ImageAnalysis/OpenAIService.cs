using Azure.AI.OpenAI;
using OpenAI.Chat;
using OpenAI.Images;
using Microsoft.ApplicationInsights;

namespace PoImageGc.Web.Features.ImageAnalysis;

/// <summary>
/// Interface for OpenAI service operations
/// </summary>
public interface IOpenAIService
{
    Task<(string EnhancedDescription, int TokensUsed, long ProcessingTimeMs)> EnhanceDescriptionAsync(
        string basicDescription, List<string> tags, int targetLength);

    Task<(string DetailedDescription, int TokensUsed, long ProcessingTimeMs)> GenerateDetailedDescriptionAsync(
        List<string> tags, int targetLength, double confidenceScore = 0);

    Task<(byte[] ImageData, string ContentType, int TokensUsed, long ProcessingTimeMs)> GenerateImageAsync(
        string description);

    Task<(string TopText, string BottomText, int TokensUsed, long ProcessingTimeMs)> GenerateMemeCaptionAsync(
        List<string> tags, double confidenceScore);
}

/// <summary>
/// Implementation of OpenAI service using Azure OpenAI
/// </summary>
public class OpenAIService : IOpenAIService
{
    private readonly ILogger<OpenAIService> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _imageEndpoint;
    private readonly string _imageApiKey;
    private readonly string _chatDeployment;
    private readonly string _imageDeployment;

    public OpenAIService(
        IConfiguration configuration,
        ILogger<OpenAIService> logger,
        TelemetryClient telemetryClient)
    {
        _logger = logger;
        _telemetryClient = telemetryClient;

        _endpoint = configuration["OpenAI:Endpoint"] ??
            throw new ArgumentNullException("OpenAI:Endpoint is not configured");
        _apiKey = configuration["OpenAI:Key"] ??
            throw new ArgumentNullException("OpenAI:Key is not configured");
        
        _imageEndpoint = configuration["OpenAI:ImageEndpoint"] ?? _endpoint;
        _imageApiKey = configuration["OpenAI:ImageKey"] ?? _apiKey;
        
        _chatDeployment = configuration["OpenAI:ChatCompletionsDeployment"] ?? "gpt-4o";
        _imageDeployment = configuration["OpenAI:ImageGenerationDeployment"] ?? "dall-e-3";

        _logger.LogInformation("OpenAI Service initialized. Chat: {Chat}, Image: {Image}", _chatDeployment, _imageDeployment);
    }

    public async Task<(string EnhancedDescription, int TokensUsed, long ProcessingTimeMs)> EnhanceDescriptionAsync(
        string basicDescription, List<string> tags, int targetLength)
    {
        ArgumentNullException.ThrowIfNull(basicDescription);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(targetLength, 0);

        _logger.LogInformation("Enhancing description. Target: {TargetLength} words", targetLength);
        var startTime = DateTime.UtcNow;

        try
        {
            var client = new AzureOpenAIClient(new Uri(_endpoint), new Azure.AzureKeyCredential(_apiKey));
            var chatClient = client.GetChatClient(_chatDeployment);

            var prompt = $@"I have an image with the following basic description:
""{basicDescription}""

The image has been tagged with these elements: {string.Join(", ", tags)}

Please enhance this description to be more detailed and comprehensive.
The enhanced description should be approximately {targetLength} words and suitable for image generation with DALL-E.

Enhanced description:";

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are an expert image description enhancer."),
                new UserChatMessage(prompt)
            };

            var chatOptions = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 800,
                Temperature = 0.7f
            };

            var response = await chatClient.CompleteChatAsync(messages, chatOptions);
            var enhancedDescription = response.Value.Content[0].Text.Trim();
            var tokensUsed = response.Value.Usage.TotalTokenCount;
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            _logger.LogInformation("Description enhanced in {ProcessingTime}ms. Tokens: {Tokens}", processingTime, tokensUsed);
            _telemetryClient.TrackMetric("OpenAIEnhancementTime", processingTime);

            return (enhancedDescription, tokensUsed, processingTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enhancing description");
            _telemetryClient.TrackException(ex);
            throw;
        }
    }

    public async Task<(string DetailedDescription, int TokensUsed, long ProcessingTimeMs)> GenerateDetailedDescriptionAsync(
        List<string> tags, int targetLength, double confidenceScore = 0)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(targetLength, 0);

        _logger.LogInformation("Generating detailed description from {TagCount} tags", tags.Count);
        var startTime = DateTime.UtcNow;

        try
        {
            var client = new AzureOpenAIClient(new Uri(_endpoint), new Azure.AzureKeyCredential(_apiKey));
            var chatClient = client.GetChatClient(_chatDeployment);

            var prompt = $@"Based on these image tags: {string.Join(", ", tags)}
Analysis confidence: {confidenceScore:P0}

Create a detailed visual description of approximately {targetLength} words suitable for image generation.
Focus on concrete visual elements and composition.

Detailed description:";

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are an expert at creating detailed image descriptions from tags."),
                new UserChatMessage(prompt)
            };

            var response = await chatClient.CompleteChatAsync(messages, new ChatCompletionOptions { MaxOutputTokenCount = 800 });
            var description = response.Value.Content[0].Text.Trim();
            var tokensUsed = response.Value.Usage.TotalTokenCount;
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            _logger.LogInformation("Description generated in {ProcessingTime}ms", processingTime);
            return (description, tokensUsed, processingTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating description");
            throw;
        }
    }

    public async Task<(byte[] ImageData, string ContentType, int TokensUsed, long ProcessingTimeMs)> GenerateImageAsync(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        _logger.LogInformation("Generating image with DALL-E");
        var startTime = DateTime.UtcNow;

        try
        {
            var client = new AzureOpenAIClient(new Uri(_imageEndpoint), new Azure.AzureKeyCredential(_imageApiKey));
            var imageClient = client.GetImageClient(_imageDeployment);

            var options = new ImageGenerationOptions
            {
                Quality = GeneratedImageQuality.Standard,
                Size = GeneratedImageSize.W1024xH1024,
                ResponseFormat = GeneratedImageFormat.Bytes
            };

            var response = await imageClient.GenerateImageAsync(description, options);
            var imageData = response.Value.ImageBytes.ToArray();
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            _logger.LogInformation("Image generated in {ProcessingTime}ms. Size: {Size} bytes", processingTime, imageData.Length);
            _telemetryClient.TrackMetric("DALLEGenerationTime", processingTime);

            return (imageData, "image/png", 0, processingTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating image");
            _telemetryClient.TrackException(ex);
            throw;
        }
    }

    public async Task<(string TopText, string BottomText, int TokensUsed, long ProcessingTimeMs)> GenerateMemeCaptionAsync(
        List<string> tags, double confidenceScore)
    {
        ArgumentNullException.ThrowIfNull(tags);

        _logger.LogInformation("Generating meme caption from {TagCount} tags", tags.Count);
        var startTime = DateTime.UtcNow;

        try
        {
            var client = new AzureOpenAIClient(new Uri(_endpoint), new Azure.AzureKeyCredential(_apiKey));
            var chatClient = client.GetChatClient(_chatDeployment);

            var prompt = $@"Create a funny meme caption for an image with these elements: {string.Join(", ", tags)}

Respond in JSON format:
{{""topText"": ""TOP CAPTION"", ""bottomText"": ""BOTTOM CAPTION""}}

Keep captions short (3-7 words each). Make it humorous and relatable.";

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a meme caption generator. Create funny, relatable captions."),
                new UserChatMessage(prompt)
            };

            var response = await chatClient.CompleteChatAsync(messages, new ChatCompletionOptions { MaxOutputTokenCount = 150 });
            var content = response.Value.Content[0].Text.Trim();
            var tokensUsed = response.Value.Usage.TotalTokenCount;
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            // Parse JSON response
            var json = System.Text.Json.JsonDocument.Parse(content);
            var topText = json.RootElement.GetProperty("topText").GetString() ?? "";
            var bottomText = json.RootElement.GetProperty("bottomText").GetString() ?? "";

            _logger.LogInformation("Meme caption generated in {ProcessingTime}ms", processingTime);
            return (topText, bottomText, tokensUsed, processingTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating meme caption");
            throw;
        }
    }
}
