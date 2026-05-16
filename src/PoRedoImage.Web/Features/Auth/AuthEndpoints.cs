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
            // anon@anon.local is the reserved ANON identity for one-click bypass and E2E tests.
            app.MapGet("/dev-login", async (string? email, string? returnUrl, HttpContext context) =>
            {
                if (!string.IsNullOrWhiteSpace(email))
                {
                    // Normalise ANON — anything hitting anon@anon.local becomes a unique ANON account.
                    // Random suffix ensures each ANON session is distinct in logs and DB (e.g. ANON463443).
                    var isAnon = string.Equals(email, "anon@anon.local", StringComparison.OrdinalIgnoreCase);
                    var anonSuffix = Random.Shared.Next(100000, 999999).ToString();
                    var userId = isAnon ? $"anon|ANON{anonSuffix}" : $"dev|{email}";
                    var displayName = isAnon ? $"ANON{anonSuffix}" : email;

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
