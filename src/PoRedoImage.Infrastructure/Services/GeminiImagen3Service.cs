using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Google Gemini / Imagen 3 implementation of IImageGenerationService.
/// Adapter pattern (GoF): wraps the Gemini REST API.
/// </summary>
public sealed class GeminiImagen3Service : IImageGenerationService
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

        var (imageData, contentType) = await GenerateWithGeminiAsync(prompt, imageBytes, seed: 0, ct);

        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.LogInformation("Gemini API img2img complete in {Elapsed}ms. Size={Size} bytes", elapsed, imageData.Length);
        return (imageData, contentType, elapsed);
    }

    /// <remarks>
    /// Idea #11 — One-Tap Re-roll: when a winning prompt is re-rolled, we pass a deterministic seed
    /// hint to the upstream model so that parallel variations diverge from each other in a
    /// reproducible way (per-session), but still remain visually close to the original "winner".
    /// The seed is mixed into the prompt text — the Gemini REST surface does not expose a top-level
    /// seed field for image-to-image generation, so we encode it as a creative direction hint.
    /// </remarks>
    public async Task<(byte[] ImageData, string ContentType, long ElapsedMs)>
        GenerateImageAsync(string prompt, byte[] imageBytes, int seed, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Gemini image generation is not configured. Set Google:ApiKey in user-secrets.");

        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(seed);

        _logger.LogInformation("Calling Gemini API (img2img re-roll). Model={Model}, Seed={Seed}", _model, seed);
        var start = Stopwatch.GetTimestamp();

        var (imageData, contentType) = await GenerateWithGeminiAsync(prompt, imageBytes, seed, ct);

        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.LogInformation("Gemini API re-roll complete in {Elapsed}ms. Seed={Seed}, Size={Size} bytes",
            elapsed, seed, imageData.Length);
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
            ? await GenerateWithGeminiAsync(prompt, null, seed: 0, ct)
            : await GenerateWithImagenAsync(prompt, ct);

        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _logger.LogInformation("Gemini API complete in {Elapsed}ms. Size={Size} bytes", elapsed, imageData.Length);
        return (imageData, contentType, elapsed);
    }

    private async Task<(byte[] ImageData, string ContentType)> GenerateWithGeminiAsync(
        string prompt, byte[]? referenceImageBytes, int seed, CancellationToken ct)
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
            ? BuildSeededPrompt(prompt, seed)
            : prompt;

        parts.Add(new { text = finalPrompt });

        // Two-part payload: a system role that forces image-only output, then the user prompt.
        // Without the explicit system instruction, gemini-2.5-flash-image frequently replies
        // with conversational text ("Sure, here it is:") and no inlineData, which previously
        // bubbled up as a 500. Constraining the modalities + system role eliminates that path.
        var systemInstruction = new
        {
            role = "system",
            parts = new[]
            {
                new
                {
                    text = "You are an image-generation engine. " +
                           "You MUST respond with exactly one image part and no other text. " +
                           "Never reply with conversational text, preambles, or postambles. " +
                           "If you cannot generate the image, respond with a single text part starting with the word REFUSE:."
                }
            }
        };

        var body = new
        {
            systemInstruction,
            contents = new[] { new { parts } },
            generationConfig = new
            {
                // Lock output to IMAGE only — Gemini cannot fall back to a text-only reply.
                responseModalities = new[] { "image" },
                temperature = 1.0
            }
        };

        _logger.LogDebug(
            "Gemini request: model={Model}, parts={PartCount}, hasRefImage={HasRef}, promptLen={PromptLen}",
            _model, parts.Count, referenceImageBytes is not null, finalPrompt.Length);

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

        var rawJson = await response.Content.ReadAsStringAsync(ct);
        _logger.LogDebug("Gemini raw response: {Body}", rawJson);

        using var json = JsonDocument.Parse(rawJson);

        // Surface API-level errors (e.g., INVALID_ARGUMENT, permission) before walking candidates.
        if (json.RootElement.TryGetProperty("error", out var apiError))
        {
            var msg = apiError.TryGetProperty("message", out var m) ? m.GetString() : "unknown error";
            throw new InvalidOperationException($"Gemini API error: {msg}");
        }

        foreach (var candidate in json.RootElement.GetProperty("candidates").EnumerateArray())
        {
            if (candidate.TryGetProperty("finishReason", out var finishReason))
            {
                var reason = finishReason.GetString();
                if (reason is "SAFETY" or "RECITATION" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "SPII")
                    throw new InvalidOperationException($"Image blocked by Gemini safety filters (reason: {reason}).");
            }

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
            {
                // System-instruction contract: refusal always begins with "REFUSE:".
                var prefix = "REFUSE:";
                var clean = refusalText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? refusalText[prefix.Length..].Trim()
                    : refusalText;
                throw new InvalidOperationException($"Gemini declined to generate an image: {clean}");
            }
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

    /// <summary>
    /// Builds a creative-direction prompt that nudges the model to produce a variation
    /// distinct from prior runs. We use a small table of "variance phrases" keyed by seed
    /// so the same seed always produces the same nudge (reproducible, cheap, and works
    /// across all Gemini model versions that do not expose an explicit seed parameter).
    /// </summary>
    private static string BuildSeededPrompt(string basePrompt, int seed)
    {
        if (seed == 0)
            return $"You are a creative image editor. Preserve the person's facial features exactly. Apply: {basePrompt}";

        // 8 well-spaced variance cues — enough for 3–10 parallel re-rolls without overlap.
        var cues = new[]
        {
            "with a slightly cooler color temperature",
            "with a slightly warmer color temperature",
            "with more pronounced shadows and contrast",
            "with softer lighting and pastel tones",
            "with a more saturated, vivid look",
            "with a muted, desaturated film-stock feel",
            "with a different camera lens perspective",
            "with a subtle stylistic reinterpretation"
        };
        var cue = cues[seed % cues.Length];

        return $"You are a creative image editor. Preserve the person's facial features exactly. " +
               $"Apply: {basePrompt}. Render the result {cue}.";
    }
}
