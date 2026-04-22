using System.Diagnostics;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using OpenAI.Images;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Azure OpenAI implementation of IGenerativeAiService.
/// Adapter pattern (GoF): adapts Azure OpenAI SDK to the domain interface.
/// </summary>
public sealed class AzureOpenAiService : IGenerativeAiService
{
    private readonly ILogger<AzureOpenAiService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ChatClient _chatClient;
    private readonly ImageClient _imageClient;
    private readonly Azure.AzureKeyCredential? _chatKeyCredential;
    private readonly Azure.AzureKeyCredential? _imageKeyCredential;
    private readonly string? _configurationError;

    public AzureOpenAiService(IConfiguration configuration, ILogger<AzureOpenAiService> logger)
    {
        _logger = logger;
        _configuration = configuration;

        var endpoint = configuration["OpenAI:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _configurationError = "OpenAI:Endpoint is not configured.";
            _logger.LogWarning("AzureOpenAI Service not configured: {Error}", _configurationError);
            _chatClient = null!;
            _imageClient = null!;
            return;
        }

        var imageEndpoint = configuration["OpenAI:ImageEndpoint"] ?? endpoint;
        var chatDeployment = configuration["OpenAI:ChatCompletionsDeployment"] ?? "gpt-4o";
        var imageDeployment = configuration["OpenAI:ImageGenerationDeployment"] ?? "dall-e-3";
        var apiKey = configuration["OpenAI:Key"];
        var imageApiKey = configuration["OpenAI:ImageKey"] ?? apiKey;

        if (string.Equals(endpoint, imageEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            var (shared, cred) = BuildClientWithCredential(endpoint, apiKey);
            _chatKeyCredential = cred;
            _imageKeyCredential = cred;
            _chatClient = shared.GetChatClient(chatDeployment);
            _imageClient = shared.GetImageClient(imageDeployment);
        }
        else
        {
            var (chatClientObj, chatCred) = BuildClientWithCredential(endpoint, apiKey);
            var (imgClientObj, imgCred) = BuildClientWithCredential(imageEndpoint, imageApiKey);
            _chatKeyCredential = chatCred;
            _imageKeyCredential = imgCred;
            _chatClient = chatClientObj.GetChatClient(chatDeployment);
            _imageClient = imgClientObj.GetImageClient(imageDeployment);
        }

        _logger.LogInformation("AzureOpenAI Service initialized. Chat={Chat}, Image={Image}", chatDeployment, imageDeployment);
    }

    private static (AzureOpenAIClient Client, Azure.AzureKeyCredential? Credential) BuildClientWithCredential(string endpoint, string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return (new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential()), null);
        var cred = new Azure.AzureKeyCredential(apiKey);
        return (new AzureOpenAIClient(new Uri(endpoint), cred), cred);
    }

    private void RefreshChatCredential()
    {
        var key = _configuration["OpenAI:Key"];
        if (!string.IsNullOrWhiteSpace(key)) _chatKeyCredential?.Update(key);
    }

    private void RefreshImageCredential()
    {
        var key = _configuration["OpenAI:ImageKey"] ?? _configuration["OpenAI:Key"];
        if (!string.IsNullOrWhiteSpace(key)) _imageKeyCredential?.Update(key);
    }

    public async Task<(string EnhancedDescription, int TokensUsed, long ElapsedMs)>
        EnhanceDescriptionAsync(string description, IReadOnlyList<string> tags, int targetLength, CancellationToken ct = default)
    {
        if (_configurationError is not null) throw new InvalidOperationException(_configurationError);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(targetLength, 0);

        RefreshChatCredential();
        _logger.LogInformation("Enhancing description. TargetLength={Length}", targetLength);
        var start = Stopwatch.GetTimestamp();

        var prompt = $"""
            I have an image with the following basic description:
            "{description}"

            The image has been tagged with these elements: {string.Join(", ", tags)}

            Please enhance this description to be more detailed and comprehensive.
            The enhanced description should be approximately {targetLength} words and suitable for image generation with DALL-E.

            Enhanced description:
            """;

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are an expert image description enhancer."),
            new UserChatMessage(prompt)
        };

        var response = await _chatClient.CompleteChatAsync(messages,
            new ChatCompletionOptions { MaxOutputTokenCount = 800, Temperature = 0.7f }, ct);

