using System.Diagnostics;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using PoRedoImage.Application.Configuration;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Azure OpenAI implementation of <see cref="IChatCompletionService"/> — the reasoning backend for
/// the Style Director agents, the Rap Roast scene describer, and the roast lyric writer.
/// </summary>
/// <remarks>
/// Adapter pattern (GoF) over the same <c>Azure.AI.OpenAI</c> chat surface
/// <see cref="AzureOpenAiService"/> already uses for the task-specific calls. Kept as a separate
/// class rather than another method on that service because the two interfaces mean different
/// things: <see cref="IGenerativeAiService"/> is a fixed menu of tasks, this is a free-form
/// reasoning primitive.
///
/// One deployment serves both paths. <c>OpenAI:ChatCompletionsDeployment</c> (gpt-5.4-nano and
/// every other GPT-4o/5-class deployment) accepts image content parts, so this one class covers both
/// free-form reasoning and image-to-text — there is no second model id to configure or keep in sync.
/// Since 2026-08 it is the only <see cref="IChatCompletionService"/> implementation outside mock mode.
/// </remarks>
public sealed class AzureOpenAiChatCompletionService : IChatCompletionService
{
    private readonly ILogger<AzureOpenAiChatCompletionService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ChatClient? _chatClient;
    private readonly Azure.AzureKeyCredential? _keyCredential;

    /// <summary>
    /// Mirrors the sibling services: an endpoint is the minimum to attempt a call. Credentials may
    /// arrive via <c>OpenAI:Key</c> or ambient managed identity, so key-presence is deliberately not
    /// part of this test.
    /// </summary>
    public bool IsConfigured => _chatClient is not null;

    public AzureOpenAiChatCompletionService(
        IConfiguration configuration, ILogger<AzureOpenAiChatCompletionService> logger)
    {
        _logger = logger;
        _configuration = configuration;

        // Defense-in-depth budget guardrail, matching AzureOpenAiService: the Azure.AI.OpenAI SDK
        // does not route through HttpClient, so MockAiDelegatingHandler cannot intercept it. A
        // regression that wired this up under mock mode would silently spend live tokens.
        if (ConfigValue.Bool(configuration, ConfigKeys.MocksUseMockAi))
        {
            throw new InvalidOperationException(
                "AzureOpenAiChatCompletionService was constructed while Mocks:UseMockAi=true. The DI "
                + "container should have resolved MockChatCompletionService instead — check "
                + "AddPoRedoImageInfrastructure(useMockAi: true). Blocking construction to guarantee "
                + "zero live token spend in test/dev paths.");
        }

        var endpoint = configuration[ConfigKeys.OpenAiEndpoint];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            // Not an error: IsConfigured=false is the contract that tells callers to use their own
            // deterministic fallback rather than calling CompleteAsync.
            _logger.LogInformation("OpenAI:Endpoint not configured; Azure OpenAI chat completions are disabled.");
            return;
        }

        var deployment = configuration[ConfigKeys.OpenAiChatCompletionsDeployment] ?? "gpt-4o";
        var apiKey = configuration[ConfigKeys.OpenAiKey];

        // Explicit resilience (§3), same settings as AzureOpenAiService: the SDK pipeline retries
        // transient 429/5xx/timeouts with exponential backoff before surfacing an error.
        var options = new AzureOpenAIClientOptions
        {
            NetworkTimeout = TimeSpan.FromMinutes(3),
            RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(maxRetries: 3),
        };

        AzureOpenAIClient client;
        if (string.IsNullOrEmpty(apiKey))
        {
            client = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential(), options);
        }
        else
        {
            _keyCredential = new Azure.AzureKeyCredential(apiKey);
            client = new AzureOpenAIClient(new Uri(endpoint), _keyCredential, options);
        }

        _chatClient = client.GetChatClient(deployment);
        _logger.LogInformation("AzureOpenAI chat completion service initialized. Deployment={Deployment}", deployment);
    }

    public async Task<ChatCompletionResult> CompleteAsync(
        string systemPrompt, string userPrompt, byte[]? image = null, CancellationToken ct = default)
    {
        if (_chatClient is null)
            throw new InvalidOperationException(
                "Azure OpenAI chat completion is not configured. Set OpenAI:Endpoint (and OpenAI:Key, "
                + "unless managed identity is in use) via Key Vault.");

        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        // Key Vault rotation without a restart, matching AzureOpenAiService.RefreshCredentials.
        var currentKey = _configuration[ConfigKeys.OpenAiKey];
        if (!string.IsNullOrWhiteSpace(currentKey)) _keyCredential?.Update(currentKey);

        var start = Stopwatch.GetTimestamp();

        var userMessage = image is null
            ? new UserChatMessage(userPrompt)
            : new UserChatMessage(
                ChatMessageContentPart.CreateImagePart(new Uri(ToDataUrl(image))),
                ChatMessageContentPart.CreateTextPart(userPrompt));

        var messages = new List<ChatMessage> { new SystemChatMessage(systemPrompt), userMessage };

        // Neither Temperature nor MaxOutputTokenCount is set, for the reason documented in
        // AzureOpenAiService.EnhanceDescriptionAsync: GPT-5-class deployments reject a custom
        // temperature outright, and Azure.AI.OpenAI 2.1.0 still emits the legacy max_tokens field
        // that those deployments refuse in favour of max_completion_tokens. Sending neither keeps
        // one code path compatible across GPT-4o- and GPT-5-class deployments.
        var response = await _chatClient.CompleteChatAsync(messages, cancellationToken: ct);

        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        if (response.Value.Content.Count == 0)
        {
            // Empty content is the shape a content-filter refusal takes here. Callers treat an
            // empty string as "no model output" and fall back, so surface it as a warning rather
            // than throwing — a refused roast should still produce a track.
            _logger.LogWarning(
                "Azure OpenAI chat completion returned no content. FinishReason={Reason}",
                response.Value.FinishReason);
            return new ChatCompletionResult(string.Empty, response.Value.Usage?.TotalTokenCount ?? 0, elapsed);
        }

        var content = response.Value.Content[0].Text.Trim();
        var tokens = response.Value.Usage?.TotalTokenCount ?? 0;

        _logger.LogInformation(
            "Azure OpenAI chat completion finished in {Elapsed}ms. Tokens={Tokens}, Image={HasImage}",
            elapsed, tokens, image is not null);

        return new ChatCompletionResult(content, tokens, elapsed);
    }

    /// <summary>
    /// Sniffs the JPEG magic bytes the same way <see cref="AzureOpenAiService.DescribePersonAsync"/>
    /// does; everything else is declared PNG, which the service accepts for the formats this app
    /// produces.
    /// </summary>
    private static string ToDataUrl(byte[] image)
    {
        var mimeType = image.Length >= 2 && image[0] == 0xFF && image[1] == 0xD8 ? "image/jpeg" : "image/png";
        return $"data:{mimeType};base64,{Convert.ToBase64String(image)}";
    }
}
