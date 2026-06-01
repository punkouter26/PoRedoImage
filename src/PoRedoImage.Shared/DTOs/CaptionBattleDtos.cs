using PoRedoImage.Domain.Entities;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Shared.DTOs;

public record CaptionCandidateDto(
    string Persona,
    string PersonaDisplayName,
    string IconClass,
    string Caption,
    int TokensUsed,
    long ElapsedMs,
    bool Succeeded,
    string? ErrorMessage);

public record CaptionBattleResultDto(
    IReadOnlyList<CaptionCandidateDto> Candidates,
    int Requested,
    int Succeeded,
    long ElapsedMs);

public record CaptionBattleRequest(
    IReadOnlyList<string> Tags,
    IReadOnlyList<string>? Personas);

public record CaptionVoteRequest(
    string ImageId,
    string Persona);

public static class CaptionBattleMappingExtensions
{
    public static CaptionCandidateDto ToDto(this CaptionCandidate c) => new(
        Persona: c.Persona.ToString(),
        PersonaDisplayName: c.PersonaDisplayName,
        IconClass: c.IconClass,
        Caption: c.Caption,
        TokensUsed: c.TokensUsed,
        ElapsedMs: c.ElapsedMs,
        Succeeded: c.Succeeded,
        ErrorMessage: c.ErrorMessage);

    public static CaptionBattleResultDto ToDto(this CaptionBattleResult result) => new(
        Candidates: result.Candidates.Select(c => c.ToDto()).ToList(),
        Requested: result.Requested,
        Succeeded: result.Succeeded,
        ElapsedMs: result.ElapsedMs);
}