        if (response.Value.Content.Count == 0)
            throw new InvalidOperationException("OpenAI returned an empty response for description enhancement.");
        var enhanced = response.Value.Content[0].Text.Trim();
        var tokens = response.Value.Usage.TotalTokenCount;
        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.LogInformation("Description enhanced in {Elapsed}ms. Tokens={Tokens}", elapsed, tokens);
        return (enhanced, tokens, elapsed);
    }

    public async Task<(byte[] ImageData, string ContentType, long ElapsedMs)>
        GenerateImageAsync(string description, CancellationToken ct = default)
    {
        if (_configurationError is not null) throw new InvalidOperationException(_configurationError);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        RefreshImageCredential();
        _logger.LogInformation("Generating image with DALL-E");
        var start = Stopwatch.GetTimestamp();

        var options = new ImageGenerationOptions
        {
            Quality = GeneratedImageQuality.Standard,
            Size = GeneratedImageSize.W1024xH1024,
            ResponseFormat = GeneratedImageFormat.Bytes
        };

        var response = await _imageClient.GenerateImageAsync(description, options, ct);
        var imageData = response.Value.ImageBytes.ToArray();
        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.LogInformation("Image generated in {Elapsed}ms. Size={Size} bytes", elapsed, imageData.Length);
        return (imageData, "image/png", elapsed);
    }

    public async Task<(string TopText, string BottomText, int TokensUsed, long ElapsedMs)>
        GenerateMemeCaptionAsync(IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        if (_configurationError is not null) throw new InvalidOperationException(_configurationError);
        ArgumentNullException.ThrowIfNull(tags);

        RefreshChatCredential();
        _logger.LogInformation("Generating meme caption from {Count} tags", tags.Count);
        var start = Stopwatch.GetTimestamp();

        var prompt = $$"""
            Create a funny meme caption for an image with these elements: {{string.Join(", ", tags)}}

            Respond in JSON format:
            {"topText": "TOP CAPTION", "bottomText": "BOTTOM CAPTION"}

            Keep captions short (3-7 words each). Make it humorous and relatable.
            """;

        var response = await _chatClient.CompleteChatAsync(
            [new SystemChatMessage("You are a meme caption generator."), new UserChatMessage(prompt)],
            new ChatCompletionOptions { MaxOutputTokenCount = 150 }, ct);

        if (response.Value.Content.Count == 0)
            throw new InvalidOperationException("OpenAI returned an empty response for meme caption.");
        var content = response.Value.Content[0].Text.Trim();
        var tokens = response.Value.Usage.TotalTokenCount;
        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        string cleaned;
        if (content.Contains("```"))
        {
            var start2 = content.IndexOf('{');
            var end2 = content.LastIndexOf('}');
            cleaned = start2 >= 0 && end2 > start2
                ? content[start2..(end2 + 1)]
                : content;
        }
        else
        {
            cleaned = content;
        }

        using var json = System.Text.Json.JsonDocument.Parse(cleaned);
        var top = json.RootElement.GetProperty("topText").GetString() ?? "";
        var bottom = json.RootElement.GetProperty("bottomText").GetString() ?? "";

        _logger.LogInformation("Meme caption generated in {Elapsed}ms", elapsed);
        return (top, bottom, tokens, elapsed);
    }

    public async Task<string> DescribePersonAsync(byte[] imageData, CancellationToken ct = default)
    {
        if (_configurationError is not null) throw new InvalidOperationException(_configurationError);

        RefreshChatCredential();
        _logger.LogInformation("Describing person via GPT-4o vision. Size={Size} bytes", imageData.Length);
        var start = Stopwatch.GetTimestamp();

        var base64 = Convert.ToBase64String(imageData);
        var mimeType = imageData.Length >= 2 && imageData[0] == 0xFF && imageData[1] == 0xD8 ? "image/jpeg" : "image/png";
        var dataUrl = $"data:{mimeType};base64,{base64}";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                "You are a precise physical appearance descriptor. " +
                "Output a short noun phrase describing the primary person's visible physical traits."),
            new UserChatMessage(
                ChatMessageContentPart.CreateImagePart(new Uri(dataUrl)),
                ChatMessageContentPart.CreateTextPart("Describe the main person in this photo as a short noun phrase for an art prompt."))
        };

        var response = await _chatClient.CompleteChatAsync(messages,
            new ChatCompletionOptions { MaxOutputTokenCount = 80 }, ct);

        if (response.Value.Content.Count == 0)
            throw new InvalidOperationException("OpenAI returned an empty response for person description.");
        var description = response.Value.Content[0].Text.Trim().TrimEnd('.');
        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.LogInformation("Person described in {Elapsed}ms: {Description}", elapsed, description);
        return description;
    }
}
