using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRedoImage.Application.Configuration;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

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

    private string BaseUrl => (_configuration[ConfigKeys.HuggingFaceBaseUrl] ?? "https://router.huggingface.co").TrimEnd('/');
    private string ChatModel => _configuration[ConfigKeys.HuggingFaceChatModel] ?? "Qwen/Qwen2.5-7B-Instruct";
    private string VisionModel => _configuration[ConfigKeys.HuggingFaceVisionModel] ?? "Qwen/Qwen2.5-VL-72B-Instruct";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration[ConfigKeys.HuggingFaceApiKey]);

    public HuggingFaceChatCompletionService(
        IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<HuggingFaceChatCompletionService> logger)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;

        // Mirror the other real AI services: never let a real provider construct under mock mode.
        if (ConfigValue.Bool(configuration, ConfigKeys.MocksUseMockAi))
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
                + "'Inference Providers' permission) in Key Vault.");
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        var start = Stopwatch.GetTimestamp();
        var model = image is null ? ChatModel : VisionModel;

        // Compress large images so the router's payload limit doesn't 413 the request. Only affects
        // the vision branch — text-only prompts pass through untouched.
        var visionInput = image is null ? null : CompressForVision(image, _logger);

        // OpenAI-compatible chat body. For the vision case the user turn carries a text part plus an
        // image_url data-URI part; otherwise the content is a plain string.
        object userContent = visionInput is null
            ? userPrompt
            : new object[]
            {
                new { type = "text", text = userPrompt },
                new { type = "image_url", image_url = new { url = ToDataUri(visionInput) } },
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _configuration[ConfigKeys.HuggingFaceApiKey] ?? string.Empty);
        // IL2026: the outbound body is an anonymous type shaped to the third-party API's exact
        // contract, and System.Text.Json source generation cannot describe anonymous types. This
        // assembly is server-side only and is never trimmed, so the reflective writer is safe here.
        // Scoped to the single statement so the analyzer (Directory.Build.props) stays live elsewhere.
        #pragma warning disable IL2026
        request.Content = JsonContent.Create(body);
        #pragma warning restore IL2026

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

    /// <summary>
    /// HuggingFace's chat-completions router rejects requests over a few MB with HTTP 413
    /// (request entity too large). The original photos users upload are typically 3-5 MB,
    /// well over that limit. Compress any input larger than ~768 KB down to a 1024-px-wide JPEG
    /// so vision calls stay under the gateway's payload ceiling while preserving enough detail
    /// for the model to read the scene. Skips re-encoding if the image is already small.
    /// </summary>
    internal static byte[] CompressForVision(byte[] source, ILogger logger)
    {
        const long largeThresholdBytes = 768 * 1024;
        const int maxDimension = 1024;
        const int quality = 85;

        if (source is null || source.Length < largeThresholdBytes)
            return source ?? Array.Empty<byte>();

        try
        {
            using var input = new MemoryStream(source);
            using var image = SixLabors.ImageSharp.Image.Load(input);
            if (image.Width <= maxDimension && image.Height <= maxDimension)
                return source;

            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(maxDimension, maxDimension),
                Mode = ResizeMode.Max,
            }));

            using var output = new MemoryStream();
            image.SaveAsJpeg(output, new JpegEncoder { Quality = quality });
            var result = output.ToArray();
            logger.LogInformation(
                "HuggingFace vision input downscaled: {From} bytes → {To} bytes ({Pct}%), {W}x{H}",
                source.Length, result.Length, (int)(100.0 * result.Length / source.Length),
                image.Width, image.Height);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Image compression for vision failed; sending original bytes.");
            return source;
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
