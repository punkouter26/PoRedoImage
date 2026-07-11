using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Application.Agents.StyleDirector;

/// <summary>
/// Agent 1 of the Style Director workflow. Studies the uploaded image (via a vision-language model)
/// and produces a higher-level subject/mood/themes interpretation for the Style Strategist. When no
/// chat provider is configured (or the call fails) it falls back to a heuristic over the Computer
/// Vision tags, so the workflow always produces a usable result.
/// </summary>
/// <remarks>
/// Idea #1 — Agentic Style Director (step 1 of 4).
/// </remarks>
public sealed class VisionAnalystAgent : IAgent<VisionAnalystInput, VisionAnalystOutput>
{
    private readonly IChatCompletionService _chat;
    private readonly ILogger<VisionAnalystAgent> _logger;

    public string Id => "vision-analyst";
    public string DisplayName => "Vision Analyst";
    public string IconClass => "bi-eye";

    public VisionAnalystAgent(IChatCompletionService chat, ILogger<VisionAnalystAgent> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    public async Task<AgentStepResult<VisionAnalystOutput>> RunAsync(VisionAnalystInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var sw = Stopwatch.StartNew();

        if (_chat.IsConfigured && input.ImageBytes is { Length: > 0 })
        {
            try
            {
                return await RunWithAiAsync(input, sw, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VisionAnalyst AI path failed; falling back to heuristic.");
            }
        }

        return Heuristic(input, sw);
    }

    private async Task<AgentStepResult<VisionAnalystOutput>> RunWithAiAsync(
        VisionAnalystInput input, Stopwatch sw, CancellationToken ct)
    {
        var tags = input.DetectedTags ?? [];
        const string system =
            "You are the Vision Analyst in an art-direction pipeline. Study the image and reply with "
            + "MINIFIED JSON only — no prose, no markdown code fences.";
        var user =
            "Analyze this photo for an art-restyling brief. Detected tags (hints, may be empty): "
            + $"{(tags.Count > 0 ? string.Join(", ", tags) : "none")}. "
            + "Respond as JSON: {\"subject\":\"concise description of the main subject\","
            + "\"mood\":\"single evocative word\",\"themes\":[\"3-5 short theme words\"]}.";

        var result = await _chat.CompleteAsync(system, user, input.ImageBytes, ct);
        var el = AgentJson.Parse(result.Content);

        var subject = AgentJson.Str(el, "subject", tags.Count > 0 ? string.Join(", ", tags.Take(3)) : "scene");
        var mood = AgentJson.Str(el, "mood", "neutral");
        var themes = AgentJson.StrArray(el, "themes");
        if (themes.Count == 0) themes.Add("portrait");

        var output = new VisionAnalystOutput(subject, mood, themes.Take(5).ToList());
        sw.Stop();

        _logger.LogInformation("VisionAnalyst (AI) done in {Elapsed}ms. Subject='{Subject}', Mood='{Mood}', Tokens={Tokens}",
            result.ElapsedMs, subject, mood, result.TokensUsed);

        return new AgentStepResult<VisionAnalystOutput>(output, Reasoning(
            $"Subject: {subject} · Mood: {mood} · Themes: {string.Join(", ", themes)}",
            result.ElapsedMs, result.TokensUsed));
    }

    private AgentStepResult<VisionAnalystOutput> Heuristic(VisionAnalystInput input, Stopwatch sw)
    {
        // Tolerate null/empty tag sets — the workflow can still produce a useful result.
        var tags = input.DetectedTags ?? [];

        var subject = tags.Count > 0 ? string.Join(", ", tags.Take(3)) : "scene";
        var mood = InferMood(tags);
        var themes = tags
            .Where(t => t.Length > 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        if (themes.Count == 0) themes.Add("portrait");

        var output = new VisionAnalystOutput(subject, mood, themes);
        sw.Stop();

        _logger.LogInformation("VisionAnalyst (heuristic) done in {Elapsed}ms. Subject='{Subject}', Mood='{Mood}'",
            sw.ElapsedMilliseconds, subject, mood);

        return new AgentStepResult<VisionAnalystOutput>(output, Reasoning(
            $"Subject: {subject} · Mood: {mood} · Themes: {string.Join(", ", themes)}",
            sw.ElapsedMilliseconds, tokensUsed: null));
    }

    private AgentReasoningEntry Reasoning(string summary, long elapsedMs, int? tokensUsed) => new(
        AgentId: Id,
        AgentDisplayName: DisplayName,
        IconClass: IconClass,
        Summary: summary,
        ElapsedMs: elapsedMs,
        TokensUsed: tokensUsed,
        Timestamp: DateTimeOffset.UtcNow,
        Activity: null);

    private static string InferMood(IReadOnlyList<string> tags)
    {
        var tagSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
        if (tagSet.Overlaps(["outdoor", "sky", "mountain", "beach", "forest", "sunset"])) return "serene";
        if (tagSet.Overlaps(["night", "dark", "neon", "city", "rain"])) return "moody";
        if (tagSet.Overlaps(["person", "people", "face", "portrait", "smile"])) return "candid";
        if (tagSet.Overlaps(["dog", "cat", "animal", "puppy", "kitten"])) return "playful";
        if (tagSet.Overlaps(["food", "meal", "drink", "cake"])) return "indulgent";
        return "neutral";
    }
}
