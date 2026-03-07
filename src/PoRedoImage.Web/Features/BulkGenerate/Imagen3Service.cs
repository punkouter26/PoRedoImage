using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace PoRedoImage.Web.Features.BulkGenerate;

public interface IImagen3Service
{
    /// <summary>True when Google:ApiKey is configured.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Generates an image using the Gemini API.
    /// When model is gemini-* (default): sends prompt + reference image for true img2img.
    /// When model is imagen-*: sends prompt only (text-to-image via Imagen 3).
    /// </summary>
    Task<(byte[] ImageData, string ContentType, long ProcessingTimeMs)> GenerateImageAsync(
        string prompt,
        byte[]? referenceImageBytes = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Google Gemini API image generation service.
/// Authenticates via API key (Google:ApiKey in user-secrets / appsettings).
/// Configure with:
///   Google:ApiKey        - Gemini API key from Google AI Studio (required)
///   Google:Imagen3Model  - model ID (default: gemini-2.0-flash-exp-image-generation)
///                          Use "imagen-3.0-generate-002" for Imagen 3 text-to-image.
/// </summary>
public class Imagen3Service : IImagen3Service
{
    private readonly ILogger<Imagen3Service> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _apiKey;
    private readonly string _model;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public Imagen3Service(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<Imagen3Service> logger)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _apiKey = configuration["Google:ApiKey"];
        _model = configuration["Google:Imagen3Model"] ?? "gemini-2.0-flash-exp-image-generation";

        if (IsConfigured)
            _logger.LogInformation("Gemini image service initialised. Model: {Model}", _model);
        else
            _logger.LogInformation("Google:ApiKey not configured; Gemini image generation is disabled.");
    }

    public async Task<(byte[] ImageData, string ContentType, long ProcessingTimeMs)> GenerateImageAsync(
        string prompt,
        byte[]? referenceImageBytes = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "Gemini image generation is not configured. Run: dotnet user-secrets set \"Google:ApiKey\" \"YOUR_KEY\"");

        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        _logger.LogInformation(
            "Calling Gemini API. Model: {Model}, HasReferenceImage: {HasRef}",
            _model, referenceImageBytes is not null);

        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var (imageData, contentType) = _model.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase)
                ? await GenerateWithGeminiAsync(prompt, referenceImageBytes, cancellationToken)
                : await GenerateWithImagenAsync(prompt, cancellationToken);

            var processingTime = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            _logger.LogInformation(
                "Gemini API completed in {ProcessingTime}ms. Output: {Size} bytes",
                processingTime, imageData.Length);

            return (imageData, contentType, processingTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating image with Gemini API");
            throw;
        }
    }

    /// <summary>
    /// Gemini generateContent API - accepts image + text for true img2img.
    /// Model: gemini-2.0-flash-exp-image-generation
    /// </summary>
    private async Task<(byte[] ImageData, string ContentType)> GenerateWithGeminiAsync(
        string prompt, byte[]? referenceImageBytes, CancellationToken cancellationToken)
    {
        var parts = new List<object>();

        // Image FIRST: Gemini treats it as the subject to edit, not a style hint
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

        // Frame as an editing instruction when a reference image is present so
        // Gemini preserves the person's face/body and only applies the requested change.
        // Note: gemini-2.0-flash-exp-image-generation does NOT support system_instruction,
        // so all persona context is embedded directly in the user prompt.
        var finalPrompt = referenceImageBytes is not null
            ? $"You are a creative image editor. Use the person in the reference photo as the subject — " +
              "preserve their facial features, skin tone, age and body proportions exactly. " +
              $"Apply only the transformation described: {prompt}. " +
              "Only change what the prompt explicitly describes (clothing, setting, art style, etc.)."
            : prompt;

        parts.Add(new { text = finalPrompt });

        var body = new
        {
            contents = new[] { new { parts } },
            generationConfig = new { responseModalities = new[] { "image", "text" } }
        };

        var client = _httpClientFactory.CreateClient("GeminiApi");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent";
        using var geminiRequest = new HttpRequestMessage(HttpMethod.Post, url);
        geminiRequest.Headers.Add("x-goog-api-key", _apiKey);
        geminiRequest.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(geminiRequest, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Gemini API error {Status}: {Body}", (int)response.StatusCode, errorBody);
            throw new InvalidOperationException($"Gemini API returned {(int)response.StatusCode}: {errorBody}");
        }

        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        foreach (var candidate in json.RootElement.GetProperty("candidates").EnumerateArray())
        {
            // Check if blocked by safety filters
            if (candidate.TryGetProperty("finishReason", out var finishReason) &&
                finishReason.GetString() is "SAFETY" or "RECITATION" or "PROHIBITED_CONTENT")
            {
                throw new InvalidOperationException($"Image blocked by Gemini safety filters (reason: {finishReason.GetString()}). Try a different image or prompt.");
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
                // Capture any text explanation Gemini provided instead of an image
                if (part.TryGetProperty("text", out var textEl))
                    refusalText = textEl.GetString();
            }
            if (refusalText is not null)
                throw new InvalidOperationException($"Gemini declined to generate an image: {refusalText}");
        }

        // Log the raw response to aid debugging
        var rawJson = json.RootElement.GetRawText();
        _logger.LogWarning("Gemini returned no image part. Raw response: {Response}", rawJson);
        throw new InvalidOperationException("Gemini returned no image. The model may have declined the request. Check logs for details.");
    }

    /// <summary>
    /// Imagen predict API - text-to-image only.
    /// Model: imagen-3.0-generate-002
    /// </summary>
    private async Task<(byte[] ImageData, string ContentType)> GenerateWithImagenAsync(
        string prompt, CancellationToken cancellationToken)
    {
        var body = new
        {
            instances = new[] { new { prompt } },
            parameters = new
            {
                sampleCount = 1,
                aspectRatio = "1:1",
                safetyFilterLevel = "block_some",
                personGeneration = "allow_adult"
            }
        };

        var client = _httpClientFactory.CreateClient("GeminiApi");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:predict";
        using var imagenRequest = new HttpRequestMessage(HttpMethod.Post, url);
        imagenRequest.Headers.Add("x-goog-api-key", _apiKey);
        imagenRequest.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(imagenRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        var prediction = json.RootElement.GetProperty("predictions").EnumerateArray().FirstOrDefault();
        var imageBase64 = prediction.GetProperty("bytesBase64Encoded").GetString()
            ?? throw new InvalidOperationException("No image data in Imagen API response");
        var mimeType = prediction.TryGetProperty("mimeType", out var mt)
            ? mt.GetString() ?? "image/png"
            : "image/png";

        return (Convert.FromBase64String(imageBase64), mimeType);
    }

    private static string DetectMimeType(byte[] bytes) =>
        bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8
            ? "image/jpeg"
            : "image/png";
}
