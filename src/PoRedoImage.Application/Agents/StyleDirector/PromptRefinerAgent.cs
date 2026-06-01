using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Application.Agents.StyleDirector;

/// <summary>
/// Agent 3 of the Style Director workflow. Takes the Strategist's directions
/// and turns them into a single, renderable Imagen 3 prompt. Heuristic — no AI
/// call — but the structure mirrors a real prompt-refinement chain (the kind
/// of work a fine-tuned GPT would do in production).
/// </summary>
/// <remarks>
/// Idea #1 — Agentic Style Director (step 3 of 4).
/// </remarks>
public sealed class PromptRefinerAgent : IAgent<PromptRefinerInput, PromptRefinerOutput>
{
    private readonly ILogger<PromptRefinerAgent> _logger;

    public string Id => "prompt-refiner";
    public string DisplayName => "Prompt Refiner";
    public string IconClass => "bi-pencil-square";

    public PromptRefinerAgent(ILogger<PromptRefinerAgent> logger) => _logger = logger;

    public Task<AgentStepResult<PromptRefinerOutput>> RunAsync(PromptRefinerInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var sw = Stopwatch.StartNew();

        // Pick the highest-priority direction; in production this is where a model
        // would do the heavy lifting (clarity, de-duplication, factuality checks).
        var primary = input.Strategy.Directions.FirstOrDefault()
            ?? new StyleDirection("Editorial Portrait", "neutral", "soft light", "modern");

        // Build a concrete Imagen 3 prompt: subject + palette + technique + era + safety suffix.
        var prompt =
            $"Reimagine the subject as a {primary.ReferenceEra} {primary.Name} illustration. " +
            $"Use a {primary.Palette} palette, {primary.Technique}. " +
            $"Preserve the subject's facial features and likeness. " +
            $"Studio quality, sharp focus, no watermarks, no text.";

        var why =
            $"Combines the Strategist's primary direction ('{primary.Name}') with the subject " +
            "preservation guard required for img2img.";

        var output = new PromptRefinerOutput(prompt, why);
        sw.Stop();

        var reasoning = new AgentReasoningEntry(
            AgentId: Id,
            AgentDisplayName: DisplayName,
            IconClass: IconClass,
            Summary: $"Refined into a single {prompt.Split(' ').Length}-word prompt.",
            ElapsedMs: (long)sw.ElapsedMilliseconds,
            TokensUsed: null,
            Timestamp: DateTimeOffset.UtcNow,
            Activity: null);

        _logger.LogInformation("PromptRefiner done in {Elapsed}ms. Words={Words}",
            reasoning.ElapsedMs, prompt.Split(' ').Length);

        return Task.FromResult(new AgentStepResult<PromptRefinerOutput>(output, reasoning));
    }
}
