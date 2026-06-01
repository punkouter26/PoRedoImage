using Microsoft.AspNetCore.Mvc;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Web.Features.CaptionBattle;

/// <summary>
/// Minimal API endpoints for the Meme Caption Battle (Idea #5).
/// POST /api/caption-battle/run  → fan out N personas in parallel, return all candidates
/// POST /api/caption-battle/vote → record the user's winning persona (lightweight, in-memory)
/// </summary>
/// <remarks>
/// Vote counts are kept in a process-local dictionary (no persistence) — enough to drive
/// a "humor profile" UI hint. A future iteration will back this with Table Storage
/// alongside the user's Style DNA (Idea #7).
/// </remarks>
public static class CaptionBattleEndpoints
{
    // Process-local vote tallies. Per persona, per user (when authenticated) — keyed by user id,
    // "anon" for guest sessions. Resets on process restart; that is acceptable for the
    // lightweight "humor profile" affordance, which is meant to feel like a session reward,
    // not a permanent profile.
    private static readonly Dictionary<string, Dictionary<string, int>> _votesByUser = new();

    public static IEndpointRouteBuilder MapCaptionBattleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/caption-battle")
            .WithTags("CaptionBattle")
            .RequireRateLimiting("ai-endpoints");

        group.MapPost("/run", async (
            CaptionBattleRequest request,
            ICaptionBattleService battle,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("CaptionBattleEndpoints");

            if (request.Tags is null || request.Tags.Count == 0)
                return Results.BadRequest("At least one tag is required to seed the caption battle.");

            // Translate persona name strings to the enum; null/empty → all 8.
            List<CaptionPersona>? personas = null;
            if (request.Personas is { Count: > 0 })
            {
                personas = new List<CaptionPersona>(request.Personas.Count);
                foreach (var name in request.Personas)
                {
                    if (Enum.TryParse<CaptionPersona>(name, ignoreCase: true, out var p))
                        personas.Add(p);
                }
                if (personas.Count == 0) personas = null; // unknown names → fall back to all
            }

            try
            {
                var result = await battle.RunBattleAsync(request.Tags, personas, ct);
                logger.LogInformation("Caption battle served. Requested={Requested}, Succeeded={Succeeded}, Elapsed={Elapsed}ms",
                    result.Requested, result.Succeeded, result.ElapsedMs);
                return Results.Ok(result.ToDto());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Caption battle failed");
                return Results.Problem("Caption battle failed.", statusCode: 500, title: "Battle Error");
            }
        })
        .WithName("RunCaptionBattle")
        .WithSummary("Run a Meme Caption Battle — fan out N personas in parallel (Idea #5)")
        .Produces<CaptionBattleResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        group.MapPost("/vote", (
            CaptionVoteRequest request,
            HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(request.Persona))
                return Results.BadRequest("Persona is required.");
            if (!Enum.TryParse<CaptionPersona>(request.Persona, ignoreCase: true, out _))
                return Results.BadRequest($"Unknown persona '{request.Persona}'.");

            var userKey = context.User.Identity?.IsAuthenticated == true
                ? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anon"
                : "anon";

            lock (_votesByUser)
            {
                if (!_votesByUser.TryGetValue(userKey, out var tally))
                {
                    tally = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    _votesByUser[userKey] = tally;
                }
                tally[request.Persona] = tally.GetValueOrDefault(request.Persona) + 1;
            }

            return Results.NoContent();
        })
        .WithName("RecordCaptionVote")
        .WithSummary("Record the user's winning persona for the lightweight humor-profile tally");

        return app;
    }
}
