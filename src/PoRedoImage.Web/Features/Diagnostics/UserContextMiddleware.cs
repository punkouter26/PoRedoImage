using System.Security.Claims;
using Serilog.Context;

namespace PoRedoImage.Web.Features.Diagnostics;

/// <summary>
/// Pushes the authenticated user's identity into the Serilog <see cref="LogContext"/> for the rest
/// of the request.
/// </summary>
/// <remarks>
/// Must be registered AFTER <c>UseAuthentication()</c>. This is the whole reason the middleware
/// exists as a separate hop from <see cref="RequestContextMiddleware"/>: that one runs ahead of
/// authentication (it echoes correlation headers and must survive a rate-limiter rejection), so
/// reading <c>context.User</c> there yields an unpopulated principal and every log line is stamped
/// "anonymous" regardless of who is signed in.
/// <para>
/// The value prefers the stable <see cref="ClaimTypes.NameIdentifier"/> — <c>dev|…</c>,
/// <c>guest|GUEST…</c>, or the Entra object id — over the display name, so log lines can be
/// correlated to a user across sessions even when the display name changes.
/// </para>
/// </remarks>
public sealed class UserContextMiddleware
{
    /// <summary>Value used when no authenticated principal is attached to the request.</summary>
    public const string AnonymousUserId = "anonymous";

    private readonly RequestDelegate _next;

    public UserContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        using (LogContext.PushProperty("UserId", ResolveUserId(context.User)))
        {
            await _next(context);
        }
    }

    /// <summary>Resolves the log-facing user id for <paramref name="user"/>.</summary>
    internal static string ResolveUserId(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return AnonymousUserId;

        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(id))
            return id;

        return string.IsNullOrWhiteSpace(user.Identity.Name) ? AnonymousUserId : user.Identity.Name;
    }
}
