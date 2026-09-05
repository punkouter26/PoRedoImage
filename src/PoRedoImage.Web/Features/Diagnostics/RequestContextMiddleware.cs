using Serilog.Context;

namespace PoRedoImage.Web.Features.Diagnostics;

/// <summary>
/// Middleware that combines correlation ID propagation and session context into one pipeline hop.
/// - Reads the client-stamped X-Correlation-ID / X-Session-ID (§6.9), generating fallbacks, and echoes
///   both in the response headers.
/// - Pushes CorrelationId and SessionId into Serilog LogContext for every log entry.
/// </summary>
/// <remarks>
/// This runs BEFORE <c>UseAuthentication()</c> — it has to, because the response headers it echoes
/// must be set before anything can start writing a response, and because a request rejected by the
/// rate limiter (which also sits ahead of authentication) still needs a correlation id.
/// <para>
/// That ordering is exactly why <c>UserId</c> is NOT pushed here. It used to be, reading
/// <c>context.User.Identity.Name</c> at a point in the pipeline where the cookie has not been
/// decoded yet, so the property was the literal string "anonymous" on every log line ever written —
/// 26,247 of 26,247 entries on a day with 53 successful logins. User identity is pushed by
/// <see cref="UserContextMiddleware"/>, which is registered immediately after
/// <c>UseAuthentication()</c>.
/// </para>
/// </remarks>
public sealed class RequestContextMiddleware
{
    /// <summary>Correlation header name, shared with <see cref="OutboundCorrelationHandler"/>.</summary>
    public const string CorrelationHeader = "X-Correlation-ID";

    /// <summary>Session header name, shared with <see cref="OutboundCorrelationHandler"/>.</summary>
    public const string SessionHeader = "X-Session-ID";

    private readonly RequestDelegate _next;

    public RequestContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationHeader].FirstOrDefault()
            ?? Guid.NewGuid().ToString("D");

        // Prefer the client-stamped session id (stable per browser tab); fall back to the per-request
        // trace id when the caller isn't our WASM client (e.g. curl, health probes).
        var sessionId = context.Request.Headers[SessionHeader].FirstOrDefault()
            ?? context.TraceIdentifier;

        context.Response.Headers[CorrelationHeader] = correlationId;
        context.Response.Headers[SessionHeader] = sessionId;
        // Both land in Items so OutboundCorrelationHandler can re-stamp them on downstream
        // calls without re-deriving the fallbacks (§3 "through all HTTP calls").
        context.Items[CorrelationHeader] = correlationId;
        context.Items[SessionHeader] = sessionId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("SessionId", sessionId))
        {
            await _next(context);
        }
    }
}
