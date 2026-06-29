using System.Diagnostics;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
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
    private readonly Azure.AzureKeyCredential? _chatKeyCredential;
    private readonly string? _configurationError;

    public AzureOpenAiService(IConfiguration configuration, ILogger<AzureOpenAiService> logger)
    {
        _logger = logger;
        _configuration = configuration;

        // Defense-in-depth budget guardrail. When Mocks:UseMockAi=true, the DI container should
        // swap this service for MockGenerativeAiService and never construct this class at all.
        // The MockAiDelegatingHandler only covers HttpClient-based clients (Gemini/Ollama); this
        // service uses the Azure.AI.OpenAI SDK, which is NOT routed through HttpClient — so a
        // future regression that wires a real client while mock mode is on would bypass the
        // handler and silently spend a live token. Fail loud here instead.
        if (configuration.GetValue<bool>("Mocks:UseMockAi"))
        {
            throw new InvalidOperationException(
                "AzureOpenAiService was constructed while Mocks:UseMockAi=true. The DI container "
                + "should have resolved MockGenerativeAiService instead — check "
                + "AddPoRedoImageInfrastructure(useMockAi: true) and the service registrations. "
                + "Blocking construction to guarantee zero live token spend in test/dev paths.");
        }

        var endpoint = configuration["OpenAI:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _configurationError = "OpenAI:Endpoint is not configured.";
            _logger.LogWarning("AzureOpenAI Service not configured: {Error}", _configurationError);
            _chatClient = null!;
            return;
        }

        // Chat/text only — image generation is handled exclusively by Gemini (IImageGenerationService).
        var chatDeployment = configuration["OpenAI:ChatCompletionsDeployment"] ?? "gpt-4o";
        var apiKey = configuration["OpenAI:Key"];

        var (client, cred) = BuildClientWithCredential(endpoint, apiKey);
        _chatKeyCredential = cred;
        _chatClient = client.GetChatClient(chatDeployment);

        _logger.LogInformation("AzureOpenAI Service initialized. Chat={Chat}", chatDeployment);
    }

    private static (AzureOpenAIClient Client, Azure.AzureKeyCredential? Credential) BuildClientWithCredential(string endpoint, string? apiKey)
    {
        // Explicit resilience (§3): the SDK pipeline retries transient failures (429/5xx/timeouts)
        // with exponential backoff before surfacing an error to the caller.
        var options = new AzureOpenAIClientOptions
        {
            NetworkTimeout = TimeSpan.FromMinutes(3),
            RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(maxRetries: 3),
        };
        if (string.IsNullOrEmpty(apiKey))
            return (new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential(), options), null);
        var cred = new Azure.AzureKeyCredential(apiKey);
        return (new AzureOpenAIClient(new Uri(endpoint), cred, options), cred);
    }

    private void RefreshCredentials()
    {
        var chatKey = _configuration["OpenAI:Key"];
        if (!string.IsNullOrWhiteSpace(chatKey)) _chatKeyCredential?.Update(chatKey);
    }

    public async Task<(string EnhancedDescription, int TokensUsed, long ElapsedMs)>
        EnhanceDescriptionAsync(string description, IReadOnlyList<string> tags, int targetLength, CancellationToken ct = default)
    {
        if (_configurationError is not null) throw new InvalidOperationException(_configurationError);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(targetLength, 0);

        RefreshCredentials();
        _logger.LogInformation("Enhancing description. TargetLength={Length}", targetLength);
        var start = Stopwatch.GetTimestamp();

        var prompt = $"""
            I have an image with the following basic description:
            "{description}"

            The image has been tagged with these elements: {string.Join(", ", tags)}

            Please enhance this description to be more detailed and comprehensive.
            The enhanced description should be approximately {targetLength} words and suitable for image generation.

            Enhanced description:
            """;

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are an expert image description enhancer."),
            new UserChatMessage(prompt)
        };

        // GPT-5-class deployments (e.g. gpt-5.4-nano) reject custom Temperature and reject the legacy
        // max_tokens that Azure.AI.OpenAI 2.1.0 emits for MaxOutputTokenCount (they require
        // max_completion_tokens). Sending neither keeps these short prompts compatible across models.
        var response = await _chatClient.CompleteChatAsync(messages, cancellationToken: ct);

        if (response.Value.Content.Count == 0)
            throw new InvalidOperationException("OpenAI returned an empty response for description enhancement.");
        var enhanced = response.Value.Content[0].Text.Trim();
        var tokens = response.Value.Usage.TotalTokenCount;
        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.LogInformation("Description enhanced in {Elapsed}ms. Tokens={Tokens}", elapsed, tokens);
        return (enhanced, tokens, elapsed);
    }

    public async Task<(string TopText, string BottomText, int TokensUsed, long ElapsedMs)>
        GenerateMemeCaptionAsync(IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        if (_configurationError is not null) throw new InvalidOperationException(_configurationError);
        ArgumentNullException.ThrowIfNull(tags);

        RefreshCredentials();
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
            cancellationToken: ct);

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

        RefreshCredentials();
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

        var response = await _chatClient.CompleteChatAsync(messages, cancellationToken: ct);

        if (response.Value.Content.Count == 0)
            throw new InvalidOperationException("OpenAI returned an empty response for person description.");
        var description = response.Value.Content[0].Text.Trim().TrimEnd('.');
        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.LogInformation("Person described in {Elapsed}ms: {Description}", elapsed, description);
        return description;
    }

}
