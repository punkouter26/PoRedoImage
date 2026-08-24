using System.Diagnostics;

namespace PoRedoImage.Application.Agents.StyleDirector;

/// <summary>
/// A single line in the workflow's reasoning trace shown in the UI.
/// </summary>
public sealed record AgentReasoningEntry(
    string AgentId,
    string AgentDisplayName,
    string IconClass,
    string Summary,
    long ElapsedMs,
    int? TokensUsed,
    DateTimeOffset Timestamp,
    Activity? Activity,
    string? FallbackReason = null);

/// <summary>Aggregate result of the style director workflow run.</summary>
public sealed record WorkflowResult<TOut>(
    bool Succeeded,
    TOut Output,
    IReadOnlyList<AgentReasoningEntry> Reasoning,
    long ElapsedMs,
    string? ErrorMessage);

/// <summary>
/// Step 1 — Vision Analyst input. We hand the agent the raw image bytes plus
/// whatever the upstream Computer Vision service has already produced, so it can
/// focus on higher-level interpretation rather than re-running OCR.
/// </summary>
public sealed record VisionAnalystInput(
    byte[] ImageBytes,
    IReadOnlyList<string>? DetectedTags,
    double ConfidenceScore);

/// <summary>
/// Step 1 output. A short, evocative description of *what* the image is and
/// the mood it conveys — the seed for the Style Strategist's reasoning.
/// </summary>
public sealed record VisionAnalystOutput(
    string Subject,
    string Mood,
    IReadOnlyList<string> Themes);

/// <summary>
/// Step 2 — Style Strategist input/output. The strategist picks a coherent set
/// of style directions (palette, era, technique) that match the mood.
/// </summary>
public sealed record StyleStrategistInput(VisionAnalystOutput Vision);
public sealed record StyleStrategistOutput(
    IReadOnlyList<StyleDirection> Directions,
    string Rationale);

public sealed record StyleDirection(
    string Name,         // "Vaporwave Neon", "Studio Ghibli Watercolor", etc.
    string Palette,      // free-form description
    string Technique,    // "soft brush, low contrast, painterly"
    string ReferenceEra);// "1980s", "1970s", etc.

/// <summary>
/// Step 3 — Prompt Refiner input/output. Takes a direction and turns it into
/// a single, concrete Imagen 3 prompt that a model can execute.
/// </summary>
public sealed record PromptRefinerInput(StyleStrategistOutput Strategy);
/// <param name="SelfCritiqueConfidence">
/// How sure the refiner is about its own prompt, 0–100. Present so the Critic can skip a second
/// round-trip to the same model when the refiner already checked its work — see
/// <c>StyleDirectorWorkflow</c>. Defaults to 0, which means "unknown, please critique properly",
/// so the heuristic refiner path keeps the full four-call behaviour.
/// </param>
public sealed record PromptRefinerOutput(
    string Prompt,
    string WhyThisPrompt,
    int SelfCritiqueConfidence = 0);

/// <summary>
/// Step 4 — Critic input/output. Self-critique step — a second pass that
/// sanity-checks the prompt for things like "would this render a person
/// without facial features" or "is this too generic".
/// </summary>
/// <param name="SkipModelCall">
/// True when the refiner already self-critiqued with high confidence. The Critic then runs its
/// deterministic path only — which is not a downgrade: the guards that actually matter for
/// image-to-image (subject preservation, the anti-watermark clause) are enforced in code, not by
/// the model, precisely so they cannot be talked out of.
/// </param>
public sealed record CriticInput(PromptRefinerOutput Refined)
{
    public bool SkipModelCall { get; init; }
}
public sealed record CriticOutput(
    string FinalPrompt,
    string Critique,
    int Confidence,         // 0..100
    IReadOnlyList<string> Suggestions);

/// <summary>
/// Final workflow output — the prompt the director recommends, plus the
/// full reasoning trace for the UI.
/// </summary>
public sealed record StyleDirectorResult(
    CriticOutput Decision,
    VisionAnalystOutput Vision,
    StyleStrategistOutput Strategy,
    PromptRefinerOutput Refined);
