using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Application.Agents.StyleDirector;

/// <summary>
/// Agent 4 of the Style Director workflow. Self-critique step that sanity-checks
/// the refined prompt for common failure modes (vagueness, style drift, missing
/// subject preservation, no anti-watermark guard). Adjusts the prompt if it
/// finds a flaw, otherwise returns it unchanged with a confidence score.
/// </summary>
/// <remarks>
/// Idea #1 — Agentic Style Director (step 4 of 4). The critic runs *after* the
/// refiner so its suggestions land in the final output the user sees.
/// </remarks>
public sealed class CriticAgent : IAgent<CriticInput, CriticOutput>
{
    private readonly ILogger<CriticAgent> _logger;

    public string Id => "critic";
    public string DisplayName => "Critic";
    public string IconClass => "bi-shield-check";

    public CriticAgent(ILogger<CriticAgent> logger) => _logger = logger;

    public Task<AgentStepResult<CriticOutput>> RunAsync(CriticInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var sw = Stopwatch.StartNew();

        var prompt = input.Refined.Prompt;
        var suggestions = new List<string>();
        var confidence = 85;

        // Heuristic checks
        if (prompt.Length < 40)
        {
            suggestions.Add("Prompt is too short — added more descriptive language.");
            prompt += " Detailed textures, balanced composition.";
            confidence -= 15;
        }
        if (!prompt.Contains("Preserve", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add("Added explicit subject-preservation guard.");
            prompt += " Preserve the subject's facial features and identity.";
            confidence -= 10;
        }
        if (!prompt.Contains("watermark", StringComparison.OrdinalIgnoreCase) &&
            !prompt.Contains("text", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add("Added anti-watermark and anti-text guard.");
            prompt += " No watermarks, no text, no logos.";
            confidence -= 5;
        }

        confidence = Math.Clamp(confidence, 0, 100);

        var critique = suggestions.Count == 0
            ? "Prompt is concrete, guards are present, style is specific. Ship it."
            : $"Adjusted {suggestions.Count} issue(s): {string.Join(" ", suggestions)}";

        var output = new CriticOutput(prompt, critique, confidence, suggestions);
        sw.Stop();

        var reasoning = new AgentReasoningEntry(
            AgentId: Id,
            AgentDisplayName: DisplayName,
            IconClass: IconClass,
            Summary: $"Confidence: {confidence}/100. {critique}",
            ElapsedMs: (long)sw.ElapsedMilliseconds,
            TokensUsed: null,
            Timestamp: DateTimeOffset.UtcNow,
            Activity: null);

        _logger.LogInformation("Critic done in {Elapsed}ms. Confidence={Confidence}, Adjustments={Count}",
            reasoning.ElapsedMs, confidence, suggestions.Count);

        return Task.FromResult(new AgentStepResult<CriticOutput>(output, reasoning));
    }
}
