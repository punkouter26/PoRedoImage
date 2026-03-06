using Serilog.Context;

namespace PoRedoImage.Web.Features.Diagnostics;

/// <summary>
/// Pushes UserId and SessionId into Serilog's LogContext for the lifetime of each request
/// so every log entry emitted during the request automatically carries these properties.
/// UserId resolves from the authenticated identity, falling back to "anonymous".
/// SessionId uses the ASP.NET Core TraceIdentifier (unique per connection/request).
/// </summary>
public sealed class UserContextMiddleware
{
    private readonly RequestDelegate _next;

    public UserContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.User?.Identity?.Name ?? "anonymous";
        var sessionId = context.TraceIdentifier;

        using (LogContext.PushProperty("UserId", userId))
        using (LogContext.PushProperty("SessionId", sessionId))
        {
            await _next(context);
        }
    }
}
