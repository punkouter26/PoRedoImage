using PoRedoImage.Domain.Entities;

namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// One caption candidate produced by the Meme Caption Battle (Idea #5).
/// </summary>
public sealed record CaptionCandidate(
    CaptionPersona Persona,
    string PersonaDisplayName,
    string IconClass,
    string Caption,
    int TokensUsed,
    long ElapsedMs,
    bool Succeeded,
    string? ErrorMessage);

/// <summary>
/// The full result of a single battle run.
/// </summary>
public sealed record CaptionBattleResult(
    IReadOnlyList<CaptionCandidate> Candidates,
    int Requested,
    int Succeeded,
    long ElapsedMs);

/// <summary>
/// Domain service for the Meme Caption Battle (Idea #5).
/// Generates N parallel caption candidates, each driven by a different persona,
/// and returns them in a deterministic order so the UI can render the same
/// grid on every refresh.
/// </summary>
public interface ICaptionBattleService
{
    /// <summary>
    /// Runs the battle. Each persona produces one candidate in parallel.
    /// </summary>
    /// <param name="tags">Image tags from vision analysis — used to seed each candidate.</param>
    /// <param name="personas">Subset of personas to use; null = all 8.</param>
    Task<CaptionBattleResult> RunBattleAsync(
        IReadOnlyList<string> tags,
        IReadOnlyList<CaptionPersona>? personas = null,
        CancellationToken ct = default);

    /// <summary>The full set of personas the service can use.</summary>
    IReadOnlyList<CaptionPersona> AllPersonas { get; }
}
