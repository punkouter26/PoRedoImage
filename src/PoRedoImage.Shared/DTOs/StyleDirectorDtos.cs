namespace PoRedoImage.Shared.DTOs;

public record StyleDirectionDto(
    string Name,
    string Palette,
    string Technique,
    string ReferenceEra);

public record StyleDirectorRequestDto(
    string ImageData,
    string ContentType,
    IReadOnlyList<string> DetectedTags,
    double ConfidenceScore);

public record StyleDirectorReasoningEntryDto(
    string AgentId,
    string AgentDisplayName,
    string IconClass,
    string Summary,
    long ElapsedMs,
    int? TokensUsed,
    DateTimeOffset Timestamp,
    /// <summary>
    /// Why this step ran without the model, or null when the model produced it. Carried to the
    /// browser so the trace can say which of the two the user is reading — the AI and rule-based
    /// paths are otherwise indistinguishable on screen.
    /// </summary>
    string? FallbackReason = null);

public record StyleDirectorResultDto(
    bool Succeeded,
    string? ErrorMessage,
    string? FinalPrompt,
    string? Critique,
    int? Confidence,
    string? Subject,
    string? Mood,
    IReadOnlyList<string>? Themes,
    IReadOnlyList<StyleDirectionDto>? Directions,
    string? StrategyRationale,
    string? WhyThisPrompt,
    IReadOnlyList<string>? Suggestions,
    long ElapsedMs,
    IReadOnlyList<StyleDirectorReasoningEntryDto> Reasoning);

