using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Local image-to-text via Ollama (e.g. gemma4 vision) over the Ollama REST API.
/// Adapter pattern (GoF): adapts the Ollama <c>/api/chat</c> contract to <see cref="IVisionService"/>,
/// so the rest of the pipeline is unaware the description came from a local model rather than Azure.
/// <para>
/// Note: Ollama is image-to-text only. Text-to-image generation is unsupported by Ollama LLMs and
/// remains the responsibility of <see cref="IImageGenerationService"/> (Gemini).
/// </para>
/// </summary>
public sealed class OllamaVisionService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<OllamaVisionService> logger) : IVisionService
{
    private string Model => configuration[ConfigKeys.OllamaVisionModel] ?? "gemma4";

    public async Task<(string Description, IReadOnlyList<string> Tags, double ConfidenceScore, long ElapsedMs)>
        AnalyzeAsync(byte[] imageData, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        if (imageData.Length == 0)
            throw new ArgumentException("Image data cannot be empty", nameof(imageData));

        var model = Model;
        logger.LogInformation("Analyzing image via Ollama. Model={Model}, Size={Size} bytes", model, imageData.Length);
        var start = Stopwatch.GetTimestamp();

        // Ollama /api/chat accepts raw base64 (no data-URI prefix) in the message `images` array.
        var base64 = Convert.ToBase64String(imageData);

        const string prompt =
            "You are an image analysis service. Look at the image and respond with ONLY a JSON object " +
            "(no markdown, no prose) of the form " +
            "{\"description\": \"<one vivid sentence describing the scene>\", \"tags\": [\"tag1\", \"tag2\", ...]}. " +
            "Provide 5-12 concise lowercase tags for the salient objects, setting, colours and mood.";

        var payload = new
        {
            model,
            stream = false,
            format = "json",
            messages = new[]
            {
                new { role = "user", content = prompt, images = new[] { base64 } }
            }
        };

        var client = httpClientFactory.CreateClient("Ollama");
        using var resp = await client.PostAsJsonAsync("/api/chat", payload, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";

        var (description, tags) = ParseModelJson(content);

        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        logger.LogInformation("Ollama vision analysis complete in {Elapsed}ms. Tags={Count}", elapsed, tags.Count);

        // Local models don't emit a calibrated confidence; report 1.0 so downstream gating treats the
        // result as usable (the pipeline only uses this value for display).
        return (description, tags, 1.0, elapsed);
    }

    /// <summary>
    /// Parses the model's JSON reply, tolerating stray markdown fences or surrounding prose.
    /// Falls back to using the raw text as the description if it isn't valid JSON.
    /// </summary>
    private static (string Description, IReadOnlyList<string> Tags) ParseModelJson(string content)
    {
        var text = content.Trim();
        var first = text.IndexOf('{');
        var last = text.LastIndexOf('}');
        if (first >= 0 && last > first)
            text = text[first..(last + 1)];

        try
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            var description = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            var tags = new List<string>();
            if (root.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in t.EnumerateArray())
                {
                    var tag = item.GetString();
                    if (!string.IsNullOrWhiteSpace(tag)) tags.Add(tag.Trim());
                }
            }

            if (string.IsNullOrWhiteSpace(description))
                description = tags.Count > 0 ? $"An image of {string.Join(", ", tags.Take(8))}" : "No description available";

            return (description.Trim(), tags);
        }
        catch (JsonException)
        {
            var fallback = string.IsNullOrWhiteSpace(content) ? "No description available" : content.Trim();
            return (fallback, []);
        }
    }
}
