using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Google Gemini / Imagen 3 implementation of IImagen3Service.
/// Adapter pattern (GoF): wraps the Gemini REST API.
/// </summary>
public sealed class GeminiImagen3Service : IImagen3Service
{
    private readonly ILogger<GeminiImagen3Service> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly string _model;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["Google:ApiKey"]);

    public GeminiImagen3Service(IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<GeminiImagen3Service> logger)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _model = configuration["Google:Imagen3Model"] ?? "gemini-2.0-flash-exp-image-generation";

        if (IsConfigured)
            _logger.LogInformation("Gemini image service initialized. Model={Model}", _model);
        else
            _logger.LogInformation("Google:ApiKey not configured; Gemini image generation is disabled.");
    }

    public async Task<(byte[] ImageData, string ContentType, long ElapsedMs)>
        GenerateImageAsync(string prompt, byte[] imageBytes, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Gemini image generation is not configured. Set Google:ApiKey in user-secrets.");

        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(imageBytes);

        _logger.LogInformation("Calling Gemini API (img2img). Model={Model}", _model);
        var start = Stopwatch.GetTimestamp();

        var (imageData, contentType) = await GenerateWithGeminiAsync(prompt, imageBytes, ct);

        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.LogInformation("Gemini API img2img complete in {Elapsed}ms. Size={Size} bytes", elapsed, imageData.Length);
        return (imageData, contentType, elapsed);
    }

    public async Task<(byte[] ImageData, string ContentType, long ElapsedMs)>
        GenerateAsync(string prompt, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Gemini image generation is not configured. Set Google:ApiKey in user-secrets.");

        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        _logger.LogInformation("Calling Gemini API. Model={Model}", _model);
        var start = Stopwatch.GetTimestamp();

        var (imageData, contentType) = _model.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase)
            ? await GenerateWithGeminiAsync(prompt, null, ct)
            : await GenerateWithImagenAsync(prompt, ct);

        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.LogInformation("Gemini API complete in {Elapsed}ms. Size={Size} bytes", elapsed, imageData.Length);
        return (imageData, contentType, elapsed);
    }

    private async Task<(byte[] ImageData, string ContentType)> GenerateWithGeminiAsync(
        string prompt, byte[]? referenceImageBytes, CancellationToken ct)
    {
        var parts = new List<object>();

        if (referenceImageBytes is not null)
        {
            parts.Add(new
            {
                inlineData = new
                {
                    mimeType = DetectMimeType(referenceImageBytes),
                    data = Convert.ToBase64String(referenceImageBytes)
                }
            });
        }

        var finalPrompt = referenceImageBytes is not null
            ? $"You are a creative image editor. Preserve the person's facial features exactly. Apply: {prompt}"
            : prompt;

        parts.Add(new { text = finalPrompt });

        var body = new
        {
            contents = new[] { new { parts } },
            generationConfig = new { responseModalities = new[] { "image", "text" } }
        };

        var client = _httpClientFactory.CreateClient("GeminiApi");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        // Re-read API key from IConfiguration to pick up Key Vault rotated secrets (singleton lifetime)
        request.Headers.Add("x-goog-api-key", _configuration["Google:ApiKey"] ?? string.Empty);
        request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Gemini API error {Status}: {Body}", (int)response.StatusCode, errorBody);
            throw new InvalidOperationException($"Gemini API returned {(int)response.StatusCode}: {errorBody}");
        }

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        foreach (var candidate in json.RootElement.GetProperty("candidates").EnumerateArray())
        {
            if (candidate.TryGetProperty("finishReason", out var finishReason) &&
                finishReason.GetString() is "SAFETY" or "RECITATION" or "PROHIBITED_CONTENT")
                throw new InvalidOperationException($"Image blocked by Gemini safety filters (reason: {finishReason.GetString()}).");

            if (!candidate.TryGetProperty("content", out var content)) continue;

            string? refusalText = null;
            foreach (var part in content.GetProperty("parts").EnumerateArray())
            {
                if (part.TryGetProperty("inlineData", out var inlineData))
                {
                    var mimeType = inlineData.GetProperty("mimeType").GetString() ?? "image/png";
                    var data = inlineData.GetProperty("data").GetString()
                        ?? throw new InvalidOperationException("No image data in Gemini response");
                    return (Convert.FromBase64String(data), mimeType);
                }
                if (part.TryGetProperty("text", out var textEl))
                    refusalText = textEl.GetString();
            }

            if (refusalText is not null)
                throw new InvalidOperationException($"Gemini declined to generate an image: {refusalText}");
        }

        throw new InvalidOperationException("Gemini returned no image. Check logs for details.");
    }

    private async Task<(byte[] ImageData, string ContentType)> GenerateWithImagenAsync(string prompt, CancellationToken ct)
    {
        var body = new
        {
            instances = new[] { new { prompt } },
            parameters = new { sampleCount = 1, aspectRatio = "1:1", safetyFilterLevel = "block_some", personGeneration = "allow_adult" }
        };

        var client = _httpClientFactory.CreateClient("GeminiApi");
        var url = $"https://us-central1-aiplatform.googleapis.com/v1/projects/*/locations/us-central1/publishers/google/models/{_model}:predict";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", _configuration["Google:ApiKey"] ?? string.Empty);
        request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Imagen API returned {(int)response.StatusCode}: {errorBody}");
        }

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var predictions = json.RootElement.GetProperty("predictions");
        var first = predictions.EnumerateArray().First();
        var b64 = first.GetProperty("bytesBase64Encoded").GetString()!;
        var mime = first.TryGetProperty("mimeType", out var mt) ? mt.GetString() ?? "image/png" : "image/png";
        return (Convert.FromBase64String(b64), mime);
    }

    private static string DetectMimeType(byte[] bytes) =>
        bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? "image/jpeg" : "image/png";
}
