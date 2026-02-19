using Serilog.Context;

namespace PoImageGc.Web.Features.Diagnostics;

/// <summary>
/// Middleware that propagates or generates a Correlation ID for each HTTP request.
/// Follows the W3C-style convention of passing it via the X-Correlation-ID header.
///
/// Behaviour:
/// - Reads X-Correlation-ID from the incoming request header.
/// - Generates a new GUID (format D) when the header is absent.
/// - Echoes the Correlation ID back in the response header.
/// - Pushes the Correlation ID into Serilog's LogContext so every log entry
///   emitted during the request includes {CorrelationId}.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Use the caller-supplied ID or mint a fresh one
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Guid.NewGuid().ToString("D");

        // Echo back to the caller and store on Items for downstream access
        context.Response.Headers[HeaderName] = correlationId;
        context.Items[HeaderName] = correlationId;

        // Push into Serilog LogContext for the lifetime of this request
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
