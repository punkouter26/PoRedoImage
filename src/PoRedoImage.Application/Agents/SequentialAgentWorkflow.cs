using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Application.Agents;

/// <summary>
/// One link in a sequential chain. A <see cref="SequentialAgentWorkflow{TIn,TOut}"/>
/// is a chain of <see cref="AgentLink{TIn,TOut}"/>s — each runs after the previous,
/// passing its output as the next step's input.
/// </summary>
public sealed record AgentLink<TIn, TOut>(IAgent<TIn, TOut> Agent, string? StepName = null);

/// <summary>
/// Runs a chain of agents in order, aggregating reasoning entries and timing.
/// Equivalent to the Microsoft Agent Framework's "Sequential Workflow" pattern —
/// the shape is intentionally similar so the implementation can be migrated to
/// <c>Microsoft.Agents.AI.Workflows.SequentialWorkflow</c> when the package GA's.
/// </summary>
/// <remarks>
/// Idea #1 — Agentic Style Director. Each step is wrapped in an OpenTelemetry
/// <see cref="Activity"/> so the trace shows up in App Insights with the agent
/// name, input size, and elapsed time — all the data you need to debug a chain.
/// </remarks>
public sealed class SequentialAgentWorkflow
{
    private readonly ILogger<SequentialAgentWorkflow> _logger;

    public SequentialAgentWorkflow(ILogger<SequentialAgentWorkflow> logger) => _logger = logger;

    /// <summary>
    /// Runs the supplied chain. If a step throws, the chain halts and the
    /// <see cref="WorkflowResult{TOut}.Succeeded"/> flag is set to false; the
    /// partial reasoning entries are still returned for the UI.
    /// </summary>
    public async Task<WorkflowResult<TOut>> RunAsync<TIn, TOut>(
        TIn input,
        IReadOnlyList<AgentLink<object, object>> chain,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(chain);

        var sw = Stopwatch.StartNew();
        var reasoning = new List<AgentReasoningEntry>(capacity: chain.Count);
        object current = input!;

        using var workflowActivity = new Activity("agent-workflow").Start();
        workflowActivity?.SetTag("workflow.steps", chain.Count);

        foreach (var (link, idx) in chain.Select((l, i) => (l, i)))
        {
            using var stepActivity = new Activity($"agent-step.{link.Agent.Id}").Start();
            stepActivity?.SetTag("agent.id", link.Agent.Id);
            stepActivity?.SetTag("agent.step", idx);

            try
            {
                var step = await link.Agent.RunAsync(current, ct);
                reasoning.Add(step.Reasoning with { Activity = stepActivity });
                current = step.Output!;
                _logger.LogInformation("Workflow step {Index}/{Total} ({Agent}) done in {Elapsed}ms",
                    idx + 1, chain.Count, link.Agent.Id, step.Reasoning.ElapsedMs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow step {Index} ({Agent}) failed", idx, link.Agent.Id);
                stepActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return new WorkflowResult<TOut>(
                    Succeeded: false,
                    Output: default!,
                    Reasoning: reasoning,
                    ElapsedMs: (long)sw.ElapsedMilliseconds,
                    ErrorMessage: ex.Message);
            }
        }

        return new WorkflowResult<TOut>(
            Succeeded: true,
            Output: (TOut)current,
            Reasoning: reasoning,
            ElapsedMs: (long)sw.ElapsedMilliseconds,
            ErrorMessage: null);
    }
}

/// <summary>Aggregate result of a workflow run — output + the full reasoning trace.</summary>
public sealed record WorkflowResult<TOut>(
    bool Succeeded,
    TOut Output,
    IReadOnlyList<AgentReasoningEntry> Reasoning,
    long ElapsedMs,
    string? ErrorMessage);
