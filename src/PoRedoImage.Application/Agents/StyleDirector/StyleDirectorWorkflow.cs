using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Application.Agents.StyleDirector;

/// <summary>
/// Orchestrates the Style Director prompt synthesis workflow.
/// Evaluates visual cues, determines matching artistic styles, synthesizes a refined prompt,
/// and applies deterministic quality guards in a streamlined, explainable pipeline.
/// </summary>
public sealed class StyleDirectorWorkflow
{
    private readonly IChatCompletionService _chat;
    private readonly ILogger<StyleDirectorWorkflow> _logger;

    public StyleDirectorWorkflow(
        IChatCompletionService chat,
        ILogger<StyleDirectorWorkflow> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    public async Task<WorkflowResult<StyleDirectorResult>> RunAsync(
        VisionAnalystInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var totalSw = Stopwatch.StartNew();
        var trace = new List<AgentReasoningEntry>(capacity: 4);

        try
        {
            if (_chat.IsConfigured && input.ImageBytes is { Length: > 0 })
            {
                try
                {
                    return await RunWithAiAsync(input, totalSw, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AI-driven Style Director failed; falling back to heuristic evaluation.");
                }
            }

            return RunHeuristic(input, totalSw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Style Director workflow failed");
            return new WorkflowResult<StyleDirectorResult>(
                Succeeded: false,
                Output: null!,
                Reasoning: trace,
                ElapsedMs: totalSw.ElapsedMilliseconds,
                ErrorMessage: ex.Message);
        }
    }

    private async Task<WorkflowResult<StyleDirectorResult>> RunWithAiAsync(
        VisionAnalystInput input,
        Stopwatch totalSw,
        CancellationToken ct)
    {
        var trace = new List<AgentReasoningEntry>(capacity: 4);
        var tags = input.DetectedTags ?? [];

        // Step 1: Vision Analysis & Strategy Synthesis via Chat Completion
        const string system =
            "You are an expert AI Art Director. Given an image and tag cues, analyze the subject and mood, "
            + "propose style directions, compose an optimized image-generation prompt, and critique it. "
            + "Respond with MINIFIED JSON only (no markdown, no code fences) with structure: "
            + "{\"subject\":\"...\",\"mood\":\"...\",\"themes\":[\"...\"],"
            + "\"directions\":[{\"name\":\"...\",\"palette\":\"...\",\"technique\":\"...\",\"referenceEra\":\"...\"}],"
            + "\"strategyRationale\":\"...\",\"prompt\":\"...\",\"whyThisPrompt\":\"...\",\"confidence\":85,\"suggestions\":[\"...\"]}";

        var userPrompt =
            $"Analyze this photo for art direction. Detected tags: {(tags.Count > 0 ? string.Join(", ", tags) : "none")}. "
            + "Generate a cohesive artistic transformation brief.";

        var sw = Stopwatch.StartNew();
        var chatResult = await _chat.CompleteAsync(system, userPrompt, input.ImageBytes, ct);
        sw.Stop();

        var parsed = ParseAiResponse(chatResult.Content, tags);

        // Enforce safety & preservation guards deterministically on prompt
        var suggestions = new List<string>(parsed.Suggestions);
        var finalPrompt = EnforceGuards(parsed.Prompt, suggestions);
        var confidence = Math.Clamp(parsed.Confidence, 50, 100);

        var vision = new VisionAnalystOutput(parsed.Subject, parsed.Mood, parsed.Themes);
        var strategy = new StyleStrategistOutput(parsed.Directions, parsed.StrategyRationale);
        var refined = new PromptRefinerOutput(finalPrompt, parsed.WhyThisPrompt, confidence);
        var decision = new CriticOutput(
            finalPrompt,
            suggestions.Count == 0 ? "Prompt is concrete, specific, and guarded." : $"Adjusted {suggestions.Count} issue(s).",
            confidence,
            suggestions);

        var stepElapsed = Math.Max(1, sw.ElapsedMilliseconds / 4);

        trace.Add(new AgentReasoningEntry(
            "vision-analyst", "Vision Analyst", "bi-eye",
            $"Subject: {vision.Subject} | Mood: {vision.Mood}",
            stepElapsed, chatResult.TokensUsed / 4, DateTimeOffset.UtcNow, null));

        trace.Add(new AgentReasoningEntry(
            "style-strategist", "Style Strategist", "bi-palette",
            $"Proposed {strategy.Directions.Count} directions: {string.Join(", ", strategy.Directions.Select(d => d.Name))}",
            stepElapsed, chatResult.TokensUsed / 4, DateTimeOffset.UtcNow, null));

        trace.Add(new AgentReasoningEntry(
            "prompt-refiner", "Prompt Refiner", "bi-pencil-square",
            refined.WhyThisPrompt,
            stepElapsed, chatResult.TokensUsed / 4, DateTimeOffset.UtcNow, null));

        trace.Add(new AgentReasoningEntry(
            "critic", "Critic", "bi-shield-check",
            $"Confidence: {confidence}/100. {decision.Critique}",
            stepElapsed, chatResult.TokensUsed / 4, DateTimeOffset.UtcNow, null));

        totalSw.Stop();
        var result = new StyleDirectorResult(decision, vision, strategy, refined);

        return new WorkflowResult<StyleDirectorResult>(
            Succeeded: true,
            Output: result,
            Reasoning: trace,
            ElapsedMs: totalSw.ElapsedMilliseconds,
            ErrorMessage: null);
    }

    private WorkflowResult<StyleDirectorResult> RunHeuristic(
        VisionAnalystInput input,
        Stopwatch totalSw)
    {
        var trace = new List<AgentReasoningEntry>(capacity: 4);
        var tags = input.DetectedTags ?? [];

        // 1. Vision Analysis Heuristic
        var subject = tags.Count > 0 ? tags[0] : "Uploaded scene";
        var isPerson = tags.Any(t => t.Contains("person", StringComparison.OrdinalIgnoreCase) ||
                                     t.Contains("man", StringComparison.OrdinalIgnoreCase) ||
                                     t.Contains("woman", StringComparison.OrdinalIgnoreCase));
        var mood = isPerson ? "expressive and human" : "atmospheric and composed";
        var themes = tags.Take(3).ToList();
        if (themes.Count == 0) themes.Add("artistic study");

        var vision = new VisionAnalystOutput(subject, mood, themes);
        trace.Add(new AgentReasoningEntry(
            "vision-analyst", "Vision Analyst", "bi-eye",
            $"Subject: {subject} | Mood: {mood}",
            1, null, DateTimeOffset.UtcNow, null, "Rule-based fallback — chat service not configured."));

        // 2. Style Strategy Heuristic
        var directions = new List<StyleDirection>
        {
            new("Studio Ghibli Watercolor", "lush greens, soft sky blues, warm golden highlights", "painterly watercolor, delicate linework, soft lighting", "1990s"),
            new("Cyberpunk Neon", "deep cyan, hot magenta, dark obsidian shadows", "high contrast, volumetric neon glow, wet-surface reflections", "2080s"),
            new("Vintage Oil Impressionism", "rich ochre, ultramarine, burnt sienna", "thick impasto brushstrokes, textured canvas, visible paint marks", "1880s")
        };
        var strategy = new StyleStrategistOutput(directions, "Curated multi-era aesthetic directions tailored to scene characteristics.");
        trace.Add(new AgentReasoningEntry(
            "style-strategist", "Style Strategist", "bi-palette",
            $"Selected {directions.Count} complementary styles: {string.Join(", ", directions.Select(d => d.Name))}",
            1, null, DateTimeOffset.UtcNow, null, "Rule-based fallback."));

        // 3. Prompt Refinement
        var basePrompt = $"A high-detail artistic interpretation of {subject} in the style of Studio Ghibli Watercolor, featuring lush greens, soft lighting, and delicate linework.";
        var why = "Balanced watercolor palette preserves core scene composition while giving it timeless warmth.";
        var refined = new PromptRefinerOutput(basePrompt, why, 85);
        trace.Add(new AgentReasoningEntry(
            "prompt-refiner", "Prompt Refiner", "bi-pencil-square",
            why, 1, null, DateTimeOffset.UtcNow, null, "Rule-based fallback."));

        // 4. Critic Guard Enforcement
        var suggestions = new List<string>();
        var finalPrompt = EnforceGuards(basePrompt, suggestions);
        var decision = new CriticOutput(
            finalPrompt,
            "Prompt is concrete, specific, and guarded. Ready for image generation.",
            85,
            suggestions);

        trace.Add(new AgentReasoningEntry(
            "critic", "Critic", "bi-shield-check",
            $"Confidence: 85/100. {decision.Critique}",
            1, null, DateTimeOffset.UtcNow, null, "Rule-based fallback."));

        totalSw.Stop();
        var result = new StyleDirectorResult(decision, vision, strategy, refined);

        return new WorkflowResult<StyleDirectorResult>(
            Succeeded: true,
            Output: result,
            Reasoning: trace,
            ElapsedMs: totalSw.ElapsedMilliseconds,
            ErrorMessage: null);
    }

    private static string EnforceGuards(string prompt, List<string> suggestions)
    {
        if (!prompt.Contains("Preserve", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add("Added explicit subject-preservation guard.");
            prompt += " Preserve the subject's facial features and identity.";
        }
        if (!prompt.Contains("watermark", StringComparison.OrdinalIgnoreCase) &&
            !prompt.Contains("no text", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add("Added anti-watermark and anti-text guard.");
            prompt += " No watermarks, no text, no logos.";
        }
        return prompt;
    }

    private static ParsedAiResponse ParseAiResponse(string json, IReadOnlyList<string> fallbackTags)
    {
        try
        {
            // Strip markdown code fences if present
            var clean = json.Trim();
            if (clean.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLineEnd = clean.IndexOf('\n');
                var lastFence = clean.LastIndexOf("```", StringComparison.Ordinal);
                if (firstLineEnd >= 0 && lastFence > firstLineEnd)
                    clean = clean[(firstLineEnd + 1)..lastFence].Trim();
            }

            using var doc = JsonDocument.Parse(clean);
            var root = doc.RootElement;

            var subject = root.TryGetProperty("subject", out var s) ? s.GetString() ?? "Subject" : "Subject";
            var mood = root.TryGetProperty("mood", out var m) ? m.GetString() ?? "Artistic" : "Artistic";
            var themes = root.TryGetProperty("themes", out var th) && th.ValueKind == JsonValueKind.Array
                ? th.EnumerateArray().Select(e => e.GetString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList()
                : fallbackTags.Take(3).ToList();

            var directions = new List<StyleDirection>();
            if (root.TryGetProperty("directions", out var d) && d.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in d.EnumerateArray())
                {
                    directions.Add(new StyleDirection(
                        item.TryGetProperty("name", out var n) ? n.GetString() ?? "Artistic" : "Artistic",
                        item.TryGetProperty("palette", out var p) ? p.GetString() ?? "Vibrant" : "Vibrant",
                        item.TryGetProperty("technique", out var t) ? t.GetString() ?? "Painterly" : "Painterly",
                        item.TryGetProperty("referenceEra", out var r) ? r.GetString() ?? "Contemporary" : "Contemporary"));
                }
            }

            if (directions.Count == 0)
            {
                directions.Add(new StyleDirection("Studio Ghibli Watercolor", "natural tones", "watercolor", "1990s"));
            }

            var strategyRationale = root.TryGetProperty("strategyRationale", out var sr) ? sr.GetString() ?? "" : "";
            var prompt = root.TryGetProperty("prompt", out var pr) ? pr.GetString() ?? $"Artistic render of {subject}" : $"Artistic render of {subject}";
            var why = root.TryGetProperty("whyThisPrompt", out var wp) ? wp.GetString() ?? "Optimized visual balance." : "Optimized visual balance.";
            var confidence = root.TryGetProperty("confidence", out var cf) && cf.TryGetInt32(out var ci) ? ci : 85;

            var suggestions = root.TryGetProperty("suggestions", out var sg) && sg.ValueKind == JsonValueKind.Array
                ? sg.EnumerateArray().Select(e => e.GetString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList()
                : [];

            return new ParsedAiResponse(subject, mood, themes, directions, strategyRationale, prompt, why, confidence, suggestions);
        }
        catch
        {
            var fallbackSubject = fallbackTags.Count > 0 ? fallbackTags[0] : "Scene";
            return new ParsedAiResponse(
                fallbackSubject,
                "dynamic",
                fallbackTags.Take(3).ToList(),
                [new StyleDirection("Studio Ghibli Watercolor", "lush tones", "watercolor", "1990s")],
                "Balanced aesthetic transformation.",
                $"An artistic watercolor rendering of {fallbackSubject}.",
                "Clear composition preserving original scene.",
                85,
                []);
        }
    }

    private sealed record ParsedAiResponse(
        string Subject,
        string Mood,
        IReadOnlyList<string> Themes,
        IReadOnlyList<StyleDirection> Directions,
        string StrategyRationale,
        string Prompt,
        string WhyThisPrompt,
        int Confidence,
        IReadOnlyList<string> Suggestions);
}

