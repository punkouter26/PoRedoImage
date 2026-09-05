using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Vision analysis performed by the chat deployment in a single call, returning a real caption and
/// a tag list together.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The Azure Computer Vision path asks for <c>Caption|Tags</c> on every
/// request, but Caption is region-limited and the configured resource does not have it — so
/// <c>AzureVisionService</c> falls back, on <em>every single call</em>, to joining its top eight
/// tags into "A photo showing person, clothing, food, man, indoor, fast food, wall, meal". That is
/// a keyword list, not a description. Everything downstream then works around it: the enhancement
/// step has to invent detail, meme captions are written from bare nouns, and the Rap Roast slice
/// grew an entire <c>SceneDescriber</c> whose job is to compensate.
/// </para>
/// <para>
/// One vision-model call replaces that pair — caption and tags from a model that actually looked at
/// the image, in the same round trip. It costs tokens where CV cost a per-image fee, and for the
/// nano-class deployment on a downscaled image the difference is small; the description is
/// categorically better.
/// </para>
/// <para>
/// It does NOT replace <see cref="ISceneDetailProvider"/>. OCR stays with Computer Vision on
/// purpose: a language model will confidently invent the text on a sign it cannot read, and that is
/// the one thing in this pipeline that has to be ground truth.
/// </para>
/// </remarks>
public sealed class OpenAiVisionService(
    IChatCompletionService chat,
    ILogger<OpenAiVisionService> logger) : IVisionService
{
    private const string SystemPrompt =
        "You are an image analyst. Look at the image and reply with MINIFIED JSON only — no prose, "
        + "no markdown fences. Schema: {\"description\":\"one vivid, specific sentence describing "
        + "what is happening\",\"tags\":[\"8-14 lowercase single-word or two-word labels for the "
        + "objects, setting and activity present\"]}. "
        + "Report only what is visible; do not invent. Do not describe or infer race, ethnicity, "
        + "skin tone, body size or weight, age, disability, or attractiveness.";

    private const string UserPrompt = "Analyze this image now.";

    public async Task<(string Description, IReadOnlyList<string> Tags, double ConfidenceScore, long ElapsedMs, string? FallbackReason)>
        AnalyzeAsync(byte[] imageData, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        if (imageData.Length == 0)
            throw new ArgumentException("Image data cannot be empty", nameof(imageData));

        if (!chat.IsConfigured)
            throw new InvalidOperationException(
                "OpenAI vision analysis is not configured. Set OpenAI:Endpoint (and OpenAI:Key, "
                + "unless managed identity is in use) via Key Vault, or select Azure Computer Vision.");

        var start = Stopwatch.GetTimestamp();
        var result = await chat.CompleteAsync(SystemPrompt, UserPrompt, imageData, ct);
        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        var (description, tags) = Parse(result.Content);

        if (string.IsNullOrWhiteSpace(description))
        {
            // An empty completion is the shape a content-filter refusal takes. Surface it as a
            // throw rather than a bland string: this backend was explicitly chosen for its
            // description quality, and silently returning nothing is the degradation the
            // architecture notes warn about.
            throw new InvalidOperationException(
                "The vision model returned no usable analysis for this image.");
        }

        logger.LogInformation(
            "OpenAI vision analysis in {Elapsed}ms. Tags={Count}, Tokens={Tokens}",
            elapsed, tags.Count, result.TokensUsed);

        // Confidence is reported as 1.0 rather than fabricated from nothing: a chat model emits no
        // calibrated score, and inventing one would put a number in the UI that means nothing.
        // OllamaVisionService reports the same for the same reason.
        return (description, tags, 1.0, elapsed, null);
    }

    private static (string Description, IReadOnlyList<string> Tags) Parse(string content)
    {
        var text = content.Trim();

        // Some deployments still fence their JSON despite the instruction.
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0) text = text[(firstNewline + 1)..];
            if (text.EndsWith("```", StringComparison.Ordinal)) text = text[..^3];
            text = text.Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            var description = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";

            var tags = new List<string>();
            if (root.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in t.EnumerateArray())
                {
                    var tag = item.GetString()?.Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(tag)) tags.Add(tag);
                }
            }

            return (description.Trim(), tags.Distinct().Take(16).ToList());
        }
        catch (JsonException)
        {
            // Malformed JSON still contains a usable sentence more often than not, and a roast built
            // from prose beats one built from nothing. Tags are lost; callers tolerate an empty list.
            return (text, []);
        }
    }
}
