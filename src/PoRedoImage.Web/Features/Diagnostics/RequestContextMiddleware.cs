using Serilog.Context;

namespace PoRedoImage.Web.Features.Diagnostics;

/// <summary>
/// Middleware that combines correlation ID propagation and user/session context into one pipeline hop.
/// - Reads the client-stamped X-Correlation-ID / X-Session-ID (§6.9), generating fallbacks, and echoes
///   both in the response headers.
/// - Pushes CorrelationId, UserId, and SessionId into Serilog LogContext for every log entry.
/// </summary>
public sealed class RequestContextMiddleware
{
    private const string CorrelationHeader = "X-Correlation-ID";
    private const string SessionHeader = "X-Session-ID";
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
        context.Items[CorrelationHeader] = correlationId;

        var userId = context.User?.Identity?.Name ?? "anonymous";

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("UserId", userId))
        using (LogContext.PushProperty("SessionId", sessionId))
        {
            await _next(context);
        }
    }
}
