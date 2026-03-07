using System.Diagnostics;
using Azure.AI.OpenAI;
using Azure.Identity;
using OpenAI.Chat;
using OpenAI.Images;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PoRedoImage.Web.Features.ImageAnalysis;

/// <summary>
/// Interface for OpenAI service operations
/// </summary>
public interface IOpenAIService
{
    Task<(string EnhancedDescription, int TokensUsed, long ProcessingTimeMs)> EnhanceDescriptionAsync(
        string basicDescription, List<string> tags, int targetLength);

    Task<(byte[] ImageData, string ContentType, int TokensUsed, long ProcessingTimeMs)> GenerateImageAsync(
        string description);

    /// <summary>
    /// DALL-E 2 image-edit: composites the uploaded photo into the scene described by <paramref name="prompt"/>.
    /// Requires an <c>OpenAI:ImageEditDeployment</c> (e.g. <c>dall-e-2</c>) configured in appsettings.
    /// </summary>
    Task<(byte[] ImageData, string ContentType, long ProcessingTimeMs)> GenerateImageEditAsync(
        byte[] imageBytes, string prompt, CancellationToken cancellationToken = default);

    Task<(string TopText, string BottomText, int TokensUsed, long ProcessingTimeMs)> GenerateMemeCaptionAsync(
        List<string> tags);

    /// <summary>
    /// Uses GPT-4o vision to produce a concise physical description of the person in the image
    /// (e.g. "a bald white man in his 40s with glasses and a short beard") suitable for use
    /// as the &lt;PERSON&gt; token replacement in art-style prompts.
    /// </summary>
    Task<string> DescribePersonAsync(byte[] imageData);
}

/// <summary>
/// Implementation of OpenAI service using Azure OpenAI
/// </summary>
public class OpenAIService : IOpenAIService
{
    private readonly ILogger<OpenAIService> _logger;
    private readonly ChatClient _chatClient;
    private readonly ImageClient _imageClient;
    private readonly ImageClient? _imageEditClient;

    public OpenAIService(IConfiguration configuration, ILogger<OpenAIService> logger)
    {
        _logger = logger;

        var endpoint = configuration["OpenAI:Endpoint"] ??
            throw new ArgumentNullException("OpenAI:Endpoint is not configured");

        var imageEndpoint = configuration["OpenAI:ImageEndpoint"] ?? endpoint;
        var chatDeployment = configuration["OpenAI:ChatCompletionsDeployment"] ?? "gpt-4o";
        var imageDeployment = configuration["OpenAI:ImageGenerationDeployment"] ?? "dall-e-3";

        var apiKey = configuration["OpenAI:Key"];
        var imageApiKey = configuration["OpenAI:ImageKey"] ?? apiKey;

        // Re-use the same AzureOpenAIClient when chat and image endpoints are identical
        // to avoid duplicate HTTP connection pools and credential objects.
        if (string.Equals(endpoint, imageEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            var sharedClient = BuildClient(endpoint, apiKey);
            _chatClient = sharedClient.GetChatClient(chatDeployment);
            _imageClient = sharedClient.GetImageClient(imageDeployment);
        }
        else
        {
            _chatClient = BuildClient(endpoint, apiKey).GetChatClient(chatDeployment);
            _imageClient = BuildClient(imageEndpoint, imageApiKey).GetImageClient(imageDeployment);
        }

        var editDeployment = configuration["OpenAI:ImageEditDeployment"];
        if (!string.IsNullOrWhiteSpace(editDeployment))
            _imageEditClient = BuildClient(imageEndpoint, imageApiKey).GetImageClient(editDeployment);

        _logger.LogInformation("OpenAI Service initialized. Chat: {Chat}, Image: {Image}, Edit: {Edit}",
            chatDeployment, imageDeployment, editDeployment ?? "(not configured)");
    }

    private static AzureOpenAIClient BuildClient(string endpoint, string? apiKey) =>
        string.IsNullOrEmpty(apiKey)
            ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
            : new AzureOpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(apiKey));

    public async Task<(string EnhancedDescription, int TokensUsed, long ProcessingTimeMs)> EnhanceDescriptionAsync(
        string basicDescription, List<string> tags, int targetLength)
    {
        ArgumentNullException.ThrowIfNull(basicDescription);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(targetLength, 0);

        _logger.LogInformation("Enhancing description. Target: {TargetLength} words", targetLength);
        var startTimestamp = Stopwatch.GetTimestamp();

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
            var processingTime = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

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
        var startTimestamp = Stopwatch.GetTimestamp();

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
            var processingTime = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            _logger.LogInformation("Image generated in {ProcessingTime}ms. Size: {Size} bytes", processingTime, imageData.Length);
            return (imageData, "image/png", 0, processingTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating image");
            throw;
        }
    }

