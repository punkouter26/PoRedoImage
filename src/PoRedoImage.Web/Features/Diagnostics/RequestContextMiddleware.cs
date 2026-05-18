using Serilog.Context;

namespace PoRedoImage.Web.Features.Diagnostics;

/// <summary>
/// Middleware that combines correlation ID propagation and user/session context into one pipeline hop.
/// - Reads or generates X-Correlation-ID and echoes it in the response header.
/// - Pushes CorrelationId, UserId, and SessionId into Serilog LogContext for every log entry.
/// </summary>
public sealed class RequestContextMiddleware
{
    private const string CorrelationHeader = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public RequestContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationHeader].FirstOrDefault()
            ?? Guid.NewGuid().ToString("D");

        context.Response.Headers[CorrelationHeader] = correlationId;
        context.Items[CorrelationHeader] = correlationId;

        var userId = context.User?.Identity?.Name ?? "anonymous";
        var sessionId = context.TraceIdentifier;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("UserId", userId))
        using (LogContext.PushProperty("SessionId", sessionId))
        {
            await _next(context);
        }
    }
}
