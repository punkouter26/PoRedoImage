using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Application.Agents.StyleDirector;

/// <summary>
/// Agent 1 of the Style Director workflow. Given raw image bytes + raw CV tags,
/// produces a higher-level subject/mood/themes interpretation. Heuristic-only
/// (no AI call) — keeps the workflow fast and free, while still giving the
/// Style Strategist something richer than bare CV tags to work with.
/// </summary>
/// <remarks>
/// Idea #1 — Agentic Style Director (step 1 of 4).
/// </remarks>
public sealed class VisionAnalystAgent : IAgent<VisionAnalystInput, VisionAnalystOutput>
{
    private readonly ILogger<VisionAnalystAgent> _logger;

    public string Id => "vision-analyst";
    public string DisplayName => "Vision Analyst";
    public string IconClass => "bi-eye";

    public VisionAnalystAgent(ILogger<VisionAnalystAgent> logger) => _logger = logger;

    public Task<AgentStepResult<VisionAnalystOutput>> RunAsync(VisionAnalystInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var sw = Stopwatch.StartNew();

        // Tolerate null/empty tag sets — the workflow can still produce a useful result.
        var tags = input.DetectedTags ?? [];

        // Subject: take the first 3 tags as the focal subject, fall back to "scene".
        var subject = tags.Count > 0
            ? string.Join(", ", tags.Take(3))
            : "scene";

        // Mood: heuristic — outdoor + nature = serene; people = candid; night = moody.
        var mood = InferMood(tags);

        // Themes: unique 3-5 most "evocative" tags.
        var themes = tags
            .Where(t => t.Length > 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        if (themes.Count == 0) themes.Add("portrait");

        var output = new VisionAnalystOutput(subject, mood, themes);
        sw.Stop();

        var reasoning = new AgentReasoningEntry(
            AgentId: Id,
            AgentDisplayName: DisplayName,
            IconClass: IconClass,
            Summary: $"Subject: {subject} · Mood: {mood} · Themes: {string.Join(", ", themes)}",
            ElapsedMs: (long)sw.ElapsedMilliseconds,
            TokensUsed: null,
            Timestamp: DateTimeOffset.UtcNow,
            Activity: null);

        _logger.LogInformation("VisionAnalyst done in {Elapsed}ms. Subject='{Subject}', Mood='{Mood}'",
            reasoning.ElapsedMs, subject, mood);

        return Task.FromResult(new AgentStepResult<VisionAnalystOutput>(output, reasoning));
    }

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
