using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Default <see cref="ICaptionBattleService"/> implementation. Fans out one chat
/// completion per persona in parallel using <see cref="IGenerativeAiService"/>,
/// then aggregates results in a stable order so the UI is deterministic.
/// </summary>
/// <remarks>
/// Idea #5 — Meme Caption Battle. Concurrency is bounded to 4 to avoid hammering
/// the upstream model with simultaneous requests. Failed candidates are returned
/// with <see cref="CaptionCandidate.Succeeded"/> = false so the UI can show them
/// as "tried but skipped" rather than dropping them silently.
/// </remarks>
public sealed class CaptionBattleService : ICaptionBattleService
{
    private static readonly CaptionPersona[] _allPersonas =
    {
        CaptionPersona.GenZ,
        CaptionPersona.Corporate,
        CaptionPersona.Absurdist,
        CaptionPersona.DadJoke,
        CaptionPersona.Sarcastic,
        CaptionPersona.Wholesome,
        CaptionPersona.TechBro,
        CaptionPersona.Surreal
    };

    private readonly IGenerativeAiService _ai;
    private readonly ILogger<CaptionBattleService> _logger;

    public IReadOnlyList<CaptionPersona> AllPersonas => _allPersonas;

    public CaptionBattleService(IGenerativeAiService ai, ILogger<CaptionBattleService> logger)
    {
        _ai = ai;
        _logger = logger;
    }

    public async Task<CaptionBattleResult> RunBattleAsync(
        IReadOnlyList<string> tags,
        IReadOnlyList<CaptionPersona>? personas = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var chosen = (personas is { Count: > 0 } ? personas : _allPersonas).Distinct().ToList();
        if (chosen.Count == 0) chosen = _allPersonas.ToList();

        _logger.LogInformation("Starting caption battle. Personas={Count}, Tags={TagCount}",
            chosen.Count, tags.Count);

        var sw = Stopwatch.StartNew();

        // Cap concurrency to 4 — GPT-4o-mini deployments have per-tenant TPM limits.
        using var gate = new SemaphoreSlim(initialCount: 4, maxCount: 4);

        var tasks = chosen.Select(async persona =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var (caption, tokens, elapsed) = await _ai.GenerateCaptionAsync(tags, persona.SystemPrompt(), ct);
                return new CaptionCandidate(
                    Persona: persona,
                    PersonaDisplayName: persona.DisplayName(),
                    IconClass: persona.IconClass(),
                    Caption: caption,
                    TokensUsed: tokens,
                    ElapsedMs: elapsed,
                    Succeeded: true,
                    ErrorMessage: null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Caption battle persona {Persona} failed", persona);
                return new CaptionCandidate(
                    Persona: persona,
                    PersonaDisplayName: persona.DisplayName(),
                    IconClass: persona.IconClass(),
                    Caption: string.Empty,
                    TokensUsed: 0,
                    ElapsedMs: 0,
                    Succeeded: false,
                    ErrorMessage: ex.Message);
            }
            finally { gate.Release(); }
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        sw.Stop();

        var ordered = results
            .OrderBy(c => chosen.IndexOf(c.Persona)) // preserve call order
            .ToList();

        _logger.LogInformation("Caption battle complete. Requested={Requested}, Succeeded={Succeeded}, Elapsed={Elapsed}ms",
            chosen.Count, ordered.Count(c => c.Succeeded), sw.ElapsedMilliseconds);

        return new CaptionBattleResult(
            Candidates: ordered,
            Requested: chosen.Count,
            Succeeded: ordered.Count(c => c.Succeeded),
            ElapsedMs: sw.ElapsedMilliseconds);
    }
}
