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
            // The login UI (Login.razor) posts to this endpoint.
            app.MapGet("/dev-login", async (string? email, string? returnUrl, HttpContext context) =>
            {
                if (!string.IsNullOrWhiteSpace(email))
                {
                    var claims = new List<Claim>
                    {
                        new(ClaimTypes.NameIdentifier, $"dev|{email}"),
                        new(ClaimTypes.Name, email),
                        new(ClaimTypes.Email, email),
                    };
                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    await context.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(identity));

                    var destination = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
                    // Prevent open-redirect: only allow relative paths
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

        // Trigger Microsoft OIDC challenge — both environments (no-op in dev since OIDC not configured)
        app.MapGet("/challenge-microsoft", async (HttpContext context, string? returnUrl) =>
        {
            // Sanitize returnUrl to prevent open-redirect
            var destination = (!string.IsNullOrWhiteSpace(returnUrl)
                && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
                && !returnUrl.StartsWith("//"))
                ? returnUrl : "/";

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