    public async Task<(byte[] ImageData, string ContentType, long ProcessingTimeMs)> GenerateImageEditAsync(
        byte[] imageBytes, string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        if (_imageEditClient is null)
            throw new InvalidOperationException(
                "DALL-E 2 image edit is not configured. Add OpenAI:ImageEditDeployment to appsettings.");

        _logger.LogInformation("Generating image edit with DALL-E 2. Prompt length: {Length}", prompt.Length);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            // DALL-E 2 edit requires: square PNG, RGBA, 1024×1024, ≤4 MB
            var pngBytes = await PrepareForImageEditAsync(imageBytes);

            var editOptions = new ImageEditOptions
            {
                Size = GeneratedImageSize.W1024xH1024,
                ResponseFormat = GeneratedImageFormat.Bytes
            };

            using var imageStream = new MemoryStream(pngBytes);
            var response = await _imageEditClient.GenerateImageEditAsync(
                imageStream, "source.png", prompt, editOptions, cancellationToken);

            var resultBytes = response.Value.ImageBytes.ToArray();
            var processingTime = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            _logger.LogInformation("Image edit completed in {ProcessingTime}ms. Output: {Size} bytes",
                processingTime, resultBytes.Length);

            return (resultBytes, "image/png", processingTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating image edit");
            throw;
        }
    }

    /// <summary>
    /// Converts any uploaded image to a square 1024×1024 RGBA PNG required by the DALL-E 2 edit API.
    /// Pads with transparent pixels so the model can choose how to fill borders.
    /// </summary>
    private static string DetectMimeType(byte[] bytes) =>
        bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8
            ? "image/jpeg"
            : "image/png";

    private static async Task<byte[]> PrepareForImageEditAsync(byte[] inputBytes)
    {
        using var img = Image.Load<Rgba32>(inputBytes);

        img.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new SixLabors.ImageSharp.Size(1024, 1024),
            Mode = ResizeMode.BoxPad,
            PadColor = SixLabors.ImageSharp.Color.Transparent   // transparent mask = let DALL-E fill
        }));

        using var ms = new MemoryStream();
        await img.SaveAsPngAsync(ms);
        return ms.ToArray();
    }

    public async Task<(string TopText, string BottomText, int TokensUsed, long ProcessingTimeMs)> GenerateMemeCaptionAsync(
        List<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        _logger.LogInformation("Generating meme caption from {TagCount} tags", tags.Count);
        var startTimestamp = Stopwatch.GetTimestamp();

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
            var processingTime = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

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

    public async Task<string> DescribePersonAsync(byte[] imageData)
    {
        ArgumentNullException.ThrowIfNull(imageData);

        _logger.LogInformation("Describing person via GPT-4o vision. Image size: {Size} bytes", imageData.Length);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var base64 = Convert.ToBase64String(imageData);
            var mimeType = DetectMimeType(imageData);
            var dataUrl = $"data:{mimeType};base64,{base64}";

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(
                    "You are a precise physical appearance descriptor. " +
                    "Your output is used directly inside an AI image generation prompt, " +
                    "so it must be a short, vivid noun phrase (no full sentences, no extra commentary). " +
                    "Focus exclusively on the primary person's visible physical traits: " +
                    "hair (or baldness), facial hair, age range, ethnicity, body build, and eyewear if present. " +
                    "Example outputs: " +
                    "\"a bald white man in his 40s with light stubble and glasses\", " +
                    "\"a young Black woman with long curly hair and a warm smile\", " +
                    "\"an elderly Asian man with a white beard and round glasses\"."),
                new UserChatMessage(
                    ChatMessageContentPart.CreateImagePart(new Uri(dataUrl)),
                    ChatMessageContentPart.CreateTextPart(
                        "Describe the main person in this photo as a short noun phrase for an art prompt."))
            };

            var response = await _chatClient.CompleteChatAsync(messages,
                new ChatCompletionOptions { MaxOutputTokenCount = 80 });

            var description = response.Value.Content[0].Text.Trim().TrimEnd('.');
            var processingTime = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            _logger.LogInformation("Person described in {ProcessingTime}ms: {Description}", processingTime, description);
            return description;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error describing person via vision");
            throw;
        }
    }
}
