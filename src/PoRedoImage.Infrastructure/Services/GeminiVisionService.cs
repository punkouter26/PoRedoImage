using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRedoImage.Application.Configuration;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Google Gemini implementation of <see cref="IVisionService"/> providing fast, sub-second multimodal
/// image-to-text and tagging using Gemini Flash models.
/// </summary>
public sealed class GeminiVisionService : IVisionService
{
    private readonly ILogger<GeminiVisionService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly string _model;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration[ConfigKeys.GoogleApiKey]);

    public GeminiVisionService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<GeminiVisionService> logger)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _model = configuration[ConfigKeys.GoogleVisionModel] ?? "gemini-2.5-flash";

        if (ConfigValue.Bool(configuration, ConfigKeys.MocksUseMockAi))
        {
            throw new InvalidOperationException(
                "GeminiVisionService was constructed while Mocks:UseMockAi=true. The DI container "
                + "should have resolved MockVisionService instead.");
        }

        if (IsConfigured)
            _logger.LogInformation("Gemini vision service initialized. Model={Model}", _model);
        else
            _logger.LogInformation("Google:ApiKey not configured; Gemini vision analysis is disabled.");
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Anonymous types for Gemini REST payload; assembly is not trimmed.")]
    public async Task<(string Description, IReadOnlyList<string> Tags, double ConfidenceScore, long ElapsedMs)>
        AnalyzeAsync(byte[] imageData, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        if (imageData.Length == 0)
            throw new ArgumentException("Image data cannot be empty", nameof(imageData));

        if (!IsConfigured)
            throw new InvalidOperationException("Gemini vision is not configured. Set Google:ApiKey via Key Vault.");

        var start = Stopwatch.GetTimestamp();
        var mimeType = imageData.Length >= 2 && imageData[0] == 0xFF && imageData[1] == 0xD8 ? "image/jpeg" : "image/png";

        var body = new
        {
            systemInstruction = new
            {
                role = "system",
                parts = new[]
                {
                    new
                    {
                        text = "You are an expert image analyst. Look at the image and reply in JSON format with schema: "
                             + "{\"description\": \"one vivid, specific sentence describing what is happening\", "
                             + "\"tags\": [\"8-14 lowercase labels for the objects, setting, and activity present\"]}. "
                             + "Report only what is visible; do not invent."
                    }
                }
            },
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new
                        {
                            inlineData = new
                            {
                                mimeType,
                                data = Convert.ToBase64String(imageData)
                            }
                        },
                        new
                        {
                            text = "Analyze this image and return the structured description and tags."
                        }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                temperature = 0.2
            }
        };

        var client = _httpClientFactory.CreateClient("GeminiApi");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", _configuration[ConfigKeys.GoogleApiKey] ?? string.Empty);
        request.Content = JsonContent.Create(body);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Gemini Vision API returned {(int)response.StatusCode}: {errorBody}");
        }

        using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(responseStream, cancellationToken: ct);

        var (description, tags) = ParseResponse(doc.RootElement);
        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        _logger.LogInformation("Gemini vision analysis complete in {Elapsed}ms. Tags={Count}", elapsed, tags.Count);
        return (description, tags, 1.0, elapsed);
    }

    private static (string Description, IReadOnlyList<string> Tags) ParseResponse(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return ("No description available.", Array.Empty<string>());

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0)
            return ("No description available.", Array.Empty<string>());

        var text = parts[0].TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(text))
            return ("No description available.", Array.Empty<string>());

        try
        {
            using var json = JsonDocument.Parse(text);
            var desc = json.RootElement.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            var tagList = new List<string>();
            if (json.RootElement.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in tg.EnumerateArray())
                {
                    var val = el.GetString()?.Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(val)) tagList.Add(val);
                }
            }
            return (string.IsNullOrWhiteSpace(desc) ? "No description available." : desc, tagList.Distinct().Take(16).ToList());
        }
        catch
        {
            return (text.Trim(), Array.Empty<string>());
        }
    }
}
