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
            // Dev-only sign-in action: /dev-login?email=X signs in and redirects.
            // guest@guest.local is the reserved GUEST identity for one-click bypass and E2E tests.
            // guestId=GUEST12345678 restores a specific GUEST identity from LocalStorage persistence.
            app.MapGet("/dev-login", async (string? email, string? guestId, string? returnUrl, HttpContext context) =>
            {
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
            }).AllowAnonymous();
        }

        // Trigger Microsoft OIDC challenge — in Dev (no OIDC registered) redirect to dev-login instead
        app.MapGet("/challenge-microsoft", async (HttpContext context, IWebHostEnvironment env, string? returnUrl) =>
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
        }).AllowAnonymous();

        // Sign out — both environments
        app.MapGet("/logout", async (HttpContext context, IWebHostEnvironment env) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (env.IsDevelopment())
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
        }).AllowAnonymous();
    }
}