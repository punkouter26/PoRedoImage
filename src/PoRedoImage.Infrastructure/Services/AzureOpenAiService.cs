using System.Diagnostics;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using OpenAI.Images;
using PoRedoImage.Domain.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Azure OpenAI implementation of IGenerativeAiService.
/// Adapter pattern (GoF): adapts Azure OpenAI SDK to the domain interface.
/// </summary>
public sealed class AzureOpenAiService : IGenerativeAiService
{
    private readonly ILogger<AzureOpenAiService> _logger;
    private readonly ChatClient _chatClient;
    private readonly ImageClient _imageClient;
    private readonly ImageClient? _imageEditClient;
    private readonly string? _configurationError;

    public AzureOpenAiService(IConfiguration configuration, ILogger<AzureOpenAiService> logger)
    {
        _logger = logger;

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
            var shared = BuildClient(endpoint, apiKey);
            _chatClient = shared.GetChatClient(chatDeployment);
            _imageClient = shared.GetImageClient(imageDeployment);
        }
        else
        {
            _chatClient = BuildClient(endpoint, apiKey).GetChatClient(chatDeployment);
            _imageClient = BuildClient(imageEndpoint, imageApiKey).GetImageClient(imageDeployment);
        }

        var editDeployment = configuration["OpenAI:ImageEditDeployment"];
        if (!string.IsNullOrWhiteSpace(editDeployment))
            _imageEditClient = BuildClient(imageEndpoint, imageApiKey).GetImageClient(editDeployment);

        _logger.LogInformation("AzureOpenAI Service initialized. Chat={Chat}, Image={Image}", chatDeployment, imageDeployment);
    }

    private static AzureOpenAIClient BuildClient(string endpoint, string? apiKey) =>
        string.IsNullOrEmpty(apiKey)
            ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
            : new AzureOpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(apiKey));

    public async Task<(string EnhancedDescription, int TokensUsed, long ElapsedMs)>
        EnhanceDescriptionAsync(string description, IReadOnlyList<string> tags, int targetLength, CancellationToken ct = default)
    {
        if (_configurationError is not null) throw new InvalidOperationException(_configurationError);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(targetLength, 0);

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

    public async Task<(byte[] ImageData, string ContentType, long ElapsedMs)>
        GenerateImageEditAsync(byte[] imageBytes, string prompt, CancellationToken ct = default)
    {
        if (_configurationError is not null) throw new InvalidOperationException(_configurationError);
        if (_imageEditClient is null)
            throw new InvalidOperationException("DALL-E 2 image edit is not configured. Add OpenAI:ImageEditDeployment to appsettings.");

        _logger.LogInformation("Generating image edit with DALL-E 2");
        var start = Stopwatch.GetTimestamp();

        var pngBytes = await PrepareForImageEditAsync(imageBytes);
        using var stream = new MemoryStream(pngBytes);
        var response = await _imageEditClient.GenerateImageEditAsync(
            stream, "source.png", prompt,
            new ImageEditOptions { Size = GeneratedImageSize.W1024xH1024, ResponseFormat = GeneratedImageFormat.Bytes }, ct);

        var resultBytes = response.Value.ImageBytes.ToArray();
        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.LogInformation("Image edit complete in {Elapsed}ms. Size={Size} bytes", elapsed, resultBytes.Length);
        return (resultBytes, "image/png", elapsed);
    }

    public async Task<(string TopText, string BottomText, int TokensUsed, long ElapsedMs)>
        GenerateMemeCaptionAsync(IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        if (_configurationError is not null) throw new InvalidOperationException(_configurationError);
        ArgumentNullException.ThrowIfNull(tags);

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

        var content = response.Value.Content[0].Text.Trim();
        var tokens = response.Value.Usage.TotalTokenCount;
        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        var cleaned = content.Contains("```")
            ? content[content.IndexOf('{')..(content.LastIndexOf('}') + 1)]
            : content;

        using var json = System.Text.Json.JsonDocument.Parse(cleaned);
        var top = json.RootElement.GetProperty("topText").GetString() ?? "";
        var bottom = json.RootElement.GetProperty("bottomText").GetString() ?? "";

        _logger.LogInformation("Meme caption generated in {Elapsed}ms", elapsed);
        return (top, bottom, tokens, elapsed);
    }

    public async Task<string> DescribePersonAsync(byte[] imageData, CancellationToken ct = default)
    {
        if (_configurationError is not null) throw new InvalidOperationException(_configurationError);

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

        var description = response.Value.Content[0].Text.Trim().TrimEnd('.');
        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.LogInformation("Person described in {Elapsed}ms: {Description}", elapsed, description);
        return description;
    }

    private static async Task<byte[]> PrepareForImageEditAsync(byte[] inputBytes)
    {
        using var img = Image.Load<Rgba32>(inputBytes);
        img.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new SixLabors.ImageSharp.Size(1024, 1024),
            Mode = ResizeMode.BoxPad,
            PadColor = SixLabors.ImageSharp.Color.Transparent
        }));
        using var ms = new MemoryStream();
        await img.SaveAsPngAsync(ms);
        return ms.ToArray();
    }
}
