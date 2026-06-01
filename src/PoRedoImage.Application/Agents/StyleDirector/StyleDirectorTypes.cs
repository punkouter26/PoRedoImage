namespace PoRedoImage.Application.Agents.StyleDirector;

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
public sealed record PromptRefinerOutput(
    string Prompt,
    string WhyThisPrompt);

/// <summary>
/// Step 4 — Critic input/output. Self-critique step — a second pass that
/// sanity-checks the prompt for things like "would this render a person
/// without facial features" or "is this too generic".
/// </summary>
public sealed record CriticInput(PromptRefinerOutput Refined);
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
