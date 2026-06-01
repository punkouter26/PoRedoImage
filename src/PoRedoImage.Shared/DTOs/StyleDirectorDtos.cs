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
    DateTimeOffset Timestamp);

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

