using System.Security.Claims;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Web.Features.Shared;

namespace PoRedoImage.Web.Features.Diagnostics;

/// <summary>
/// Client-vitals collection and history, inside the Diagnostics slice.
/// </summary>
/// <remarks>
/// The two routes are mapped separately rather than sharing one group because their audiences
/// differ. <b>Writing</b> a sample is something every signed-in user's browser does on every page
/// load, so the POST only needs an authenticated principal. <b>Reading</b> the history exposes
/// aggregate usage across all users, so the GET sits behind the same
/// <see cref="AuthorizationPolicies.Diagnostics"/> allow-list that guards <c>/api/diag</c>.
/// </remarks>
public static class VitalsEndpoints
{
    /// <summary>Upper bound on a history query, so a caller cannot ask for an unbounded scan.</summary>
    internal const int MaxHistoryDays = 90;

    /// <summary>Upper bound on returned samples, matching the dashboard's window.</summary>
    internal const int MaxHistorySamples = 2000;

    public static void MapVitalsEndpoints(this WebApplication app)
    {
        // ── Write ────────────────────────────────────────────────────────────────
        // Any authenticated user. Rate-limited on its own cheap policy: this endpoint is far
        // less costly than an AI call, but it is an unauthenticated-shaped write path into
        // storage, so it must not be free to spam.
        app.MapPost("/api/diag/vitals", SaveVitalsAsync)
            .WithName("SaveClientVitals")
            .WithSummary("Record one browser-measured page-load sample")
            .WithTags("Diagnostics")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization()
            .RequireRateLimiting("telemetry")
            .AddEndpointFilter<ValidationFilter<ClientVitalsSampleRequest>>();

        // ── Read ─────────────────────────────────────────────────────────────────
        app.MapGet("/api/diag/vitals", GetVitalsAsync)
            .WithName("GetClientVitalsHistory")
            .WithSummary("Read recent client-vitals samples, newest first")
            .WithTags("Diagnostics")
            .Produces<ClientVitalsHistoryDto>(StatusCodes.Status200OK)
            .RequireAuthorization(AuthorizationPolicies.Diagnostics);
    }

    private static async Task<IResult> SaveVitalsAsync(
        ClientVitalsSampleRequest request,
        HttpContext context,
        IClientVitalsRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        // Identity is taken from the principal and the correlation middleware — never from the
        // body — so a client cannot attribute its samples to another user or session.
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? context.User.Identity?.Name
                     ?? "unknown";
        var sessionId = context.Items[RequestContextMiddleware.SessionHeader] as string
                        ?? context.TraceIdentifier;

        var sample = new ClientVitalsSample(
            Timestamp: DateTimeOffset.UtcNow,
            UserId: userId,
            SessionId: sessionId,
            Route: request.Route,
            InteractiveMs: request.InteractiveMs,
            LoadMs: request.LoadMs,
            DomContentLoadedMs: request.DomContentLoadedMs,
            Cls: request.Cls,
            JsHeapMb: request.JsHeapMb,
            WasmHeapMb: request.WasmHeapMb);

        try
        {
            await repository.SaveAsync(sample, ct);
        }
        catch (Exception ex)
        {
            // Telemetry must never degrade the experience it is measuring. Log and accept:
            // the client fires this and forgets, so a 5xx here would be noise it cannot act on.
            loggerFactory.CreateLogger(typeof(VitalsEndpoints))
                .LogWarning(ex, "Client vitals sample for {Route} was dropped.", sample.Route);
        }

        return Results.Accepted();
    }

    private static async Task<IResult> GetVitalsAsync(
        IClientVitalsRepository repository,
        CancellationToken ct,
        int days = 30,
        int max = 500)
    {
        days = Math.Clamp(days, 1, MaxHistoryDays);
        max = Math.Clamp(max, 1, MaxHistorySamples);

        var samples = await repository.GetRecentAsync(days, max, ct);

        return Results.Ok(new ClientVitalsHistoryDto(
            Days: days,
            Count: samples.Count,
            GeneratedAt: DateTimeOffset.UtcNow,
            Samples: [.. samples.Select(s => new ClientVitalsPointDto(
                s.Timestamp, s.Route, s.InteractiveMs, s.LoadMs, s.DomContentLoadedMs,
                s.Cls, s.JsHeapMb, s.WasmHeapMb))]));
    }
}
