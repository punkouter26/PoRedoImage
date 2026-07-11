using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Application.Agents.StyleDirector;

/// <summary>
/// Agent 4 of the Style Director workflow. An LLM self-critique step that sanity-checks the refined
/// prompt (vagueness, style drift, missing subject preservation, no anti-watermark guard) and returns
/// a confidence score. A deterministic guard-enforcement pass runs on the final prompt in BOTH the AI
/// and fallback paths, so the img2img-critical guards are always present regardless of model output.
/// </summary>
/// <remarks>
/// Idea #1 — Agentic Style Director (step 4 of 4). The critic runs *after* the refiner so its
/// suggestions land in the final output the user sees.
/// </remarks>
public sealed class CriticAgent : IAgent<CriticInput, CriticOutput>
{
    private readonly IChatCompletionService _chat;
    private readonly ILogger<CriticAgent> _logger;

    public string Id => "critic";
    public string DisplayName => "Critic";
    public string IconClass => "bi-shield-check";

    public CriticAgent(IChatCompletionService chat, ILogger<CriticAgent> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    public async Task<AgentStepResult<CriticOutput>> RunAsync(CriticInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var sw = Stopwatch.StartNew();

        if (_chat.IsConfigured)
        {
            try
            {
                return await RunWithAiAsync(input, sw, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Critic AI path failed; falling back to heuristic.");
            }
        }

        return Heuristic(input, sw);
    }

    private async Task<AgentStepResult<CriticOutput>> RunWithAiAsync(CriticInput input, Stopwatch sw, CancellationToken ct)
    {
        const string system =
            "You are the Critic in an art-direction pipeline. Critique an image-generation prompt for "
            + "vagueness, subject preservation and an anti-watermark guard, fixing it if needed. Reply "
            + "with MINIFIED JSON only — no prose, no markdown code fences.";
        var user =
            $"Critique this image-to-image prompt: \"{input.Refined.Prompt}\". Respond as JSON: "
            + "{\"finalPrompt\":\"the improved prompt\",\"critique\":\"one or two sentences\","
            + "\"confidence\":0-100,\"suggestions\":[\"changes you made\"]}.";

        var result = await _chat.CompleteAsync(system, user, image: null, ct);
        var el = AgentJson.Parse(result.Content);

        var finalPrompt = AgentJson.Str(el, "finalPrompt", input.Refined.Prompt);
        var confidence = Math.Clamp(AgentJson.Int(el, "confidence", 85), 0, 100);
        var suggestions = AgentJson.StrArray(el, "suggestions");

        // Safety net: guarantee the img2img-critical guards regardless of what the model returned.
        finalPrompt = EnforceGuards(finalPrompt, suggestions);

        var critique = AgentJson.Str(el, "critique",
            suggestions.Count == 0 ? "Prompt is concrete and specific." : $"Adjusted {suggestions.Count} issue(s).");

        var output = new CriticOutput(finalPrompt, critique, confidence, suggestions);
        sw.Stop();

        _logger.LogInformation("Critic (AI) done in {Elapsed}ms. Confidence={Confidence}, Tokens={Tokens}",
            result.ElapsedMs, confidence, result.TokensUsed);

        return new AgentStepResult<CriticOutput>(output, Reasoning(
            $"Confidence: {confidence}/100. {critique}", result.ElapsedMs, result.TokensUsed));
    }

    private AgentStepResult<CriticOutput> Heuristic(CriticInput input, Stopwatch sw)
    {
        var suggestions = new List<string>();
        var confidence = 85;

        var prompt = input.Refined.Prompt;
        if (prompt.Length < 40) confidence -= 15;
        if (!prompt.Contains("Preserve", StringComparison.OrdinalIgnoreCase)) confidence -= 10;
        if (!prompt.Contains("watermark", StringComparison.OrdinalIgnoreCase) &&
            !prompt.Contains("text", StringComparison.OrdinalIgnoreCase)) confidence -= 5;

        prompt = EnforceGuards(prompt, suggestions);
        if (input.Refined.Prompt.Length < 40)
        {
            suggestions.Insert(0, "Prompt is too short — added more descriptive language.");
            prompt += " Detailed textures, balanced composition.";
        }
        confidence = Math.Clamp(confidence, 0, 100);

        var critique = suggestions.Count == 0
            ? "Prompt is concrete, guards are present, style is specific. Ship it."
            : $"Adjusted {suggestions.Count} issue(s): {string.Join(" ", suggestions)}";

        var output = new CriticOutput(prompt, critique, confidence, suggestions);
        sw.Stop();

        _logger.LogInformation("Critic (heuristic) done in {Elapsed}ms. Confidence={Confidence}, Adjustments={Count}",
            sw.ElapsedMilliseconds, confidence, suggestions.Count);

        return new AgentStepResult<CriticOutput>(output, Reasoning(
            $"Confidence: {confidence}/100. {critique}", sw.ElapsedMilliseconds, tokensUsed: null));
    }

    /// <summary>
    /// Appends the two img2img-critical guards (subject preservation, no watermark/text) when absent,
    /// recording each addition in <paramref name="suggestions"/>. Shared by both execution paths.
    /// </summary>
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

    private AgentReasoningEntry Reasoning(string summary, long elapsedMs, int? tokensUsed) => new(
        AgentId: Id,
        AgentDisplayName: DisplayName,
        IconClass: IconClass,
        Summary: summary,
        ElapsedMs: elapsedMs,
        TokensUsed: tokensUsed,
        Timestamp: DateTimeOffset.UtcNow,
        Activity: null);
}
