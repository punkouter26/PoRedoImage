namespace PoRedoImage.Client.Shared;

/// <summary>
/// One feature run — what it is doing and how far along it is, if that is even knowable.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. Every feature page hand-rolled the same run as four loose fields —
/// <c>isProcessing</c>, <c>isComplete</c>, <c>progressPercentage</c>, <c>progressMessage</c> — with
/// nothing tying them together, so the illegal combinations were all reachable and at least one was
/// reached: a page could sit at <c>isProcessing=false, isComplete=false, progressPercentage=85</c>
/// after a thrown exception. <see cref="BoardStatus"/> already named the four states this app
/// allows; it just had no owner outside the progress bar.
/// </para>
/// <para>
/// AND WHY <see cref="Determinate"/> IS NULLABLE. The percentages those pages published were
/// invented — Rap Roast stepped 15 → 45 → 85 → 100 and Style Director 10 → 40 → 100, in both cases
/// with no relationship to the work. The visible cost is a bar that parks at 85% for as long as the
/// model takes and then jumps, which reads as a stall. Null means "running, length unknown" and
/// renders as an indeterminate bar, which is the true statement. Bulk Generate, which really does
/// know it has finished 3 of 10, is the only caller that should ever set a number.
/// </para>
/// </remarks>
public sealed class FeatureRunState
{
    /// <summary>Where the run is. Starts <see cref="BoardStatus.Queued"/> — nothing has run yet.</summary>
    public BoardStatus Status { get; private set; } = BoardStatus.Queued;

    /// <summary>What the run is doing right now, in the user's words.</summary>
    public string Stage { get; private set; } = string.Empty;

    /// <summary>
    /// Completion 0–100 when the caller genuinely knows it, else null for an indeterminate bar.
    /// Do not populate this with a guess; that is the defect this type was written to remove.
    /// </summary>
    public int? Determinate { get; private set; }

    /// <summary>Why the run failed, when it did.</summary>
    public string? Error { get; private set; }

    public bool IsRunning => Status == BoardStatus.Working;
    public bool IsComplete => Status == BoardStatus.Done;
    public bool IsFailed => Status == BoardStatus.Failed;

    /// <summary>Begin a run. Clears any previous result or error — the four fields moved together.</summary>
    public void Start(string stage)
    {
        Status = BoardStatus.Working;
        Stage = stage;
        Determinate = null;
        Error = null;
    }

    /// <summary>
    /// Update the stage text of a running job, optionally with REAL progress.
    /// </summary>
    /// <param name="determinate">
    /// Only pass this when the number is measured (n of m completed). Omit it otherwise.
    /// </param>
    public void Advance(string stage, int? determinate = null)
    {
        if (Status != BoardStatus.Working) return;
        Stage = stage;
        if (determinate is { } d) Determinate = Math.Clamp(d, 0, 100);
    }

    /// <summary>Finish successfully.</summary>
    public void Succeed(string stage = "")
    {
        Status = BoardStatus.Done;
        Stage = stage;
        Determinate = 100;
        Error = null;
    }

    /// <summary>Finish unsuccessfully. The reason is required — a failed run with no reason is the
    /// silent degradation this codebase treats as a defect.</summary>
    public void Fail(string error)
    {
        Status = BoardStatus.Failed;
        Stage = string.Empty;
        Determinate = null;
        Error = error;
    }

    /// <summary>Back to the start, e.g. when the user picks a different image.</summary>
    public void Reset()
    {
        Status = BoardStatus.Queued;
        Stage = string.Empty;
        Determinate = null;
        Error = null;
    }
}
