using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// HuggingFace Inference Providers implementation of <see cref="IChatCompletionService"/> — the
/// real-AI backend for the Style Director reasoning agents. Talks the OpenAI-compatible chat API
/// exposed by the HF router (<c>POST {BaseUrl}/v1/chat/completions</c>), so a text prompt routes to
/// <c>HuggingFace:ChatModel</c> and an image prompt routes to the vision-capable
/// <c>HuggingFace:VisionModel</c>. Reuses the same token (<c>HuggingFace:ApiKey</c>) and named
/// HttpClient (with resilience + the mock guardrail handler) as the image-generation service.
/// </summary>
public sealed class HuggingFaceChatCompletionService : IChatCompletionService
{
    private readonly ILogger<HuggingFaceChatCompletionService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    private string BaseUrl => (_configuration["HuggingFace:BaseUrl"] ?? "https://router.huggingface.co").TrimEnd('/');
    private string ChatModel => _configuration["HuggingFace:ChatModel"] ?? "Qwen/Qwen2.5-7B-Instruct";
    private string VisionModel => _configuration["HuggingFace:VisionModel"] ?? "Qwen/Qwen2.5-VL-72B-Instruct";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["HuggingFace:ApiKey"]);

    public HuggingFaceChatCompletionService(
        IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<HuggingFaceChatCompletionService> logger)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;

        // Mirror the other real AI services: never let a real provider construct under mock mode.
        if (configuration.GetValue<bool>("Mocks:UseMockAi"))
            throw new InvalidOperationException(
                "HuggingFaceChatCompletionService was constructed while Mocks:UseMockAi=true — DI should "
                + "have resolved MockChatCompletionService. Blocking to guarantee zero live token spend.");

        if (IsConfigured)
            _logger.LogInformation("HuggingFace chat service initialized. chat={Chat}, vision={Vision}", ChatModel, VisionModel);
        else
            _logger.LogInformation("HuggingFace:ApiKey not configured; HuggingFace chat completions are disabled.");
    }

    public async Task<ChatCompletionResult> CompleteAsync(
        string systemPrompt, string userPrompt, byte[]? image = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "HuggingFace chat completion is not configured. Set HuggingFace:ApiKey (a token with the "
                + "'Inference Providers' permission) in user-secrets / Key Vault.");
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        var start = Stopwatch.GetTimestamp();
        var model = image is null ? ChatModel : VisionModel;

        // OpenAI-compatible chat body. For the vision case the user turn carries a text part plus an
        // image_url data-URI part; otherwise the content is a plain string.
        object userContent = image is null
            ? userPrompt
            : new object[]
            {
                new { type = "text", text = userPrompt },
                new { type = "image_url", image_url = new { url = ToDataUri(image) } },
            };

        var body = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent },
            },
            max_tokens = 700,
        };

        var client = _httpClientFactory.CreateClient("HuggingFaceApi");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _configuration["HuggingFace:ApiKey"] ?? string.Empty);
        request.Content = JsonContent.Create(body);

        using var response = await client.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("HuggingFace chat API error {Status}: {Body}", (int)response.StatusCode, Truncate(raw, 400));
            throw new InvalidOperationException($"HuggingFace chat API returned {(int)response.StatusCode}: {Truncate(raw, 200)}");
        }

        using var json = JsonDocument.Parse(raw);
        var root = json.RootElement;
        var content = root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var msg)
            && msg.TryGetProperty("content", out var c)
                ? c.GetString() ?? string.Empty
                : string.Empty;

        var tokens = root.TryGetProperty("usage", out var usage)
            && usage.TryGetProperty("total_tokens", out var tt) && tt.TryGetInt32(out var t) ? t : 0;

        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.LogInformation("HuggingFace chat complete in {Elapsed}ms via {Model}. Tokens={Tokens}", elapsed, model, tokens);

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("HuggingFace chat API returned an empty completion.");

        return new ChatCompletionResult(content.Trim(), tokens, elapsed);
    }

    private static string ToDataUri(byte[] bytes)
    {
        var mime = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? "image/jpeg" : "image/png";
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
