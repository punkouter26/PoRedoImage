using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Application.Agents.StyleDirector;

/// <summary>
/// Agent 3 of the Style Director workflow. Turns the Strategist's primary direction into a single,
/// concrete image-to-image prompt via an LLM (with a deterministic template fallback). The prompt
/// always preserves the subject's likeness and forbids watermarks/text.
/// </summary>
/// <remarks>
/// Idea #1 — Agentic Style Director (step 3 of 4).
/// </remarks>
public sealed class PromptRefinerAgent : IAgent<PromptRefinerInput, PromptRefinerOutput>
{
    private readonly IChatCompletionService _chat;
    private readonly ILogger<PromptRefinerAgent> _logger;

    public string Id => "prompt-refiner";
    public string DisplayName => "Prompt Refiner";
    public string IconClass => "bi-pencil-square";

    public PromptRefinerAgent(IChatCompletionService chat, ILogger<PromptRefinerAgent> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    public async Task<AgentStepResult<PromptRefinerOutput>> RunAsync(PromptRefinerInput input, CancellationToken ct = default)
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
                _logger.LogWarning(ex, "PromptRefiner AI path failed; falling back to heuristic.");
            }
        }

        return Heuristic(input, sw);
    }

    private async Task<AgentStepResult<PromptRefinerOutput>> RunWithAiAsync(
        PromptRefinerInput input, Stopwatch sw, CancellationToken ct)
    {
        var primary = input.Strategy.Directions.FirstOrDefault()
            ?? new StyleDirection("Editorial Portrait", "neutral", "soft light", "modern");

        const string system =
            "You are the Prompt Refiner in an art-direction pipeline — an expert image-generation prompt "
            + "engineer. Reply with MINIFIED JSON only — no prose, no markdown code fences.";
        var user =
            $"Primary style direction — name: {primary.Name}; palette: {primary.Palette}; technique: "
            + $"{primary.Technique}; era: {primary.ReferenceEra}. Write ONE concrete image-to-image prompt "
            + "(max 60 words) that applies this style while preserving the subject's facial features and "
            + "likeness, and explicitly forbids watermarks, text and logos. Respond as JSON: "
            + "{\"prompt\":\"the prompt\",\"whyThisPrompt\":\"one sentence rationale\"}.";

        var result = await _chat.CompleteAsync(system, user, image: null, ct);
        var el = AgentJson.Parse(result.Content);

        var prompt = AgentJson.Str(el, "prompt");
        if (string.IsNullOrWhiteSpace(prompt)) throw new FormatException("Refiner returned an empty prompt.");
        var why = AgentJson.Str(el, "whyThisPrompt",
            $"Applies the Strategist's primary direction ('{primary.Name}') with a subject-preservation guard.");

        var output = new PromptRefinerOutput(prompt, why);
        sw.Stop();

        _logger.LogInformation("PromptRefiner (AI) done in {Elapsed}ms. Words={Words}, Tokens={Tokens}",
            result.ElapsedMs, prompt.Split(' ').Length, result.TokensUsed);

        return new AgentStepResult<PromptRefinerOutput>(output, Reasoning(
            $"Refined into a single {prompt.Split(' ').Length}-word prompt.", result.ElapsedMs, result.TokensUsed));
    }

    private AgentStepResult<PromptRefinerOutput> Heuristic(PromptRefinerInput input, Stopwatch sw)
    {
        var primary = input.Strategy.Directions.FirstOrDefault()
            ?? new StyleDirection("Editorial Portrait", "neutral", "soft light", "modern");

        var prompt =
            $"Reimagine the subject as a {primary.ReferenceEra} {primary.Name} illustration. " +
            $"Use a {primary.Palette} palette, {primary.Technique}. " +
            "Preserve the subject's facial features and likeness. " +
            "Studio quality, sharp focus, no watermarks, no text.";

        var why =
            $"Combines the Strategist's primary direction ('{primary.Name}') with the subject " +
            "preservation guard required for img2img.";

        var output = new PromptRefinerOutput(prompt, why);
        sw.Stop();

        _logger.LogInformation("PromptRefiner (heuristic) done in {Elapsed}ms. Words={Words}",
            sw.ElapsedMilliseconds, prompt.Split(' ').Length);

        return new AgentStepResult<PromptRefinerOutput>(output, Reasoning(
            $"Refined into a single {prompt.Split(' ').Length}-word prompt.", sw.ElapsedMilliseconds, tokensUsed: null));
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
