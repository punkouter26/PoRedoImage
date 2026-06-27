using System.Diagnostics;

namespace PoRedoImage.Application.Agents;

/// <summary>
/// One step in a multi-agent workflow. The agent receives a typed input and produces
/// a typed output, plus a human-readable reasoning entry that the UI can surface.
/// </summary>
/// <remarks>
/// Idea #1 — Agentic Style Director. The interface is intentionally lightweight
/// so it can be hosted today inside the app and later swapped for a Microsoft
/// Agent Framework agent with no contract change.
/// </remarks>
public interface IAgent<TIn, TOut>
{
    /// <summary>Stable id used in the reasoning trace (e.g., "vision-analyst").</summary>
    string Id { get; }

    /// <summary>Display name shown in the UI (e.g., "Vision Analyst").</summary>
    string DisplayName { get; }

    /// <summary>Icon for the UI — a bootstrap-icons class name.</summary>
    string IconClass { get; }

    /// <summary>Run the agent synchronously; throw to fail the workflow step.</summary>
    Task<AgentStepResult<TOut>> RunAsync(TIn input, CancellationToken ct = default);
}

/// <summary>Result wrapper that pairs the agent's output with its reasoning entry.</summary>
public sealed record AgentStepResult<T>(T Output, AgentReasoningEntry Reasoning);

/// <summary>
/// A single line in the workflow's reasoning trace. Shown in the UI to give the
/// user visibility into *why* the agent made each decision (Idea #1 — explainable AI).
/// </summary>
public sealed record AgentReasoningEntry(
    string AgentId,
    string AgentDisplayName,
    string IconClass,
    string Summary,
    long ElapsedMs,
    int? TokensUsed,
    DateTimeOffset Timestamp,
    Activity? Activity);

/// <summary>Aggregate result of a multi-agent workflow run — output + the full reasoning trace.</summary>
public sealed record WorkflowResult<TOut>(
    bool Succeeded,
    TOut Output,
    IReadOnlyList<AgentReasoningEntry> Reasoning,
    long ElapsedMs,
    string? ErrorMessage);
