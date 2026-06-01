using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Application.Agents.StyleDirector;

/// <summary>
/// Orchestrates the four-agent Style Director chain. Heuristic-only agents today;
/// the workflow shape is identical to what a Microsoft Agent Framework
/// <c>SequentialWorkflow</c> would run, so swapping any agent for an MAF-backed
/// implementation is a one-line change.
/// </summary>
/// <remarks>
/// Idea #1 — Agentic Style Director. The full chain runs in &lt; 50ms in-process
/// (no AI calls in the heuristic implementation), so the reasoning trace shows
/// up in the UI instantly. When the agents are upgraded to use GPT-4o-nano, the
/// end-to-end latency target stays under 5s.
/// </remarks>
public sealed class StyleDirectorWorkflow
{
    private readonly VisionAnalystAgent _vision;
    private readonly StyleStrategistAgent _strategist;
    private readonly PromptRefinerAgent _refiner;
    private readonly CriticAgent _critic;
    private readonly ILogger<StyleDirectorWorkflow> _logger;

    public StyleDirectorWorkflow(
        VisionAnalystAgent vision,
        StyleStrategistAgent strategist,
        PromptRefinerAgent refiner,
        CriticAgent critic,
        ILogger<StyleDirectorWorkflow> logger)
    {
        _vision = vision;
        _strategist = strategist;
        _refiner = refiner;
        _critic = critic;
        _logger = logger;
    }

    /// <summary>
    /// Runs the four agents in sequence. We execute them directly rather than via
    /// the generic <see cref="SequentialAgentWorkflow"/> so we can preserve the
    /// strongly-typed pipeline state without <c>dynamic</c> casts — each agent has
    /// a different I/O type. The generic runner remains in the codebase for future
    /// migration to MAF, which carries that state natively.
/// </summary>
    public async Task<WorkflowResult<StyleDirectorResult>> RunAsync(
        VisionAnalystInput input,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var trace = new List<AgentReasoningEntry>(capacity: 4);

        VisionAnalystOutput vision;
        StyleStrategistOutput strategy;
        PromptRefinerOutput refined;
        CriticOutput decision;

        try
        {
            var step1 = await _vision.RunAsync(input, ct);
            trace.Add(step1.Reasoning);
            vision = step1.Output;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vision analyst failed");
            return new WorkflowResult<StyleDirectorResult>(false, null!, trace, (long)sw.ElapsedMilliseconds, ex.Message);
        }

        try
        {
            var step2 = await _strategist.RunAsync(new StyleStrategistInput(vision), ct);
            trace.Add(step2.Reasoning);
            strategy = step2.Output;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Style strategist failed");
            return new WorkflowResult<StyleDirectorResult>(false, null!, trace, (long)sw.ElapsedMilliseconds, ex.Message);
        }

        try
        {
            var step3 = await _refiner.RunAsync(new PromptRefinerInput(strategy), ct);
            trace.Add(step3.Reasoning);
            refined = step3.Output;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Prompt refiner failed");
            return new WorkflowResult<StyleDirectorResult>(false, null!, trace, (long)sw.ElapsedMilliseconds, ex.Message);
        }

        try
        {
            var step4 = await _critic.RunAsync(new CriticInput(refined), ct);
            trace.Add(step4.Reasoning);
            decision = step4.Output;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critic failed");
            return new WorkflowResult<StyleDirectorResult>(false, null!, trace, (long)sw.ElapsedMilliseconds, ex.Message);
        }

        var result = new StyleDirectorResult(decision, vision, strategy, refined);
        _logger.LogInformation("Style director workflow complete. Elapsed={Elapsed}ms, Confidence={Confidence}",
            sw.ElapsedMilliseconds, decision.Confidence);

        return new WorkflowResult<StyleDirectorResult>(
            Succeeded: true,
            Output: result,
            Reasoning: trace,
            ElapsedMs: (long)sw.ElapsedMilliseconds,
            ErrorMessage: null);
    }
}
