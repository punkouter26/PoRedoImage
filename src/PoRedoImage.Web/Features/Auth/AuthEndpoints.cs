using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using System.Security.Claims;

namespace PoRedoImage.Web.Features.Auth;

/// <summary>
/// Auth endpoints: dev sign-in action, Microsoft OIDC challenge, and logout.
/// The login UI lives in Components/Pages/Login.razor at route /login.
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            // Legacy dev-only sign-in alias — kept because Login.razor still uses /dev-login.
            // Canonical entry point is /auth/login/fake (registered below for both Dev and Test).
            app.MapGet("/dev-login", (HttpContext context, IWebHostEnvironment env, string? email, string? guestId, string? returnUrl) =>
                DevLoginAsync(context, env, email, guestId, returnUrl))
            .AllowAnonymous();
        }

        // Trigger Microsoft OIDC challenge — in Dev (no OIDC registered) redirect to dev-login instead
        app.MapGet("/challenge-microsoft", (HttpContext context, IWebHostEnvironment env, string? returnUrl) =>
            ChallengeMicrosoftAsync(context, env, returnUrl))
        .AllowAnonymous();

        // Sign out — both environments
        app.MapGet("/logout", (HttpContext context, IWebHostEnvironment env) =>
            SignOutAsync(context, env))
        .AllowAnonymous();

        // ─── Canonical /auth/* routes (spec §2) ─────────────────────────────────
        // These are the public, documented auth entry points. The legacy routes above
        // (/challenge-microsoft, /dev-login, /logout) remain as back-compat aliases
        // referenced by Login.razor and the existing E2E suites. Both forms share the
        // same handler bodies so behavior is identical.

        app.MapGet("/auth/login/microsoft", (HttpContext context, IWebHostEnvironment env, string? returnUrl) =>
            ChallengeMicrosoftAsync(context, env, returnUrl))
        .AllowAnonymous();

        app.MapGet("/auth/login/fake", (HttpContext context, IWebHostEnvironment env, string? email, string? guestId, string? returnUrl) =>
            DevLoginAsync(context, env, email, guestId, returnUrl))
        .AllowAnonymous();

        app.MapGet("/auth/logout", (HttpContext context, IWebHostEnvironment env) =>
            SignOutAsync(context, env))
        .AllowAnonymous();

        // Server auth state + returnUrl validation. Returns 401 for unauthenticated callers
        // (no redirect, no body leak) so API clients get a clean signal.
        app.MapGet("/auth/me", (HttpContext context, string? returnUrl) =>
        {
            var user = context.User;
            if (user.Identity?.IsAuthenticated != true)
                return Results.Json(new { authenticated = false }, statusCode: StatusCodes.Status401Unauthorized);

            var safeReturn = !string.IsNullOrWhiteSpace(returnUrl)
                && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
                && !returnUrl.StartsWith("//");

            return Results.Json(new
            {
                authenticated = true,
                name = user.Identity.Name,
                email = user.FindFirst(ClaimTypes.Email)?.Value,
                roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
                returnUrlValid = safeReturn
            });
        })
        // Must be reachable anonymously: it is the client's auth-probe and returns its own 401
        // body ({authenticated:false}) rather than triggering the FallbackPolicy login redirect.
        .AllowAnonymous();
    }

    // ─── Shared handlers (legacy + canonical routes) ──────────────────────────

    private static async Task ChallengeMicrosoftAsync(HttpContext context, IWebHostEnvironment env, string? returnUrl)
    {
        // Sanitize returnUrl to prevent open-redirect
        var destination = (!string.IsNullOrWhiteSpace(returnUrl)
            && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
            && !returnUrl.StartsWith("//"))
            ? returnUrl : "/";

        var clientId = context.RequestServices.GetRequiredService<IConfiguration>()["AzureAd:ClientId"];
        var hasOidc = !string.IsNullOrWhiteSpace(clientId);

        if (env.IsDevelopment() && !hasOidc)
        {
            context.Response.Redirect("/login");
            return;
        }

        await context.ChallengeAsync(
            OpenIdConnectDefaults.AuthenticationScheme,
            new AuthenticationProperties { RedirectUri = destination });
    }

    private static async Task DevLoginAsync(HttpContext context, IWebHostEnvironment env, string? email, string? guestId, string? returnUrl)
    {
        if (!env.IsDevelopment() && !env.IsEnvironment("Test"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        // Restore specific GUEST identity from LocalStorage (browser refresh / E2E test resume)
        if (!string.IsNullOrWhiteSpace(guestId) && guestId.StartsWith("GUEST", StringComparison.OrdinalIgnoreCase))
        {
            var userId = $"guest|{guestId}";
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId),
                new(ClaimTypes.Name, guestId),
                new(ClaimTypes.Email, "guest@guest.local"),
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));
            var destination = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
            if (!Uri.IsWellFormedUriString(destination, UriKind.Relative))
                destination = "/";
            context.Response.Redirect(destination);
            return;
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            // Normalise GUEST — anything hitting guest@guest.local becomes a unique GUEST account.
            // Random suffix ensures each GUEST session is distinct in logs and DB (e.g. GUEST463443).
            var isGuest = string.Equals(email, "guest@guest.local", StringComparison.OrdinalIgnoreCase);
            var guestSuffix = Random.Shared.Next(10000000, 99999999).ToString();
            var userId = isGuest ? $"guest|GUEST{guestSuffix}" : $"dev|{email}";
            var displayName = isGuest ? $"GUEST{guestSuffix}" : email;

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId),
                new(ClaimTypes.Name, displayName),
                new(ClaimTypes.Email, email),
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            var destination = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
            if (!Uri.IsWellFormedUriString(destination, UriKind.Relative))
                destination = "/";

            // Append guest ID to URL for LocalStorage persistence on the client side
            if (isGuest)
            {
                var separator = destination.Contains('?') ? '&' : '?';
                destination = $"{destination}{separator}guestId={displayName}";
            }

            context.Response.Redirect(destination);
        }
        else
        {
            context.Response.Redirect("/login");
        }
    }

    private static async Task SignOutAsync(HttpContext context, IWebHostEnvironment env)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (env.IsDevelopment() || env.IsEnvironment("Test"))
        {
            context.Response.Redirect("/login");
        }
        else
        {
            // Triggers Microsoft sign-out redirect; browser is sent to /signout-oidc callback
            await context.SignOutAsync(
                OpenIdConnectDefaults.AuthenticationScheme,
                new AuthenticationProperties { RedirectUri = "/" });
        }
    }
}