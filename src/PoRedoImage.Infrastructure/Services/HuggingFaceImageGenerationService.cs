using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// HuggingFace Inference Providers implementation of <see cref="IImageGenerationService"/> — the
/// low-cost alternative to Gemini/Imagen (see docs/CONFORMANCE-BACKLOG.md). Routes through the HF
/// router to the <c>fal-ai</c> provider (verified live): text-to-image via FLUX.1-schnell
/// (~$0.003/img), image-to-image (the regenerate flow) via Qwen-Image-Edit (~$0.02/img) — both
/// NON-gated so a standard Inference-Providers token works. Selected only when
/// <c>ImageGen:Provider=huggingface</c>. Adapter pattern (GoF) over the fal-native REST shape:
/// POST <c>{BaseUrl}/{provider}/{providerId}</c> with <c>{prompt, image_url?}</c> → <c>{images:[{url}]}</c>.
/// </summary>
public sealed class HuggingFaceImageGenerationService : IImageGenerationService
{
    private readonly ILogger<HuggingFaceImageGenerationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    private string BaseUrl => (_configuration[ConfigKeys.HuggingFaceBaseUrl] ?? "https://router.huggingface.co").TrimEnd('/');
    private string Provider => _configuration[ConfigKeys.HuggingFaceProvider] ?? "fal-ai";
    private string T2IProviderId => _configuration[ConfigKeys.HuggingFaceTextToImageProviderId] ?? "fal-ai/flux/schnell";
    private string I2IProviderId => _configuration[ConfigKeys.HuggingFaceImageToImageProviderId] ?? "fal-ai/qwen-image-edit";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration[ConfigKeys.HuggingFaceApiKey]);

    public HuggingFaceImageGenerationService(
        IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<HuggingFaceImageGenerationService> logger)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;

        // Mirror the Gemini guard: never let a real provider construct under mock mode.
        if (configuration.GetValue<bool>(ConfigKeys.MocksUseMockAi))
            throw new InvalidOperationException(
                "HuggingFaceImageGenerationService was constructed while Mocks:UseMockAi=true — DI should "
                + "have resolved MockImagen3Service. Blocking to guarantee zero live token spend.");

        if (IsConfigured)
            _logger.LogInformation("HuggingFace image service initialized. t2i={T2I}, i2i={I2I}", T2IProviderId, I2IProviderId);
        else
            _logger.LogInformation("HuggingFace:ApiKey not configured; HuggingFace image generation is disabled.");
    }

    public Task<(byte[] ImageData, string ContentType, long ElapsedMs)>
        GenerateImageAsync(string prompt, byte[] imageBytes, CancellationToken ct = default)
        => GenerateImageAsync(prompt, imageBytes, seed: 0, ct);

    public async Task<(byte[] ImageData, string ContentType, long ElapsedMs)>
        GenerateImageAsync(string prompt, byte[] imageBytes, int seed, CancellationToken ct = default)
    {
        EnsureConfigured();
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(seed);

        // fal image-to-image: the source image travels as a data-URI in `image_url`.
        var dataUri = $"data:{DetectMimeType(imageBytes)};base64,{Convert.ToBase64String(imageBytes)}";
        var body = new Dictionary<string, object> { ["prompt"] = prompt, ["image_url"] = dataUri };
        if (seed > 0) body["seed"] = seed;

        _logger.HuggingFaceImg2ImgStarting(Provider, I2IProviderId, seed);
        return await PostAsync(I2IProviderId, body, ct);
    }

    public async Task<(byte[] ImageData, string ContentType, long ElapsedMs)>
        GenerateAsync(string prompt, CancellationToken ct = default)
    {
        EnsureConfigured();
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        _logger.HuggingFaceText2ImgStarting(Provider, T2IProviderId);
        return await PostAsync(T2IProviderId, new Dictionary<string, object> { ["prompt"] = prompt }, ct);
    }

    private async Task<(byte[] ImageData, string ContentType, long ElapsedMs)> PostAsync(
        string providerId, object body, CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        var client = _httpClientFactory.CreateClient("HuggingFaceApi");
        var url = $"{BaseUrl}/{Provider}/{providerId}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", _configuration[ConfigKeys.HuggingFaceApiKey] ?? string.Empty);
        request.Content = JsonContent.Create(body);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.HuggingFaceError((int)response.StatusCode, errorBody);
            throw new InvalidOperationException($"HuggingFace API returned {(int)response.StatusCode}: {errorBody}");
        }

        var (data, contentType) = await ReadImageAsync(response, client, ct);
        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.HuggingFaceComplete(elapsed, data.Length);
        return (data, contentType, elapsed);
    }

    /// <summary>
    /// fal replies with JSON <c>{"images":[{"url","content_type"}]}</c>; the HF-normalized task API
    /// (e.g. hf-inference) replies with raw image bytes. Handle both so a provider swap in config
    /// doesn't break the call.
    /// </summary>
    private static async Task<(byte[] Data, string ContentType)> ReadImageAsync(
        HttpResponseMessage response, HttpClient client, CancellationToken ct)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return (await response.Content.ReadAsByteArrayAsync(ct), mediaType);

        var raw = await response.Content.ReadAsStringAsync(ct);
        using var json = JsonDocument.Parse(raw);

        if (TryGetImage(json.RootElement, out var url, out var jsonContentType) && url is not null)
        {
            var bytes = await client.GetByteArrayAsync(url, ct);
            return (bytes, jsonContentType ?? GuessContentType(url));
        }

        throw new InvalidOperationException($"HuggingFace returned no image URL. Body: {Truncate(raw, 300)}");
    }

    private static bool TryGetImage(JsonElement root, out string? url, out string? contentType)
    {
        url = null;
        contentType = null;

        // fal / most providers: { "images": [ { "url", "content_type" } ] }
        if (root.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array
            && images.GetArrayLength() > 0)
        {
            var first = images[0];
            url = first.TryGetProperty("url", out var u) ? u.GetString() : null;
            contentType = first.TryGetProperty("content_type", out var c) ? c.GetString() : null;
            return url is not null;
        }
        // Fallbacks: { "image": { "url" } } or top-level { "url" }
        if (root.TryGetProperty("image", out var img) && img.ValueKind == JsonValueKind.Object
            && img.TryGetProperty("url", out var u1))
        {
            url = u1.GetString();
            return url is not null;
        }
        if (root.TryGetProperty("url", out var u2) && u2.ValueKind == JsonValueKind.String)
        {
            url = u2.GetString();
            return url is not null;
        }
        return false;
    }

    private static string DetectMimeType(byte[] bytes) =>
        bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? "image/jpeg" : "image/png";

    private static string GuessContentType(string url) =>
        url.Contains(".jpg", StringComparison.OrdinalIgnoreCase) || url.Contains(".jpeg", StringComparison.OrdinalIgnoreCase)
            ? "image/jpeg" : "image/png";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "HuggingFace image generation is not configured. Set HuggingFace:ApiKey (a token with the "
                + "'Inference Providers' permission) in user-secrets / Key Vault.");
    }
}
