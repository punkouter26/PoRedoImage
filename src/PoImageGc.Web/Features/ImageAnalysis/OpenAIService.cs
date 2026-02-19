using Azure.AI.OpenAI;
using Azure.Identity;
using OpenAI.Chat;
using OpenAI.Images;

namespace PoImageGc.Web.Features.ImageAnalysis;

/// <summary>
/// Interface for OpenAI service operations
/// </summary>
public interface IOpenAIService
{
    Task<(string EnhancedDescription, int TokensUsed, long ProcessingTimeMs)> EnhanceDescriptionAsync(
        string basicDescription, List<string> tags, int targetLength);

    Task<(byte[] ImageData, string ContentType, int TokensUsed, long ProcessingTimeMs)> GenerateImageAsync(
        string description);

    Task<(string TopText, string BottomText, int TokensUsed, long ProcessingTimeMs)> GenerateMemeCaptionAsync(
        List<string> tags);
}

/// <summary>
/// Implementation of OpenAI service using Azure OpenAI
/// </summary>
public class OpenAIService : IOpenAIService
{
    private readonly ILogger<OpenAIService> _logger;
    private readonly ChatClient _chatClient;
    private readonly ImageClient _imageClient;

    public OpenAIService(IConfiguration configuration, ILogger<OpenAIService> logger)
    {
        _logger = logger;

        var endpoint = configuration["OpenAI:Endpoint"] ??
            throw new ArgumentNullException("OpenAI:Endpoint is not configured");

        var imageEndpoint = configuration["OpenAI:ImageEndpoint"] ?? endpoint;
        var chatDeployment = configuration["OpenAI:ChatCompletionsDeployment"] ?? "gpt-4o";
        var imageDeployment = configuration["OpenAI:ImageGenerationDeployment"] ?? "dall-e-3";

        // Prefer Managed Identity (DefaultAzureCredential) when no API key is configured —
        // required for production ACA deployments using Workload Identity / Managed Identity.
        var apiKey = configuration["OpenAI:Key"];
        var imageApiKey = configuration["OpenAI:ImageKey"] ?? apiKey;

        // Re-use the same AzureOpenAIClient when chat and image endpoints are identical
        // to avoid duplicate HTTP connection pools and credential objects.
        if (string.Equals(endpoint, imageEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            var sharedClient = string.IsNullOrEmpty(apiKey)
                ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(apiKey));
            _chatClient = sharedClient.GetChatClient(chatDeployment);
            _imageClient = sharedClient.GetImageClient(imageDeployment);
        }
        else
        {
            _chatClient = (string.IsNullOrEmpty(apiKey)
                ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(apiKey)))
                .GetChatClient(chatDeployment);
            _imageClient = (string.IsNullOrEmpty(imageApiKey)
                ? new AzureOpenAIClient(new Uri(imageEndpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(imageEndpoint), new Azure.AzureKeyCredential(imageApiKey)))
                .GetImageClient(imageDeployment);
        }

        _logger.LogInformation("OpenAI Service initialized. Chat: {Chat}, Image: {Image}", chatDeployment, imageDeployment);
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

            var response = await _chatClient.CompleteChatAsync(messages, chatOptions);
            var enhancedDescription = response.Value.Content[0].Text.Trim();
            var tokensUsed = response.Value.Usage.TotalTokenCount;
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            _logger.LogInformation("Description enhanced in {ProcessingTime}ms. Tokens: {Tokens}", processingTime, tokensUsed);
            return (enhancedDescription, tokensUsed, processingTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enhancing description");
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
            var options = new ImageGenerationOptions
            {
                Quality = GeneratedImageQuality.Standard,
                Size = GeneratedImageSize.W1024xH1024,
                ResponseFormat = GeneratedImageFormat.Bytes
            };

            var response = await _imageClient.GenerateImageAsync(description, options);
            var imageData = response.Value.ImageBytes.ToArray();
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            _logger.LogInformation("Image generated in {ProcessingTime}ms. Size: {Size} bytes", processingTime, imageData.Length);
            return (imageData, "image/png", 0, processingTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating image");
            throw;
        }
    }

    public async Task<(string TopText, string BottomText, int TokensUsed, long ProcessingTimeMs)> GenerateMemeCaptionAsync(
        List<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        _logger.LogInformation("Generating meme caption from {TagCount} tags", tags.Count);
        var startTime = DateTime.UtcNow;

        try
        {
            var prompt = $@"Create a funny meme caption for an image with these elements: {string.Join(", ", tags)}

Respond in JSON format:
{{""topText"": ""TOP CAPTION"", ""bottomText"": ""BOTTOM CAPTION""}}

Keep captions short (3-7 words each). Make it humorous and relatable.";

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a meme caption generator. Create funny, relatable captions."),
                new UserChatMessage(prompt)
            };

            var response = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions { MaxOutputTokenCount = 150 });
            var content = response.Value.Content[0].Text.Trim();
            var tokensUsed = response.Value.Usage.TotalTokenCount;
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            // Strip markdown code fences that GPT-4o may wrap around JSON (e.g. ```json ... ```)
            var cleaned = content;
            if (cleaned.Contains("```"))
            {
                var start = cleaned.IndexOf('{');
                var end = cleaned.LastIndexOf('}');
                if (start >= 0 && end > start)
                    cleaned = cleaned[start..(end + 1)];
            }

            // Parse JSON response
            var json = System.Text.Json.JsonDocument.Parse(cleaned);
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
