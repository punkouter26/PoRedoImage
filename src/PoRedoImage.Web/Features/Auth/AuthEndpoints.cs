using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using System.Security.Claims;
using PoRedoImage.Shared.Configuration;
using PoRedoImage.Web.Configuration;

namespace PoRedoImage.Web.Features.Auth;

/// <summary>
/// Auth endpoints: dev sign-in action, Microsoft OIDC challenge, and logout.
/// The login UI lives in Components/Pages/Login.razor at route /login.
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        // Dev/Test-only sign-in bypass, used by Login.razor's GUEST button and by both E2E suites.
        // Registered for Dev OR Test so the route exists wherever DevLoginAsync is willing to serve
        // it — the handler 404s on its own in any other environment, so this is belt-and-braces.
        //
        // A parallel set of "canonical" routes (/auth/login/fake, /auth/login/microsoft,
        // /auth/logout, /auth/me) used to be registered alongside these, sharing the same handler
        // bodies. Nothing ever called them: every caller in src/ and tests/ used the three routes
        // below, and /auth/me had no consumer at all despite carrying an IL2026 suppression to
        // exist. They were deleted rather than migrated to — these are the real entry points.
        if (app.Environment.IsDevOrTest())
        {
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
    }

    // ─── Shared handlers ──────────────────────────────────────────────────────

    private static async Task ChallengeMicrosoftAsync(HttpContext context, IWebHostEnvironment env, string? returnUrl)
    {
        // Sanitize returnUrl to prevent open-redirect
        var destination = (!string.IsNullOrWhiteSpace(returnUrl)
            && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
            && !returnUrl.StartsWith("//"))
            ? returnUrl : "/";

        var clientId = context.RequestServices.GetRequiredService<IConfiguration>()[ConfigKeys.AzureAdClientId];
        var hasOidc = !string.IsNullOrWhiteSpace(clientId);

        if (!hasOidc)
        {
            if (env.IsDevelopment() || env.IsEnvironment("Test"))
            {
                // In Dev/Test without Azure AD ClientId configured, simulate MS sign-in via dev-login
                var devDestination = $"/dev-login?email=developer%40microsoft.local&returnUrl={Uri.EscapeDataString(destination)}";
                context.Response.Redirect(devDestination);
                return;
            }

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