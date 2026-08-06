using Microsoft.AspNetCore.Antiforgery;

namespace PoRedoImage.Web.Features.Shared;

/// <summary>
/// Issues the antiforgery request token the Blazor WASM client echoes back on every write.
/// </summary>
/// <remarks>
/// The double-submit pair is split deliberately: <c>GetAndStoreTokens</c> writes the secret half to
/// a <c>HttpOnly</c> cookie (unreadable from JavaScript, which is the point) and returns the request
/// half, which is what the browser must send back in a header. A SPA therefore needs a route to
/// fetch that request half — it cannot read the cookie. Anonymous because the client boots and
/// primes its token before the user has signed in.
/// </remarks>
public static class AntiforgeryEndpoints
{
    public static IEndpointRouteBuilder MapAntiforgeryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/antiforgery/token", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            // Never cache: the token is bound to this caller's antiforgery cookie.
            context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
            return Results.Ok(new AntiforgeryTokenDto(tokens.RequestToken ?? string.Empty));
        })
        .WithTags("Security")
        .WithName("GetAntiforgeryToken")
        .WithSummary("Issue an antiforgery request token for the calling browser session")
        .AllowAnonymous()
        .ExcludeFromDescription();

        return app;
    }
}

/// <summary>The request half of the antiforgery token pair.</summary>
public sealed record AntiforgeryTokenDto(string Token);
